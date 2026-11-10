using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlFichajes.API.Tests;

public class MfaLoginTests
{
    private const string Clave = "ClaveNueva1!";
    private const string Origen = "http://127.0.0.1:5173";
    private static readonly DateTime Instante = DateTime.SpecifyKind(DateTime.UnixEpoch.AddSeconds(1_700_000_015), DateTimeKind.Utc);

    [Fact]
    public void Totp_AceptaElPasoActualYElAnterior_YRechazaElFuturo()
    {
        var secreto = RandomNumberGenerator.GetBytes(20);
        Assert.Equal(1, ValidadorTotp.VentanaAnterior);
        Assert.Equal(0, ValidadorTotp.VentanaFutura);
        Assert.Equal(30, ValidadorTotp.PasoSegundos);
        Assert.Equal(6, ValidadorTotp.Digitos);

        Assert.True(ValidadorTotp.Verificar(secreto, ValidadorTotp.CodigoEn(secreto, Instante), Instante).Coincide);
        Assert.True(ValidadorTotp.Verificar(secreto, ValidadorTotp.CodigoEn(secreto, Instante.AddSeconds(-30)), Instante).Coincide);
        Assert.False(ValidadorTotp.Verificar(secreto, ValidadorTotp.CodigoEn(secreto, Instante.AddSeconds(30)), Instante).Coincide);
        Assert.False(ValidadorTotp.Verificar(secreto, ValidadorTotp.CodigoEn(secreto, Instante.AddSeconds(-60)), Instante).Coincide);
    }

    [Fact]
    public async Task SinMfa_SuperAdminYAgente_ConservanElContrato()
    {
        await using var context = Memoria();
        var usuario = await UsuarioAsync(context, rol: "SuperAdmin");
        var http = Http(false);
        var controlador = Controlador(context, http);

        var result = await controlador.Login(new LoginRequestDto { Email = usuario.Correo, Password = Clave });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AuthResponseDto>(ok.Value);
        Assert.Equal("Autenticación exitosa", body.Mensaje);
        Assert.False(body.RequiereCambioPassword);
        Assert.Equal(0, await context.DesafioMfa.CountAsync());
        Assert.False(usuario.MfaObligatorio);
        Assert.False(usuario.TotpHabilitado);

        var sucursal = new Sucursal { Nombre = "Central", EmpresaId = 1, SerialLector = "lector-mfa" };
        context.Sucursal.Add(sucursal);
        await context.SaveChangesAsync();
        var agente = new AgenteInstalacion { ClientId = "agente-mfa", Nombre = "Agente", SucursalId = sucursal.Id, Activo = true };
        agente.SecretHash = new PasswordHasher<AgenteInstalacion>().HashPassword(agente, "SecretoAgente1234");
        context.AgenteInstalacion.Add(agente);
        await context.SaveChangesAsync();
        var agenteResult = await controlador.LoginAgente(new AgenteLoginDto { ClientId = "agente-mfa", ClientSecret = "SecretoAgente1234" });
        var agenteOk = Assert.IsType<OkObjectResult>(agenteResult);
        var agenteBody = Assert.IsType<AuthResponseDto>(agenteOk.Value);
        Assert.Equal("Autenticación exitosa", agenteBody.Mensaje);
        Assert.Equal("agent", Leer(agenteBody.Token).Claims.Single(c => c.Type == "token_use").Value);
        Assert.DoesNotContain(Leer(agenteBody.Token).Claims, c => c.Type == "sid");
    }

    [Fact]
    public async Task PasswordCorrectaConMfa_NoEntregaSesion()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var http = Http(true);
        var controlador = Controlador(context, http, armado);

        var result = await controlador.Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("mfa_requerido", Texto(unauthorized.Value, "codigo"));
        Assert.Null(unauthorized.Value!.GetType().GetProperty("mensaje"));
        Assert.Null(unauthorized.Value.GetType().GetProperty("token"));
        var desafio = Texto(unauthorized.Value, "desafio");
        Assert.DoesNotContain(".", desafio);
        Assert.False(new JwtSecurityTokenHandler().CanReadToken(desafio));
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal(0, await context.RefreshToken.CountAsync());
        var fila = await context.DesafioMfa.SingleAsync();
        Assert.Equal(armado.Usuario.TokenVersion, fila.TokenVersion);
        Assert.Equal(ModoSesionMfa.Renovable, fila.ModoSesion);
        Assert.NotNull(fila.Generacion);
        Assert.Equal(ModoSesionMfa.PropositoLogin, fila.Proposito);
        Assert.Equal(64, fila.TokenHash.Length);
        Assert.DoesNotContain(fila.TokenHash, desafio, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordIncorrecta_SigueSiendoGenerica()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var result = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = "ClaveMala1!" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Credenciales incorrectas", Texto(unauthorized.Value, "mensaje"));
        Assert.Equal(0, await context.DesafioMfa.CountAsync());
    }

