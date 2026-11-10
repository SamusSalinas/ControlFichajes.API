using System.Security.Claims;
using System.Security.Cryptography;
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
using Microsoft.Extensions.Configuration;

namespace ControlFichajes.API.Tests;

public class MfaVinculacionTests
{
    private const string Clave = "ClaveNueva1!";
    private const string Origen = "http://127.0.0.1:5173";
    private static readonly DateTime Instante = DateTime.SpecifyKind(DateTime.UnixEpoch.AddSeconds(1_700_000_015), DateTimeKind.Utc);

    [Fact]
    public async Task Vinculacion_NoEntregaSesion_YConfirmacionActivaMfa()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, obligatorio: true);
        var http = Http(generacion: Guid.NewGuid());
        var login = await Controlador(http, armado).Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave });
        var pendiente = Assert.IsType<UnauthorizedObjectResult>(login);
        Assert.Equal("vinculacion_mfa_pendiente", Codigo(pendiente.Value));
        Assert.Null(pendiente.Value?.GetType().GetProperty("token")?.GetValue(pendiente.Value));
        Assert.False(http.Response.Headers.ContainsKey("Set-Cookie"));

        var desafio = Texto(pendiente.Value, "desafio");
        var ajeno = await Verificacion(http, armado, desafio, "123456");
        Assert.Equal("mfa_invalido", Codigo(Assert.IsType<UnauthorizedObjectResult>(ajeno).Value));
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == armado.Usuario.Id)).TotpHabilitado);

        var inicio = Assert.IsType<OkObjectResult>(await Controlador(http, armado).IniciarVinculacion(new IniciarVinculacionMfaDto { Desafio = desafio }));
        var secreto = Secreto(inicio.Value);
        var operacion = Texto(inicio.Value, "operacion");
        var confirmado = Assert.IsType<OkObjectResult>(await Controlador(http, armado).ConfirmarVinculacion(new ConfirmarVinculacionMfaDto
        {
            Desafio = desafio,
            Operacion = operacion,
            Codigo = ValidadorTotp.CodigoEn(secreto, Instante)
        }));
        var codigos = confirmado.Value?.GetType().GetProperty("codigos")?.GetValue(confirmado.Value) as System.Collections.IEnumerable;
        Assert.Equal(10, codigos?.Cast<object>().Count());
        Assert.Null(confirmado.Value?.GetType().GetProperty("token")?.GetValue(confirmado.Value));

        var usuario = await context.Usuario.SingleAsync(u => u.Id == armado.Usuario.Id);
        Assert.True(usuario.TotpHabilitado);
        Assert.True(usuario.MfaObligatorio);
        Assert.Equal(1, usuario.TokenVersion);
        var repetido = await Controlador(http, armado).ConfirmarVinculacion(new ConfirmarVinculacionMfaDto
        {
            Desafio = desafio,
            Operacion = operacion,
            Codigo = ValidadorTotp.CodigoEn(secreto, Instante)
        });
        Assert.Null(Assert.IsType<UnauthorizedObjectResult>(repetido).Value?.GetType().GetProperty("codigos")?.GetValue(Assert.IsType<UnauthorizedObjectResult>(repetido).Value));
    }

    [Fact]
    public async Task CambioDePassword_EsperaALaVinculacion_YLaBanderaApagadaNoAbreSesion()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, obligatorio: true, cambio: true);
        var http = Http(generacion: null);
        var login = Assert.IsType<UnauthorizedObjectResult>(await Controlador(http, armado).Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave }));
        Assert.Equal("vinculacion_mfa_pendiente", Codigo(login.Value));

        await using var apagado = Memoria();
        var sinBandera = await ArmarAsync(apagado, obligatorio: true, habilitar: false);
        var bloqueado = await Controlador(Http(null), sinBandera, habilitar: false).Login(new LoginRequestDto { Email = sinBandera.Usuario.Correo, Password = Clave });
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(bloqueado).StatusCode);
        Assert.Equal("vinculacion_no_disponible", Codigo(Assert.IsType<ObjectResult>(bloqueado).Value));
    }

    [Fact]
    public async Task DesafioVencido_NoConfirma()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, obligatorio: true);
        var http = Http(null);
        var login = Assert.IsType<UnauthorizedObjectResult>(await Controlador(http, armado).Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave }));
        armado.Servicio.Reloj = () => DateTime.UtcNow.AddMinutes(11);
        var inicio = await Controlador(http, armado).IniciarVinculacion(new IniciarVinculacionMfaDto { Desafio = Texto(login.Value, "desafio") });
        Assert.IsType<UnauthorizedObjectResult>(inicio);
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == armado.Usuario.Id)).TotpHabilitado);
    }

    private static async Task<IActionResult> Verificacion(DefaultHttpContext http, Armado armado, string desafio, string codigo)
    {
        return await Controlador(http, armado).VerificarMfa(new VerificarMfaDto { Desafio = desafio, Codigo = codigo });
    }

    [Fact]
    public async Task DesafioDeVinculacionNoConsultaElEstado()
    {
        await using var context = Memoria();
        var armado = await ArmarAsync(context, obligatorio: true);
        var http = Http(generacion: Guid.NewGuid());
        var login = Assert.IsType<UnauthorizedObjectResult>(await Controlador(http, armado).Login(new LoginRequestDto { Email = armado.Usuario.Correo, Password = Clave }));
        var desafio = Texto(login.Value, "desafio");
        Assert.False(string.IsNullOrWhiteSpace(desafio));

        http.User = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.IsType<UnauthorizedObjectResult>(await Controlador(http, armado).EstadoMfa());

        http.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, desafio),
            new Claim("token_use", "web"),
            new Claim("token_version", "0")
        ], "test"));
        Assert.IsType<UnauthorizedObjectResult>(await Controlador(http, armado).EstadoMfa());
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == armado.Usuario.Id)).TotpHabilitado);
    }

    private static async Task<Armado> ArmarAsync(AppDbContext context, bool obligatorio, bool cambio = false, bool habilitar = true)
    {
        if (!await context.Empresa.AnyAsync())
        {
            context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno", CUIT = "30-11111111-1" });
            await context.SaveChangesAsync();
        }

        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "vinculo",
            Correo = "vinculo@local.test",
            Rol = "RRHH",
            Activo = true,
            MfaObligatorio = obligatorio,
            RequiereCambioPassword = cambio,
            PasswordTemporalVenceEn = cambio ? DateTime.UtcNow.AddHours(1) : null
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Clave);
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        var protector = new ProtectorSecretosMfa("k1", new Dictionary<string, byte[]> { ["k1"] = RandomNumberGenerator.GetBytes(32) });
        var config = Configuracion(habilitar);
        var limite = new LimiteMfaPorIp();
        var jwt = new JwtTokenService(config);
        var auth = new AuthService(context, new PasswordHasher<Usuario>(), jwt, configuration: config, protector: protector, limite: limite);
        var verificacion = new VerificacionMfaService(context, config, protector, jwt, new SesionWebService(context, config, jwt), limite) { Reloj = () => Instante };
        var servicio = new EnrolamientoMfaService(context, config, protector, new PasswordHasher<Usuario>(), limite) { Reloj = () => Instante };
        return new Armado(context, usuario, auth, verificacion, servicio);
    }

    private static AuthController Controlador(DefaultHttpContext http, Armado armado, bool habilitar = true)
    {
        var config = Configuracion(habilitar);
        return new AuthController(
            armado.Auth,
            new AgenteAuthService(armado.Context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(config)),
            config,
            mfa: armado.Verificacion,
            enrolamiento: armado.Servicio)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static DefaultHttpContext Http(Guid? generacion)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["Origin"] = Origen;
        if (generacion.HasValue)
        {
            http.Request.Headers["X-CF-Solicitud"] = "1";
            http.Request.Headers["X-CF-Generacion"] = generacion.Value.ToString("D");
        }

        http.Response.Body = new MemoryStream();
        return http;
    }

    private static IConfiguration Configuracion(bool habilitar)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
            ["Jwt:Issuer"] = "ControlFichajes.Tests",
            ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
            ["Jwt:ExpireMinutes"] = "60",
            ["Auth:PermitirEnrolamientoMfa"] = habilitar ? "true" : "false",
            ["Auth:MfaEnrolamientoMinutos"] = "10",
            ["Cors:AllowedOrigins:0"] = Origen
        }).Build();
    }

    private static AppDbContext Memoria()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    private static string? Codigo(object? valor) => valor?.GetType().GetProperty("codigo")?.GetValue(valor) as string;

    private static string Texto(object? valor, string nombre) => valor?.GetType().GetProperty(nombre)?.GetValue(valor) as string ?? "";

    private static byte[] Secreto(object? valor)
    {
        var uri = Texto(valor, "uri");
        return OtpNet.Base32Encoding.ToBytes(uri.Split("secret=")[1].Split('&')[0]);
    }

    private sealed record Armado(AppDbContext Context, Usuario Usuario, AuthService Auth, VerificacionMfaService Verificacion, EnrolamientoMfaService Servicio);
}
