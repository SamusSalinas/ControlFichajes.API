using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ControlFichajes.API.Tests;

public class RecuperacionPasswordTests
{
    private const string ClaveAnterior = "ClaveVieja1!";
    private const string ClaveNueva = "ClaveNueva1!";
    private static readonly InMemoryDatabaseRoot Raiz = new();
    private static readonly ConditionalWeakTable<AppDbContext, DbContextOptions<AppDbContext>> Opciones = new();

    [Fact]
    public async Task RecuperacionCompleta_CambiaLaClaveRechazaElJwtYConservaEmpresaYRol()
    {
        await using var context = CrearContexto();
        var usuario = await AgregarUsuarioAsync(context, "persona.local@empresa.test");
        var version = usuario.TokenVersion;
        var auth = CrearAuth(context);
        var anterior = await auth.LoginAsync(new LoginRequestDto { Email = usuario.Correo, Password = ClaveAnterior });
        Assert.NotNull(anterior);
        Assert.False(anterior!.RequiereCambioPassword);
        Assert.False(string.IsNullOrWhiteSpace(anterior.Token));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(anterior.Token);
        Assert.Equal(version.ToString(), jwt.Claims.Single(c => c.Type == "token_version").Value);
        Assert.True(await MiddlewareAsync(context, usuario.Id, version));

        var buzon = new Buzon();
        var controlador = CrearControlador(context, buzon);
        var solicitud = await controlador.SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = "  Persona.Local@Empresa.Test  " });
        Assert.Equal(AuthController.MensajeRecuperacionSolicitada, Mensaje(Assert.IsType<OkObjectResult>(solicitud).Value));
        var mensaje = Assert.Single(buzon.Mensajes);
        Assert.Contains("/restablecer-contrasena?token=", mensaje.Texto, StringComparison.Ordinal);
        Assert.Contains(WebUtility.HtmlEncode(TokenDelEnlace(mensaje.Texto)), mensaje.Html, StringComparison.Ordinal);
        var token = TokenDelEnlace(mensaje.Texto);
        Assert.Equal(TokenOpaco.Bytes, Decodificar(token).Length);
        var fila = await context.RecuperacionPassword.SingleAsync();
        Assert.Equal(TokenOpaco.Hash(token), fila.TokenHash);
        Assert.DoesNotContain(token, fila.TokenHash, StringComparison.Ordinal);
        Assert.Null(fila.UsadaEn);
        Assert.InRange((fila.ExpiraEn - DateTime.UtcNow).TotalMinutes, 29, 31);

        var restablecio = await controlador.RestablecerPassword(Solicitud(token, ClaveNueva));
        Assert.Equal("Contraseña restablecida. Ya podés iniciar sesión.", Mensaje(Assert.IsType<OkObjectResult>(restablecio).Value));
        var json = System.Text.Json.JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(restablecio).Value);
        Assert.DoesNotContain(token, json, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaveNueva, json, StringComparison.Ordinal);

        await using var lectura = CrearContexto(Nombre(context));
        var actual = await lectura.Usuario.SingleAsync(u => u.Id == usuario.Id);
        Assert.True(actual.Activo);
        Assert.NotNull(actual.CorreoVerificadoEn);
        Assert.Equal(1, actual.EmpresaId);
        Assert.Equal(AppRoles.Rrhh, actual.Rol);
        Assert.False(actual.RequiereCambioPassword);
        Assert.Equal(version + 1, actual.TokenVersion);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<Usuario>().VerifyHashedPassword(actual, actual.PasswordHash, ClaveNueva));
        Assert.NotNull(await lectura.RecuperacionPassword.SingleAsync(r => r.UsadaEn != null));
        Assert.Null(await CrearAuth(lectura).LoginAsync(new LoginRequestDto { Email = actual.Correo, Password = ClaveAnterior }));
        var nuevo = await CrearAuth(lectura).LoginAsync(new LoginRequestDto { Email = actual.Correo, Password = ClaveNueva });
        Assert.NotNull(nuevo);
        Assert.False(nuevo!.RequiereCambioPassword);
        Assert.False(string.IsNullOrWhiteSpace(nuevo.Token));
        Assert.False(await MiddlewareAsync(lectura, actual.Id, version));
        Assert.True(await MiddlewareAsync(lectura, actual.Id, version + 1));

        var sucursal = new Sucursal { Nombre = "Central", EmpresaId = 1, SerialLector = "lector-local" };
        lectura.Sucursal.Add(sucursal);
        await lectura.SaveChangesAsync();
        var agente = new AgenteInstalacion { ClientId = "agente-local", Nombre = "Agente", SucursalId = sucursal.Id, Activo = true };
        agente.SecretHash = new PasswordHasher<AgenteInstalacion>().HashPassword(agente, "SecretoAgente1234");
        lectura.AgenteInstalacion.Add(agente);
        await lectura.SaveChangesAsync();
        var tokenAgente = new JwtTokenService(CrearJwt()).CreateAgentToken(agente, 1);
        var claimsAgente = new JwtSecurityTokenHandler().ReadJwtToken(tokenAgente).Claims;
        Assert.DoesNotContain(claimsAgente, c => c.Type == "token_version");
        Assert.Contains(claimsAgente, c => c.Type == "token_use" && c.Value == "agent");
        Assert.True(await MiddlewareDeAgenteAsync(lectura));

        var auditoria = string.Join('\n', lectura.AuditoriaSeguridad.Select(a => a.Evento + a.Detalle + a.Correo));
        Assert.Contains("recuperacion_solicitada", auditoria, StringComparison.Ordinal);
        Assert.Contains("recuperacion_envio_aceptado", auditoria, StringComparison.Ordinal);
        Assert.Contains("recuperacion_restablecida", auditoria, StringComparison.Ordinal);
        Assert.DoesNotContain(token, auditoria, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaveNueva, auditoria, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaveAnterior, auditoria, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RespuestaPublica_EsIgualSiNoSePuedeEnviar()
    {
        await using var context = CrearContexto();
        await AgregarUsuarioAsync(context, "activa.local@empresa.test");
        await AgregarUsuarioAsync(context, "inactiva.local@empresa.test", activo: false);
        await AgregarUsuarioAsync(context, "noverificada.local@empresa.test", verificada: false);
        var buzon = new Buzon();
        var controlador = CrearControlador(context, buzon);

        var respuestas = new[]
        {
            await controlador.SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = "activa.local@empresa.test" }),
            await controlador.SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = "nadie.local@empresa.test" }),
            await controlador.SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = "inactiva.local@empresa.test" }),
            await controlador.SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = "noverificada.local@empresa.test" }),
            await controlador.SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = "smtp.local@empresa.test" })
        };

        Assert.All(respuestas, respuesta =>
        {
            Assert.Equal(AuthController.MensajeRecuperacionSolicitada, Mensaje(Assert.IsType<OkObjectResult>(respuesta).Value));
        });
        Assert.Single(buzon.Mensajes);
        Assert.Equal(1, await context.RecuperacionPassword.CountAsync());
        Assert.False((await context.Usuario.SingleAsync(u => u.Correo == "inactiva.local@empresa.test")).Activo);
        Assert.Null((await context.Usuario.SingleAsync(u => u.Correo == "noverificada.local@empresa.test")).CorreoVerificadoEn);

        var registro = new RegistroDeLog();
        var smtp = CrearControlador(context, new SmtpQueFalla(), registro);
        var falla = await AgregarUsuarioAsync(context, "falla.local@empresa.test");
        var fallo = await smtp.SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = falla.Correo });
        Assert.Equal(AuthController.MensajeRecuperacionSolicitada, Mensaje(Assert.IsType<OkObjectResult>(fallo).Value));
        var guardada = await context.RecuperacionPassword.SingleAsync(r => r.UsuarioId == falla.Id);
        Assert.Null(guardada.UsadaEn);
        Assert.Contains(nameof(InvalidOperationException), string.Join('\n', registro.Mensajes), StringComparison.Ordinal);
        Assert.DoesNotContain("token-secreto", string.Join('\n', registro.Mensajes), StringComparison.Ordinal);
        Assert.Contains("recuperacion_envio_fallido", string.Join('\n', context.AuditoriaSeguridad.Select(a => a.Evento)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvitadoPendiente_PermaneceInactivoYNoRecibeRecuperacion()
    {
        await using var context = CrearContexto();
        var usuario = await AgregarUsuarioAsync(context, "invitado.local@empresa.test", activo: false, verificada: false);
        usuario.InvitadoEn = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var respuesta = await CrearControlador(context, new Buzon())
            .SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = usuario.Correo });

        Assert.Equal(AuthController.MensajeRecuperacionSolicitada, Mensaje(Assert.IsType<OkObjectResult>(respuesta).Value));
        var actual = await context.Usuario.SingleAsync(u => u.Id == usuario.Id);
        Assert.False(actual.Activo);
        Assert.Null(actual.InvitacionAceptadaEn);
        Assert.Equal(0, await context.RecuperacionPassword.CountAsync());
    }

    [Fact]
    public async Task NuevaSolicitud_InvalidaLaAnteriorYElTokenUsadoNoSeRepite()
    {
        await using var context = CrearContexto();
        var usuario = await AgregarUsuarioAsync(context, "persona.local@empresa.test");
        var primero = new Buzon();
        var controlador = CrearControlador(context, primero);
        await controlador.SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = usuario.Correo });
        var tokenViejo = TokenDelEnlace(Assert.Single(primero.Mensajes).Texto);
        var segundo = new Buzon();
        await CrearControlador(context, segundo).SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = usuario.Correo });
        var tokenNuevo = TokenDelEnlace(Assert.Single(segundo.Mensajes).Texto);

        var rechazado = await controlador.RestablecerPassword(Solicitud(tokenViejo, ClaveNueva));
        Assert.Equal("La recuperación no es válida.", Mensaje(Assert.IsType<BadRequestObjectResult>(rechazado).Value));
        var ahora = DateTime.UtcNow;
        var vigente = Assert.Single(await context.RecuperacionPassword.Where(r => r.UsadaEn == null && r.ExpiraEn > ahora).ToListAsync());
        Assert.Equal(TokenOpaco.Hash(tokenNuevo), vigente.TokenHash);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<Usuario>().VerifyHashedPassword(usuario, usuario.PasswordHash, ClaveAnterior));

        var ok = await controlador.RestablecerPassword(Solicitud(tokenNuevo, ClaveNueva));
        Assert.IsType<OkObjectResult>(ok);
        var repetido = await controlador.RestablecerPassword(Solicitud(tokenNuevo, ClaveNueva));
        Assert.Equal("La recuperación no es válida.", Mensaje(Assert.IsType<BadRequestObjectResult>(repetido).Value));
        Assert.Equal(1, await context.RecuperacionPassword.CountAsync(r => r.UsadaEn != null));
    }

    [Fact]
    public async Task TokensInvalidosYDeOtroProposito_SeRechazanSinCambiarLaCuenta()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, "admin.local@empresa.test");
        var buzon = new Buzon();
        var invitaciones = CrearInvitaciones(context, buzon);
        var creada = await invitaciones.InvitarAsync(1, "invitado", "invitado.local@empresa.test", AppRoles.Rrhh, admin.Id);
        Assert.Equal(EstadoInvitacion.Enviada, creada.Estado);
        var tokenInvitacion = TokenDelEnlace(Assert.Single(buzon.Mensajes).Texto);
        var invitado = await context.Usuario.SingleAsync(u => u.Correo == "invitado.local@empresa.test");

        var persona = await AgregarUsuarioAsync(context, "persona.local@empresa.test");
        var recuperacion = new Buzon();
        await CrearControlador(context, recuperacion).SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = persona.Correo });
        var tokenRecuperacion = TokenDelEnlace(Assert.Single(recuperacion.Mensajes).Texto);
        var hash = (await context.RecuperacionPassword.SingleAsync()).TokenHash;
        var fila = await context.RecuperacionPassword.SingleAsync();
        fila.ExpiraEn = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();

        var controlador = CrearControlador(context, new Buzon());
        foreach (var token in new[] { tokenInvitacion, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", tokenRecuperacion })
        {
            var rechazo = await controlador.RestablecerPassword(Solicitud(token, ClaveNueva));
            Assert.Equal("La recuperación no es válida.", Mensaje(Assert.IsType<BadRequestObjectResult>(rechazo).Value));
        }

        Assert.False((await context.Usuario.SingleAsync(u => u.Id == invitado.Id)).Activo);
        Assert.Null((await context.InvitacionUsuario.SingleAsync()).UsadaEn);
        Assert.Null((await context.RecuperacionPassword.SingleAsync()).UsadaEn);
        Assert.Equal(hash, (await context.RecuperacionPassword.SingleAsync()).TokenHash);
        Assert.Equal(0, persona.TokenVersion);

        fila.ExpiraEn = DateTime.UtcNow.AddMinutes(30);
        persona.Activo = false;
        await context.SaveChangesAsync();
        var inactiva = await controlador.RestablecerPassword(Solicitud(tokenRecuperacion, ClaveNueva));
        Assert.Equal("La recuperación no es válida.", Mensaje(Assert.IsType<BadRequestObjectResult>(inactiva).Value));
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == persona.Id)).Activo);
        Assert.Null((await context.RecuperacionPassword.SingleAsync()).UsadaEn);

        persona.Activo = true;
        await context.SaveChangesAsync();
        var aceptacion = await CrearAceptacion(context).AceptarInvitacion(new AceptarInvitacionDto
        {
            Token = tokenRecuperacion,
            Password = ClaveNueva,
            ConfirmarPassword = ClaveNueva
        });
        Assert.Equal("La invitación no es válida.", Mensaje(Assert.IsType<BadRequestObjectResult>(aceptacion).Value));
        Assert.Null((await context.RecuperacionPassword.SingleAsync()).UsadaEn);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<Usuario>().VerifyHashedPassword(persona, (await context.Usuario.SingleAsync(u => u.Id == persona.Id)).PasswordHash, ClaveAnterior));
    }

    [Fact]
    public async Task ClaveInvalidaODistinta_NoConsumeLaRecuperacion()
    {
        await using var context = CrearContexto();
        var usuario = await AgregarUsuarioAsync(context, "persona.local@empresa.test");
        var version = usuario.TokenVersion;
        var hashClave = usuario.PasswordHash;
        var buzon = new Buzon();
        var controlador = CrearControlador(context, buzon);
        await controlador.SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = usuario.Correo });
        var token = TokenDelEnlace(Assert.Single(buzon.Mensajes).Texto);

        var distinta = await controlador.RestablecerPassword(Solicitud(token, ClaveNueva, "OtraClave1!"));
        var corta = await controlador.RestablecerPassword(Solicitud(token, "corta"));
        Assert.Equal("La contraseña no cumple las reglas o no coincide.", Mensaje(Assert.IsType<BadRequestObjectResult>(distinta).Value));
        Assert.Equal("La contraseña no cumple las reglas o no coincide.", Mensaje(Assert.IsType<BadRequestObjectResult>(corta).Value));
        Assert.Null((await context.RecuperacionPassword.SingleAsync()).UsadaEn);
        Assert.Equal(hashClave, (await context.Usuario.SingleAsync(u => u.Id == usuario.Id)).PasswordHash);
        Assert.Equal(version, (await context.Usuario.SingleAsync(u => u.Id == usuario.Id)).TokenVersion);
        Assert.IsType<OkObjectResult>(await controlador.RestablecerPassword(Solicitud(token, ClaveNueva)));
    }

    [Fact]
    public async Task DosRestablecimientosConcurrentes_ProducenUnSoloExito()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearContexto(nombre);
        var usuario = await AgregarUsuarioAsync(preparacion, "persona.local@empresa.test");
        var buzon = new Buzon();
        await CrearControlador(preparacion, buzon).SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = usuario.Correo });
        var token = TokenDelEnlace(Assert.Single(buzon.Mensajes).Texto);
        await using var primero = CrearContexto(nombre);
        await using var segundo = CrearContexto(nombre);

        var resultados = await Task.WhenAll(
            CrearControlador(primero, new Buzon()).RestablecerPassword(Solicitud(token, ClaveNueva)),
            CrearControlador(segundo, new Buzon()).RestablecerPassword(Solicitud(token, ClaveNueva)));

        Assert.Equal(1, resultados.Count(r => r is OkObjectResult));
        Assert.Equal(1, resultados.Count(r => r is BadRequestObjectResult));
        await using var lectura = CrearContexto(nombre);
        var actual = await lectura.Usuario.SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(1, actual.TokenVersion);
        Assert.Equal(1, actual.EmpresaId);
        Assert.Equal(AppRoles.Rrhh, actual.Rol);
        Assert.Equal(1, await lectura.RecuperacionPassword.CountAsync(r => r.UsadaEn != null));
        Assert.NotNull(await CrearAuth(lectura).LoginAsync(new LoginRequestDto { Email = actual.Correo, Password = ClaveNueva }));
    }

    [Fact]
    public async Task SolicitudYRestablecimientoConcurrentes_DejanUnEstadoCoherente()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearContexto(nombre);
        var usuario = await AgregarUsuarioAsync(preparacion, "persona.local@empresa.test");
        var buzon = new Buzon();
        await CrearControlador(preparacion, buzon).SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = usuario.Correo });
        var token = TokenDelEnlace(Assert.Single(buzon.Mensajes).Texto);
        var hashOriginal = TokenOpaco.Hash(token);
        await using var restablece = CrearContexto(nombre);
        await using var solicita = CrearContexto(nombre);

        var resultados = await Task.WhenAll(
            CrearControlador(restablece, new Buzon()).RestablecerPassword(Solicitud(token, ClaveNueva)),
            CrearControlador(solicita, new Buzon()).SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = usuario.Correo }));

        await using var lectura = CrearContexto(nombre);
        var ahora = DateTime.UtcNow;
        var actual = await lectura.Usuario.SingleAsync(u => u.Id == usuario.Id);
        var vigentes = await lectura.RecuperacionPassword.CountAsync(r => r.UsadaEn == null && r.ExpiraEn > ahora);
        var usada = await lectura.RecuperacionPassword.AnyAsync(r => r.TokenHash == hashOriginal && r.UsadaEn != null);
        Assert.Equal(AuthController.MensajeRecuperacionSolicitada, Mensaje(Assert.IsType<OkObjectResult>(resultados[1]).Value));
        Assert.Equal(1, actual.EmpresaId);
        Assert.Equal(AppRoles.Rrhh, actual.Rol);
        if (resultados[0] is OkObjectResult)
        {
            Assert.True(usada);
            Assert.Equal(1, actual.TokenVersion);
            Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<Usuario>().VerifyHashedPassword(actual, actual.PasswordHash, ClaveNueva));
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(resultados[0]);
            Assert.False(usada);
            Assert.Equal(0, actual.TokenVersion);
            Assert.Equal(1, vigentes);
            Assert.NotEqual(hashOriginal, (await lectura.RecuperacionPassword.SingleAsync(r => r.UsadaEn == null && r.ExpiraEn > ahora)).TokenHash);
            Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<Usuario>().VerifyHashedPassword(actual, actual.PasswordHash, ClaveAnterior));
        }
    }

    [Fact]
    public async Task FalloAlGuardar_NoCambiaContrasenaTokenNiVersion()
    {
        var nombre = Guid.NewGuid().ToString();
        await using var preparacion = CrearContexto(nombre);
        var usuario = await AgregarUsuarioAsync(preparacion, "persona.local@empresa.test");
        var hashClave = usuario.PasswordHash;
        var buzon = new Buzon();
        await CrearControlador(preparacion, buzon).SolicitarRecuperacion(new SolicitarRecuperacionDto { Correo = usuario.Correo });
        var token = TokenDelEnlace(Assert.Single(buzon.Mensajes).Texto);
        var hashToken = (await preparacion.RecuperacionPassword.SingleAsync()).TokenHash;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(nombre, Raiz).Options;
        await using var fallido = new ContextoQueFalla(options);
        var registro = new RegistroDeLog();
        var servicio = new RecuperacionPasswordService(
            fallido,
            new PasswordHasher<Usuario>(),
            new Buzon(),
            Auditoria(preparacion, registro),
            Configuracion(),
            registro.Para<RecuperacionPasswordService>());

        var estado = await servicio.RestablecerAsync(token, ClaveNueva);

        Assert.Equal(EstadoRestablecimiento.Error, estado.Estado);
        var texto = string.Join('\n', registro.Mensajes);
        Assert.Contains(nameof(InvalidOperationException), texto, StringComparison.Ordinal);
        Assert.DoesNotContain(token, texto, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaveNueva, texto, StringComparison.Ordinal);
        await using var lectura = CrearContexto(nombre);
        var actual = await lectura.Usuario.SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(hashClave, actual.PasswordHash);
        Assert.Equal(0, actual.TokenVersion);
        var fila = await lectura.RecuperacionPassword.SingleAsync();
        Assert.Null(fila.UsadaEn);
        Assert.Equal(hashToken, fila.TokenHash);
    }

    [Fact]
    public void Cuerpos_NoAceptanDatosDeCuenta()
    {
        Assert.Equal(["Correo"], typeof(SolicitarRecuperacionDto).GetProperties().Select(p => p.Name).ToArray());
        Assert.Equal(["ConfirmarPassword", "Password", "Token"], typeof(RestablecerPasswordDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Null(CorreoNormalizado.ParaRecuperacion("sin-arroba"));
        Assert.Equal("persona.local@empresa.test", CorreoNormalizado.ParaRecuperacion("  Persona.Local@Empresa.Test "));
    }

    private static async Task<bool> MiddlewareAsync(AppDbContext context, int usuarioId, int version)
    {
        var siguiente = false;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
                new Claim("token_use", "web"),
                new Claim("token_version", version.ToString())
            ], "Test")),
            RequestServices = new ServiceCollection().AddOptions().BuildServiceProvider()
        };
        http.Request.Path = "/api/usuarios";
        var middleware = new WebSessionSecurityMiddleware(ctx =>
        {
            siguiente = true;
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(http, context);
        return siguiente && http.Response.StatusCode != StatusCodes.Status401Unauthorized;
    }

    private static async Task<bool> MiddlewareDeAgenteAsync(AppDbContext context)
    {
        var siguiente = false;
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim("token_use", "agent"),
                new Claim(ClaimTypes.Role, "AGENTE_SUCURSAL")
            ], "Test")),
            RequestServices = new ServiceCollection().AddOptions().BuildServiceProvider()
        };
        http.Request.Path = "/api/fichadas";
        http.Request.Method = HttpMethods.Post;
        var middleware = new WebSessionSecurityMiddleware(ctx =>
        {
            siguiente = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(http, context);
        return siguiente;
    }

    private static string? Mensaje(object? valor)
    {
        return valor?.GetType().GetProperty("mensaje")?.GetValue(valor) as string;
    }

    private static RestablecerPasswordDto Solicitud(string token, string password, string? confirmacion = null)
    {
        return new RestablecerPasswordDto
        {
            Token = token,
            Password = password,
            ConfirmarPassword = confirmacion ?? password
        };
    }

    private static string TokenDelEnlace(string texto)
    {
        const string marca = "token=";
        var inicio = texto.IndexOf(marca, StringComparison.Ordinal);
        Assert.True(inicio >= 0);
        var token = texto[(inicio + marca.Length)..].Trim();
        var corte = token.IndexOfAny([' ', '\r', '\n', '"', '<']);
        return corte < 0 ? token : token[..corte];
    }

    private static byte[] Decodificar(string token)
    {
        var normalizado = token.Replace('-', '+').Replace('_', '/');
        var resto = normalizado.Length % 4;
        if (resto != 0)
            normalizado = normalizado.PadRight(normalizado.Length + 4 - resto, '=');
        return Convert.FromBase64String(normalizado);
    }

    private static readonly ConditionalWeakTable<AppDbContext, NombreGuardado> Nombres = new();

    private static AppDbContext CrearContexto(string? nombre = null)
    {
        nombre ??= Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nombre, Raiz)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new AppDbContext(options);
        Opciones.Add(context, options);
        Nombres.Add(context, new NombreGuardado { Nombre = nombre });
        if (!context.Empresa.Any())
        {
            context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno S.A.", CUIT = "30-11111111-1" });
            context.SaveChanges();
        }
        return context;
    }

    private static string Nombre(AppDbContext context)
    {
        return Nombres.GetValue(context, _ => throw new InvalidOperationException()).Nombre;
    }

    private static AuthController CrearControlador(AppDbContext context, ICorreoSaliente correo, RegistroDeLog? registro = null)
    {
        registro ??= new RegistroDeLog();
        return new AuthController(
            CrearAuth(context),
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(CrearJwt())),
            Configuracion(),
            Auditoria(context, registro),
            registro.Para<AuthController>(),
            CrearInvitaciones(context, correo, registro),
            new RecuperacionPasswordService(
                context,
                new PasswordHasher<Usuario>(),
                correo,
                Auditoria(context, registro),
                Configuracion(),
                registro.Para<RecuperacionPasswordService>()));
    }

    private static InvitacionUsuarioService CrearInvitaciones(AppDbContext context, ICorreoSaliente correo, RegistroDeLog? registro = null)
    {
        registro ??= new RegistroDeLog();
        return new InvitacionUsuarioService(
            context,
            new PasswordHasher<Usuario>(),
            correo,
            Auditoria(context, registro),
            Configuracion(),
            registro.Para<InvitacionUsuarioService>());
    }

    private static AuthController CrearAceptacion(AppDbContext context)
    {
        var registro = new RegistroDeLog();
        return new AuthController(
            CrearAuth(context),
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(CrearJwt())),
            invitaciones: CrearInvitaciones(context, new Buzon(), registro));
    }

    private static AuditoriaSeguridadService Auditoria(AppDbContext context, RegistroDeLog registro)
    {
        return new AuditoriaSeguridadService(
            new AuditoriaDbContextFactory(Opciones.GetValue(context, _ => throw new InvalidOperationException())),
            registro.Para<AuditoriaSeguridadService>(),
            new HttpContextAccessor());
    }

    private static AuthService CrearAuth(AppDbContext context)
    {
        return new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(CrearJwt()));
    }

    private static IConfiguration CrearJwt()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
                ["Jwt:Issuer"] = "ControlFichajes.Tests",
                ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
                ["Jwt:ExpireMinutes"] = "60"
            })
            .Build();
    }

    private static IConfiguration Configuracion()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:PermitirAltaConPassword"] = "true",
                ["Auth:RecuperacionMinutos"] = "30",
                ["Auth:InvitacionHoras"] = "72",
                ["App:PublicBaseUrl"] = "http://127.0.0.1:5173"
            })
            .Build();
    }

    private static async Task<Usuario> AgregarUsuarioAsync(
        AppDbContext context,
        string correo,
        bool activo = true,
        bool verificada = true)
    {
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = correo.Split('@')[0],
            Correo = correo,
            Rol = AppRoles.Rrhh,
            Activo = activo,
            CorreoVerificadoEn = verificada ? DateTime.UtcNow : null
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, ClaveAnterior);
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }

    private sealed class NombreGuardado
    {
        public required string Nombre { get; init; }
    }

    private sealed class Buzon : ICorreoSaliente
    {
        public List<CorreoSaliente> Mensajes { get; } = [];

        public Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            Mensajes.Add(mensaje);
            return Task.CompletedTask;
        }
    }

    private sealed class SmtpQueFalla : ICorreoSaliente
    {
        public Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("token-secreto");
        }
    }

    private sealed class ContextoQueFalla : AppDbContext
    {
        public ContextoQueFalla(DbContextOptions<AppDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("fallo_controlado");
        }
    }

    private sealed class RegistroDeLog : ILogger
    {
        public List<string> Mensajes { get; } = [];

        public ILogger<T> Para<T>() => new RegistroTipado<T>(this);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Vacio.Instancia;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Mensajes.Add(formatter(state, exception));
        }

        private sealed class RegistroTipado<T> : ILogger<T>
        {
            private readonly RegistroDeLog _inner;
            public RegistroTipado(RegistroDeLog inner) => _inner = inner;
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
            public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _inner.Log(logLevel, eventId, state, exception, formatter);
        }

        private sealed class Vacio : IDisposable
        {
            public static readonly Vacio Instancia = new();
            public void Dispose() { }
        }
    }
}