    [Fact]
    public async Task Tradicional_RegistraGeneracionAusente_YCompletarNoCreaFamilia()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        var fila = await context.DesafioMfa.SingleAsync();
        Assert.Equal(ModoSesionMfa.Tradicional, fila.ModoSesion);
        Assert.Null(fila.Generacion);

        var http = Http(false);
        var result = await Controlador(context, http, armado).VerificarMfa(new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)login).Value, "desafio"),
            Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante)
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AuthResponseDto>(ok.Value);
        Assert.Equal("Autenticación exitosa", body.Mensaje);
        Assert.False(body.RequiereCambioPassword);
        Assert.DoesNotContain(Leer(body.Token).Claims, c => c.Type == "sid");
        Assert.InRange((Leer(body.Token).ValidTo - DateTime.UtcNow).TotalMinutes, 55, 65);
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Equal(0, await context.RefreshToken.CountAsync());
        Assert.Equal(0, (await context.Usuario.SingleAsync()).MfaIntentosFallidos);
    }

    [Fact]
    public async Task Renovable_EmiteSidCookieYAccessCorto_DespuesDelCommit()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var loginHttp = Http(true);
        var login = await Controlador(context, loginHttp, armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        Assert.False(loginHttp.Response.Headers.ContainsKey("Set-Cookie"));
        var http = loginHttp;
        http.Response.Body = new MemoryStream();
        var result = await Controlador(context, http, armado).VerificarMfa(new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)login).Value, "desafio"),
            Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante)
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AuthResponseDto>(ok.Value);
        Assert.InRange((Leer(body.Token).ValidTo - DateTime.UtcNow).TotalMinutes, 14, 16);
        var sid = Leer(body.Token).Claims.Single(c => c.Type == "sid").Value;
        Assert.Equal(1, await context.RefreshToken.CountAsync(r => r.FamiliaId == Guid.Parse(sid) && r.RevocadaEn == null));
        Assert.True(http.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.Contains("cf_g_", http.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CambioDePasswordPendiente_QuedaRestringidoDespuesDeMfa()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, cambio: true);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        var result = await Controlador(context, Http(false), armado).VerificarMfa(new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)login).Value, "desafio"),
            Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante)
        });
        var body = Assert.IsType<AuthResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(body.RequiereCambioPassword);

        var bloqueado = await MiddlewareAsync(context, armado.Usuario, "/api/empleados");
        var permitido = await MiddlewareAsync(context, armado.Usuario, "/api/Auth/cambiar-password", post: true);
        Assert.Equal(StatusCodes.Status403Forbidden, bloqueado);
        Assert.Equal(StatusCodes.Status200OK, permitido);
    }

    [Theory]
    [InlineData("inexistente")]
    [InlineData("vencido")]
    [InlineData("consumido")]
    public async Task DesafioInutilizable_NoEntregaSesion(string caso)
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        var desafio = Texto(((UnauthorizedObjectResult)login).Value, "desafio");
        if (caso == "vencido")
        {
            var fila = await context.DesafioMfa.SingleAsync();
            fila.ExpiraEn = Instante.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        if (caso == "consumido")
        {
            var fila = await context.DesafioMfa.SingleAsync();
            fila.ConsumidoEn = Instante;
            await context.SaveChangesAsync();
        }

        var result = await Controlador(context, Http(false), armado).VerificarMfa(new VerificarMfaDto
        {
            Desafio = caso == "inexistente" ? TokenOpaco.Crear() : desafio,
            Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante)
        });

        Assert.Equal("mfa_invalido", Texto(Assert.IsType<UnauthorizedObjectResult>(result).Value, "codigo"));
        Assert.Equal(0, await context.RefreshToken.CountAsync());
    }

    [Fact]
    public async Task ContextoDistinto_SeRechazaSinGastarElCodigo()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        var result = await Controlador(context, Http(true), armado).VerificarMfa(new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)login).Value, "desafio"),
            Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante)
        });

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
        Assert.Equal(0, (await context.DesafioMfa.SingleAsync()).Intentos);
        Assert.Null((await context.Usuario.SingleAsync()).UltimoPasoTotp);
    }

    [Fact]
    public async Task CodigoIncorrectoYRepetido_NoReutilizanElPaso()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var primero = Texto(((UnauthorizedObjectResult)await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave })).Value, "desafio");
        var malo = await Controlador(context, Http(false), armado)
            .VerificarMfa(new VerificarMfaDto { Desafio = primero, Codigo = "000000" });
        Assert.Equal("mfa_invalido", Texto(Assert.IsType<UnauthorizedObjectResult>(malo).Value, "codigo"));
        Assert.Equal(1, (await context.Usuario.SingleAsync()).MfaIntentosFallidos);

        var bueno = await Controlador(context, Http(false), armado)
            .VerificarMfa(new VerificarMfaDto { Desafio = primero, Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante) });
        Assert.IsType<OkObjectResult>(bueno);
        var segundo = Texto(((UnauthorizedObjectResult)await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave })).Value, "desafio");
        var repetido = await Controlador(context, Http(false), armado)
            .VerificarMfa(new VerificarMfaDto { Desafio = segundo, Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante) });
        Assert.Equal("mfa_invalido", Texto(Assert.IsType<UnauthorizedObjectResult>(repetido).Value, "codigo"));
        Assert.Equal(0, await context.RefreshToken.CountAsync());
        Assert.NotNull((await context.Usuario.SingleAsync()).UltimoPasoTotp);
        Assert.Equal(1, (await context.DesafioMfa.OrderByDescending(d => d.Id).FirstAsync()).Intentos);
    }

    [Fact]
    public async Task LimitesDeDesafioUsuarioEIp_YElBloqueoSobreviveAOtroDesafio()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, emision: 2, verificacion: 4, intentosDesafio: 2, intentosUsuario: 3);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        var segundo = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        var tercero = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        Assert.Equal(StatusCodes.Status429TooManyRequests, ((ObjectResult)tercero).StatusCode);
        Assert.Equal(2, await context.DesafioMfa.CountAsync());
        Assert.Equal(0, (await context.Usuario.SingleAsync()).MfaIntentosFallidos);

        var desafio = Texto(((UnauthorizedObjectResult)login).Value, "desafio");
        Assert.IsType<UnauthorizedObjectResult>(await Controlador(context, Http(false), armado)
            .VerificarMfa(new VerificarMfaDto { Desafio = desafio, Codigo = "111111" }));
        Assert.IsType<UnauthorizedObjectResult>(await Controlador(context, Http(false), armado)
            .VerificarMfa(new VerificarMfaDto { Desafio = desafio, Codigo = "222222" }));
        var usuario = await context.Usuario.SingleAsync();
        Assert.Equal(2, usuario.MfaIntentosFallidos);
        Assert.NotNull((await context.DesafioMfa.SingleAsync(d => d.TokenHash == TokenOpaco.Hash(desafio))).ConsumidoEn);

        var otro = Texto(((UnauthorizedObjectResult)segundo).Value, "desafio");
        var causa = await Controlador(context, Http(false), armado)
            .VerificarMfa(new VerificarMfaDto { Desafio = otro, Codigo = "333333" });
        Assert.Equal("mfa_invalido", Texto(Assert.IsType<UnauthorizedObjectResult>(causa).Value, "codigo"));
        var bloqueo = await Controlador(context, Http(false), armado)
            .VerificarMfa(new VerificarMfaDto { Desafio = otro, Codigo = "333333" });
        Assert.Equal("mfa_bloqueado", Texto(Assert.IsType<UnauthorizedObjectResult>(bloqueo).Value, "codigo"));
        usuario = await context.Usuario.SingleAsync();
        Assert.Equal(3, usuario.MfaIntentosFallidos);
        Assert.NotNull(usuario.MfaBloqueadoHasta);
        var durante = usuario.MfaBloqueadoHasta;
        var ip = await Controlador(context, Http(false), armado)
            .VerificarMfa(new VerificarMfaDto { Desafio = TokenOpaco.Crear(), Codigo = "444444" });
        Assert.Equal(StatusCodes.Status429TooManyRequests, ((ObjectResult)ip).StatusCode);
        var extra = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        Assert.Equal(StatusCodes.Status429TooManyRequests, ((ObjectResult)extra).StatusCode);
        armado.Limite.Actual = new LimiteMfaPorIp();
        var nuevo = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        Assert.IsType<UnauthorizedObjectResult>(nuevo);
        usuario = await context.Usuario.SingleAsync();
        Assert.Equal(3, usuario.MfaIntentosFallidos);
        Assert.Equal(durante, usuario.MfaBloqueadoHasta);
    }

    [Fact]
    public async Task DosVerificacionesDelMismoDesafio_DejanUnSoloExito()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        var pedido = new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)login).Value, "desafio"),
            Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante)
        };
        var servicio = armado.Servicio;
        var tareas = new[]
        {
            servicio.VerificarAsync(pedido.Desafio, pedido.Codigo, ModoSesionMfa.Tradicional, null, "127.0.0.1", null, null),
            servicio.VerificarAsync(pedido.Desafio, pedido.Codigo, ModoSesionMfa.Tradicional, null, "127.0.0.1", null, null)
        };
        var resultados = await Task.WhenAll(tareas);
        Assert.Equal(1, resultados.Count(r => r.Estado == EstadoVerificacionMfa.Emitido));
        Assert.NotNull((await context.Usuario.SingleAsync()).UltimoPasoTotp);
    }

    [Fact]
    public async Task RollbackAntesDelCommit_NoConsumeElDesafio()
    {
        await using var conexion = new SqliteConnection("Data Source=mfa-rollback;Mode=Memory;Cache=Shared");
        await conexion.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conexion).Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var armado = await ArmarAsync(context);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        armado.Servicio.AntesDeConfirmar = _ => throw new InvalidOperationException("corte");
        var result = await armado.Servicio.VerificarAsync(
            Texto(((UnauthorizedObjectResult)login).Value, "desafio"),
            ValidadorTotp.CodigoEn(armado.Secreto, Instante),
            ModoSesionMfa.Tradicional,
            null,
            "127.0.0.1",
            null,
            null);
        Assert.Equal(EstadoVerificacionMfa.Error, result.Estado);
        await using var lectura = new AppDbContext(options);
        var fila = await lectura.DesafioMfa.SingleAsync();
        Assert.Null(fila.ConsumidoEn);
        Assert.Null(await lectura.Usuario.Select(u => u.UltimoPasoTotp).SingleAsync());
        Assert.Equal(0, await lectura.RefreshToken.CountAsync());
    }

    [Fact]
    public async Task RespuestaPerdida_ElDesafioQuedaConsumido()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        armado.Servicio.DespuesDeConfirmar = _ => throw new InvalidOperationException("corte");
        var perdido = await armado.Servicio.VerificarAsync(
            Texto(((UnauthorizedObjectResult)login).Value, "desafio"),
            ValidadorTotp.CodigoEn(armado.Secreto, Instante),
            ModoSesionMfa.Tradicional,
            null,
            "127.0.0.1",
            null,
            null);
        Assert.Equal(EstadoVerificacionMfa.Error, perdido.Estado);
        Assert.NotNull((await context.DesafioMfa.SingleAsync()).ConsumidoEn);
        armado.Servicio.DespuesDeConfirmar = null;
        armado.Servicio.Reloj = () => Instante.AddSeconds(30);
        var nuevo = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        var result = await Controlador(context, Http(false), armado).VerificarMfa(new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)nuevo).Value, "desafio"),
            Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante.AddSeconds(30))
        });
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task TokenVersionOBaja_InvalidanElDesafioSinEmitirSesion()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        armado.Usuario.TokenVersion++;
        await context.SaveChangesAsync();
        var version = await Controlador(context, Http(false), armado).VerificarMfa(new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)login).Value, "desafio"),
            Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante)
        });
        Assert.Equal("mfa_invalido", Texto(Assert.IsType<UnauthorizedObjectResult>(version).Value, "codigo"));
        Assert.Equal(0, await context.RefreshToken.CountAsync());

        var otro = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        armado.Usuario.Activo = false;
        await context.SaveChangesAsync();
        var baja = await Controlador(context, Http(false), armado).VerificarMfa(new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)otro).Value, "desafio"),
            Codigo = ValidadorTotp.CodigoEn(armado.Secreto, Instante.AddSeconds(30))
        });
        Assert.Equal("mfa_invalido", Texto(Assert.IsType<UnauthorizedObjectResult>(baja).Value, "codigo"));
    }

    [Fact]
    public async Task ClaveAusenteOCifradoAlterado_NoOmiteMfaNiCuentaComoCodigo()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, protectorSinClave: true);
        var login = await Controlador(context, Http(false), armado)
            .Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        Assert.Equal("mfa_requerido", Texto(Assert.IsType<UnauthorizedObjectResult>(login).Value, "codigo"));
        var ausente = await Controlador(context, Http(false), armado).VerificarMfa(new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)login).Value, "desafio"),
            Codigo = "123456"
        });
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((ObjectResult)ausente).StatusCode);
        Assert.Equal(0, (await context.Usuario.SingleAsync()).MfaIntentosFallidos);

        await using var otro = Memoria();
        var valido = await ArmarAsync(otro);
        var ingreso = await Controlador(otro, Http(false), valido)
            .Login(new LoginRequestDto { Email = valido.Usuario.Correo, Password = Clave });
        var original = valido.Usuario.TotpSecretoCifrado!;
        var partes = original.Split('.');
        Assert.Equal(5, partes.Length);
        var cifrado = partes[3].ToCharArray();
        cifrado[0] = cifrado[0] == 'A' ? 'B' : 'A';
        partes[3] = new string(cifrado);
        valido.Usuario.TotpSecretoCifrado = string.Join('.', partes);
        Assert.NotEqual(original, valido.Usuario.TotpSecretoCifrado);
        await otro.SaveChangesAsync();
        var alterado = await Controlador(otro, Http(false), valido).VerificarMfa(new VerificarMfaDto
        {
            Desafio = Texto(((UnauthorizedObjectResult)ingreso).Value, "desafio"),
            Codigo = ValidadorTotp.CodigoEn(valido.Secreto, Instante)
        });
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((ObjectResult)alterado).StatusCode);
        Assert.Equal(0, (await otro.Usuario.SingleAsync()).MfaIntentosFallidos);
        Assert.DoesNotContain(valido.Auditoria.Detalles, d => d.Contains("123456", StringComparison.Ordinal) || d.Contains(Convert.ToBase64String(valido.Secreto), StringComparison.Ordinal));
    }

    [Fact]
    public async Task HttpLocal_CuentaFicticiaCompletaMfaSinTocarSuperAdmin()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CF_MFA_HTTP"), "1", StringComparison.Ordinal))
            return;

        Assert.NotNull(ArchivoLocal());
        var configuracion = ConfiguracionLocal();
        var protector = new ProtectorSecretosMfa(configuracion);
        var conexion = configuracion.GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(conexion));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(conexion!, ServerVersion.Parse("8.0.0-mysql"))
            .Options;
        var secreto = RandomNumberGenerator.GetBytes(20);
        var correo = $"etapa7b.http.{Guid.NewGuid():N}@local.test";
        int usuarioId = 0;
        try
        {
            await using var alta = new AppDbContext(options);
            var usuario = new Usuario
            {
                EmpresaId = 1,
                NombreUsuario = "mfa.http",
                Correo = correo,
                Rol = "RRHH",
                Activo = true,
                TotpHabilitado = true
            };
            usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Clave);
            alta.Usuario.Add(usuario);
            await alta.SaveChangesAsync();
            usuario.TotpSecretoCifrado = protector.Proteger(usuario.Id, ProtectorSecretosMfa.PropositoTotpActivo, secreto);
            await alta.SaveChangesAsync();
            usuarioId = usuario.Id;

            using var cliente = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5088") };
            using var login = await cliente.PostAsJsonAsync("/api/Auth/login", new { email = correo, password = Clave });
            Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
            using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            Assert.Equal("mfa_requerido", loginJson.RootElement.GetProperty("codigo").GetString());
            Assert.False(loginJson.RootElement.TryGetProperty("mensaje", out _));
            Assert.False(loginJson.RootElement.TryGetProperty("token", out _));
            Assert.False(login.Headers.Contains("Set-Cookie"));
            var desafio = loginJson.RootElement.GetProperty("desafio").GetString();
            Assert.False(string.IsNullOrWhiteSpace(desafio));

            using var verificar = await cliente.PostAsJsonAsync("/api/Auth/mfa/verificar", new
            {
                desafio,
                codigo = ValidadorTotp.CodigoEn(secreto, DateTime.UtcNow)
            });
            Assert.Equal(HttpStatusCode.OK, verificar.StatusCode);
            using var ok = JsonDocument.Parse(await verificar.Content.ReadAsStringAsync());
            Assert.False(string.IsNullOrWhiteSpace(ok.RootElement.GetProperty("token").GetString()));
            Assert.Equal("Autenticación exitosa", ok.RootElement.GetProperty("mensaje").GetString());
            Assert.False(ok.RootElement.GetProperty("requiereCambioPassword").GetBoolean());
            Assert.False(verificar.Headers.Contains("Set-Cookie"));
        }
        finally
        {
            if (usuarioId > 0)
            {
                await using var limpieza = new AppDbContext(options);
                await limpieza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DesafioMfa WHERE UsuarioId = {usuarioId}");
                await limpieza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RefreshToken WHERE UsuarioId = {usuarioId}");
                await limpieza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Usuario WHERE Id = {usuarioId}");
                var super = await limpieza.Usuario.AsNoTracking().SingleAsync(u => u.Id == 6);
                Assert.False(super.TotpHabilitado);
                Assert.False(super.MfaObligatorio);
                Assert.Null(super.TotpSecretoCifrado);
            }
        }
    }

    [Fact]
    public async Task DosSesionesDeBase_NoConsumenElMismoPaso()
    {
        var conexion = ConexionLocal();
        if (conexion == null)
            return;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(conexion, ServerVersion.Parse("8.0.0-mysql"))
            .Options;
        var secreto = RandomNumberGenerator.GetBytes(20);
        var claves = new Dictionary<string, byte[]> { ["k1"] = RandomNumberGenerator.GetBytes(32) };
        var protector = new ProtectorSecretosMfa("k1", claves);
        int usuarioId = 0;
        try
        {
            await using var alta = new AppDbContext(options);
            var usuario = new Usuario
            {
                EmpresaId = 1,
                NombreUsuario = "mfa.concurrencia",
                Correo = $"etapa7b.{Guid.NewGuid():N}@local.test",
                Rol = "RRHH",
                Activo = true,
                TotpHabilitado = true
            };
            usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Clave);
            alta.Usuario.Add(usuario);
            await alta.SaveChangesAsync();
            usuario.TotpSecretoCifrado = protector.Proteger(usuario.Id, ProtectorSecretosMfa.PropositoTotpActivo, secreto);
            var ahora = DateTime.UtcNow;
            var desafio = TokenOpaco.Crear();
            alta.DesafioMfa.Add(new DesafioMfa
            {
                UsuarioId = usuario.Id,
                TokenHash = TokenOpaco.Hash(desafio),
                ExpiraEn = ahora.AddMinutes(5),
                Proposito = ModoSesionMfa.PropositoLogin,
                TokenVersion = usuario.TokenVersion,
                ModoSesion = ModoSesionMfa.Tradicional,
                Intentos = 0
            });
            await alta.SaveChangesAsync();
            usuarioId = usuario.Id;
            var codigo = ValidadorTotp.CodigoEn(secreto, ahora);

            await using var primera = new AppDbContext(options);
            await using var segunda = new AppDbContext(options);
            var una = ServicioMySql(primera, protector);
            var otra = ServicioMySql(segunda, protector);
            una.Reloj = () => ahora;
            otra.Reloj = () => ahora;
            var resultados = await Task.WhenAll(
                una.VerificarAsync(desafio, codigo, ModoSesionMfa.Tradicional, null, "10.0.0.1", null, null),
                otra.VerificarAsync(desafio, codigo, ModoSesionMfa.Tradicional, null, "10.0.0.2", null, null));

            Assert.Equal(1, resultados.Count(r => r.Estado == EstadoVerificacionMfa.Emitido));
            await using var lectura = new AppDbContext(options);
            Assert.NotNull(await lectura.Usuario.Where(u => u.Id == usuarioId).Select(u => u.UltimoPasoTotp).SingleAsync());
            Assert.NotNull(await lectura.DesafioMfa.Where(d => d.UsuarioId == usuarioId).Select(d => d.ConsumidoEn).SingleAsync());
        }
        finally
        {
            if (usuarioId > 0)
            {
                await using var limpieza = new AppDbContext(options);
                await limpieza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DesafioMfa WHERE UsuarioId = {usuarioId}");
                await limpieza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RefreshToken WHERE UsuarioId = {usuarioId}");
                await limpieza.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Usuario WHERE Id = {usuarioId}");
            }
        }
    }

    private static VerificacionMfaService ServicioMySql(AppDbContext context, IProtectorSecretosMfa protector)
    {
        var config = Configuracion();
        return new VerificacionMfaService(
            context,
            config,
            protector,
            new JwtTokenService(config),
            new SesionWebService(context, config, new JwtTokenService(config)),
            new LimiteMfaPorIp());
    }

    private static string? ConexionLocal()
    {
        var archivo = ArchivoLocal();
        if (archivo == null)
            return null;

        using var documento = JsonDocument.Parse(File.ReadAllText(archivo));
        if (documento.RootElement.TryGetProperty("ConnectionStrings", out var cadenas)
            && cadenas.TryGetProperty("DefaultConnection", out var valor))
            return valor.GetString();

        return null;
    }

    private static IConfiguration ConfiguracionLocal()
    {
        using var documento = JsonDocument.Parse(File.ReadAllText(ArchivoLocal()!));
        var valores = new Dictionary<string, string?>();
        void Copiar(JsonElement nodo, string prefijo)
        {
            foreach (var propiedad in nodo.EnumerateObject())
            {
                var clave = string.IsNullOrEmpty(prefijo) ? propiedad.Name : prefijo + ":" + propiedad.Name;
                if (propiedad.Value.ValueKind == JsonValueKind.Object)
                    Copiar(propiedad.Value, clave);
                else if (propiedad.Value.ValueKind != JsonValueKind.Array)
                    valores[clave] = propiedad.Value.ToString();
            }
        }

        Copiar(documento.RootElement, "");
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static string? ArchivoLocal()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var archivo = Path.Combine(dir.FullName, "ControlFichajes.API", "appsettings.Development.local.json");
            if (File.Exists(archivo))
                return archivo;

            dir = dir.Parent;
        }

        return null;
    }

    private static async Task<int> MiddlewareAsync(AppDbContext context, Usuario usuario, string path, bool post = false)
    {
        var http = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new System.Security.Claims.Claim("token_use", "web"),
                new System.Security.Claims.Claim("token_version", usuario.TokenVersion.ToString())
            ], "Test"))
        };
        http.Request.Method = post ? HttpMethods.Post : HttpMethods.Get;
        http.Request.Path = path;
        http.Response.Body = new MemoryStream();
        var middleware = new WebSessionSecurityMiddleware(_ =>
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(http, context, Configuracion());
        return http.Response.StatusCode;
    }

    private static AppDbContext Memoria()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new AppDbContext(options);
        context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno", CUIT = "30-00000000-0" });
        context.SaveChanges();
        return context;
    }

    private static async Task<Usuario> UsuarioAsync(AppDbContext context, string rol)
    {
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "cuenta.local",
            Correo = $"{Guid.NewGuid():N}@local.test",
            Rol = rol,
            Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Clave);
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }

    private static async Task<Armado> ArmarAsync(
        AppDbContext context,
        bool cambio = false,
        int emision = 10,
        int verificacion = 10,
        int intentosDesafio = 5,
        int intentosUsuario = 5,
        bool protectorSinClave = false)
    {
        if (!await context.Empresa.AnyAsync())
        {
            context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno", CUIT = "30-00000000-0" });
            await context.SaveChangesAsync();
        }

        var secreto = RandomNumberGenerator.GetBytes(20);
        var claves = new Dictionary<string, byte[]> { ["k1"] = RandomNumberGenerator.GetBytes(32) };
        IProtectorSecretosMfa protector = protectorSinClave
            ? new ProtectorSecretosMfa(new ConfigurationBuilder().Build())
            : new ProtectorSecretosMfa("k1", claves);
        var usuario = await UsuarioAsync(context, "RRHH");
        usuario.TotpHabilitado = true;
        usuario.RequiereCambioPassword = cambio;
        if (cambio)
            usuario.PasswordTemporalVenceEn = DateTime.UtcNow.AddHours(1);
        usuario.TotpSecretoCifrado = protectorSinClave
            ? "v1.k1.AAAAAAAAAAAAAAAA.BBBBBBBBBBBBBBBB.CCCCCCCCCCCCCCCCCCCCCC"
            : protector.Proteger(usuario.Id, ProtectorSecretosMfa.PropositoTotpActivo, secreto);
        await context.SaveChangesAsync();
        var config = Configuracion(emision, verificacion, intentosDesafio, intentosUsuario);
        var limite = new PuenteLimite();
        var auditoria = new CapturaAuditoria();
        var auth = new AuthService(
            context,
            new PasswordHasher<Usuario>(),
            new JwtTokenService(config),
            auditoria,
            configuration: config,
            protector: protector,
            limite: limite);
        var servicio = new VerificacionMfaService(
            context,
            config,
            protector,
            new JwtTokenService(config),
            new SesionWebService(context, config, new JwtTokenService(config), auditoria),
            limite,
            auditoria)
        {
            Reloj = () => Instante
        };
        return new Armado(usuario, secreto, auth, servicio, limite, auditoria);
    }

    private static AuthController Controlador(AppDbContext context, DefaultHttpContext http, Armado? armado = null)
    {
        var config = Configuracion();
        var auth = armado?.Auth ?? new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(config));
        var controller = new AuthController(
            auth,
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(config)),
            config,
            sesiones: new SesionWebService(context, config, new JwtTokenService(config)),
            entorno: new EntornoLocal(),
            mfa: armado?.Servicio)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return controller;
    }

    private static DefaultHttpContext Http(bool renovable)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "http";
        http.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        http.Response.Body = new MemoryStream();
        if (!renovable)
            return http;

        http.Request.Headers[ProteccionSolicitudSesion.Encabezado] = ProteccionSolicitudSesion.Valor;
        http.Request.Headers.Origin = Origen;
        http.Request.Headers[GeneracionSesion.Encabezado] = Guid.NewGuid().ToString();
        return http;
    }

    private static IConfiguration Configuracion(int emision = 10, int verificacion = 10, int intentosDesafio = 5, int intentosUsuario = 5)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
            ["Jwt:Issuer"] = "ControlFichajes.Tests",
            ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
            ["Jwt:ExpireMinutes"] = "60",
            ["Jwt:AccessExpireMinutes"] = "15",
            ["Jwt:RefreshExpireDays"] = "14",
            ["Jwt:RefreshInactividadHoras"] = "12",
            ["Cors:AllowedOrigins:0"] = Origen,
            ["Auth:MfaEmisionLimite"] = emision.ToString(),
            ["Auth:MfaVerificacionLimite"] = verificacion.ToString(),
            ["Auth:MfaIntentosDesafio"] = intentosDesafio.ToString(),
            ["Auth:MfaIntentosUsuario"] = intentosUsuario.ToString()
        }).Build();
    }

    private static string Texto(object? valor, string nombre)
    {
        return valor!.GetType().GetProperty(nombre)!.GetValue(valor)!.ToString()!;
    }

    private static JwtSecurityToken Leer(string token)
    {
        return new JwtSecurityTokenHandler().ReadJwtToken(token);
    }

    private sealed class Armado
    {
        public Armado(Usuario usuario, byte[] secreto, AuthService auth, VerificacionMfaService servicio, PuenteLimite limite, CapturaAuditoria auditoria)
        {
            Usuario = usuario;
            Secreto = secreto;
            Auth = auth;
            Servicio = servicio;
            Limite = limite;
            Auditoria = auditoria;
        }

        public Usuario Usuario { get; }
        public byte[] Secreto { get; }
        public AuthService Auth { get; }
        public VerificacionMfaService Servicio { get; }
        public PuenteLimite Limite { get; }
        public CapturaAuditoria Auditoria { get; }
    }

    private sealed class PuenteLimite : ILimiteMfaPorIp
    {
        public ILimiteMfaPorIp Actual { get; set; } = new LimiteMfaPorIp();

        public bool Permitir(string categoria, string? ip, int limite, TimeSpan ventana)
        {
            return Actual.Permitir(categoria, ip, limite, ventana);
        }
    }

    private sealed class CapturaAuditoria : IAuditoriaSeguridad
    {
        public List<string> Detalles { get; } = [];

        public Task RegistrarAsync(string evento, string resultado, int? usuarioId, string? correo, string? detalle, CancellationToken cancellationToken = default)
        {
            Detalles.Add(detalle ?? string.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class EntornoLocal : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
