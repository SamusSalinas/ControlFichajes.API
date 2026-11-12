using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ControlFichajes.API.Tests;

public class AuditoriaSeguridadTests
{
    private const string Password = "Password123!";

    [Fact]
    public async Task LoginFallido_RegistraEventoSinLaContrasena()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        await AgregarUsuarioAsync(context, "admin@empresa.test");

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = "admin@empresa.test",
            Password = "ClaveMal1!"
        });

        Assert.Null(result);
        var evento = Assert.Single(context.AuditoriaSeguridad);
        Assert.Equal("login_fallido", evento.Evento);
        Assert.Equal("rechazado", evento.Resultado);
        AssertNoContieneSecretos(context, "ClaveMal1!", Password);
    }

    [Fact]
    public async Task TercerIntento_RegistraFalloYBloqueo()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        var usuario = await AgregarUsuarioAsync(context, "admin@empresa.test");
        usuario.IntentosFallidos = 2;
        await context.SaveChangesAsync();

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = "ClaveMal1!"
        });

        Assert.Null(result);
        var eventos = context.AuditoriaSeguridad.Select(a => a.Evento).OrderBy(e => e).ToArray();
        Assert.Equal(["login_bloqueado", "login_fallido"], eventos);
        Assert.NotNull(usuario.BloqueadoHasta);
        Assert.True(usuario.BloqueadoHasta > DateTime.UtcNow.AddMinutes(14));
        AssertNoContieneSecretos(context, "ClaveMal1!", Password);
    }

    [Fact]
    public async Task DosIntentos_NoBloqueanYLaClaveValidaReiniciaElContador()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        var usuario = await AgregarUsuarioAsync(context, "rrhh@empresa.test", rol: "RRHH");

        Assert.Null(await service.LoginAsync(new LoginRequestDto { Email = usuario.Correo, Password = "ClaveMal1!" }));
        Assert.Null(await service.LoginAsync(new LoginRequestDto { Email = usuario.Correo, Password = "ClaveMal1!" }));
        Assert.Equal(2, usuario.IntentosFallidos);
        Assert.Null(usuario.BloqueadoHasta);

        var ingreso = await service.LoginAsync(new LoginRequestDto { Email = usuario.Correo, Password = Password });

        Assert.NotNull(ingreso);
        Assert.Equal(0, usuario.IntentosFallidos);
        Assert.Null(usuario.BloqueadoHasta);
    }

    [Fact]
    public async Task SuperAdmin_NoSeBloqueaYUnBloqueoPrevioNoImpideLaClaveValida()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        var libre = await AgregarUsuarioAsync(context, "super.libre@empresa.test", rol: "SuperAdmin");
        for (var intento = 0; intento < 6; intento++)
            Assert.Null(await service.LoginAsync(new LoginRequestDto { Email = libre.Correo, Password = "ClaveMal1!" }));

        Assert.Equal(0, libre.IntentosFallidos);
        Assert.Null(libre.BloqueadoHasta);
        Assert.NotNull(await service.LoginAsync(new LoginRequestDto { Email = libre.Correo, Password = Password }));

        var previo = await AgregarUsuarioAsync(context, "super.previo@empresa.test", rol: "SuperAdmin");
        previo.IntentosFallidos = 3;
        previo.BloqueadoHasta = DateTime.UtcNow.AddMinutes(15);
        await context.SaveChangesAsync();

        var ingreso = await service.LoginAsync(new LoginRequestDto { Email = previo.Correo, Password = Password });

        Assert.NotNull(ingreso);
        Assert.Equal(0, previo.IntentosFallidos);
        Assert.Null(previo.BloqueadoHasta);
        Assert.DoesNotContain(context.AuditoriaSeguridad, a => a.UsuarioId == previo.Id && a.Evento == "login_bloqueado");
    }

    [Fact]
    public async Task LoginCorrecto_ConservaElContratoYRegistraElAcceso()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        var usuario = await AgregarUsuarioAsync(context, "admin@empresa.test");

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = Password
        });

        Assert.NotNull(result);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var documento = JsonDocument.Parse(json);
        var nombres = documento.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["mensaje", "requiereCambioPassword", "token"], nombres);
        var evento = Assert.Single(context.AuditoriaSeguridad);
        Assert.Equal("login_ok", evento.Evento);
        Assert.Equal(usuario.Id, evento.UsuarioId);
        AssertNoContieneSecretos(context, Password, result!.Token);
    }

    [Fact]
    public async Task CambioYRestablecimiento_NoGuardanLasClaves()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        var usuario = await AgregarUsuarioAsync(context, "admin@empresa.test");

        var cambio = await service.CambiarPasswordAsync(usuario.Id, new CambiarPasswordRequestDto
        {
            PasswordActual = Password,
            NuevaPassword = "NuevaClave123!",
            ConfirmarPassword = "NuevaClave123!"
        });
        var restablecida = await service.RestablecerPasswordAsync(usuario.Id);

        Assert.True(cambio);
        Assert.NotNull(restablecida);
        var eventos = context.AuditoriaSeguridad.Select(a => a.Evento).OrderBy(e => e).ToArray();
        Assert.Equal(["password_cambio", "password_reset_admin"], eventos);
        AssertNoContieneSecretos(context, Password, "NuevaClave123!", restablecida!.PasswordTemporal);
    }

    [Fact]
    public async Task EstadoRolEIdentidad_QuedanAuditadosSinSecretos()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        var usuario = await AgregarUsuarioAsync(context, "rrhh@empresa.test", rol: "RRHH");

        await service.CambiarEstadoAsync(usuario.Id, false);
        await service.CambiarEstadoAsync(usuario.Id, true);
        await service.CambiarRolAsync(usuario.Id, "ADMIN");
        var identidad = await service.CambiarIdentidadAsync(usuario.Id, "rrhh.local", "rrhh.nuevo@empresa.test");

        Assert.Equal(200, identidad.StatusCode);
        var eventos = context.AuditoriaSeguridad.OrderBy(a => a.Id).Select(a => a.Evento).ToArray();
        Assert.Equal(["usuario_estado", "usuario_estado", "usuario_rol", "usuario_identidad"], eventos);
        Assert.Contains(context.AuditoriaSeguridad, a => a.Detalle == "rol=ADMIN");
        AssertNoContieneSecretos(context, Password);
    }

    [Fact]
    public async Task FalloDeAuditoria_NoImpideElLoginCorrecto()
    {
        await using var context = CrearContexto();
        var registro = new RegistroDeLog();
        var service = CrearServicio(context, new AuditoriaQueFalla(), registro.Para<AuthService>());
        var usuario = await AgregarUsuarioAsync(context, "admin@empresa.test");

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = Password
        });

        Assert.NotNull(result);
        AssertContrato(result);
        Assert.Empty(context.ChangeTracker.Entries<AuditoriaSeguridad>());
        Assert.DoesNotContain(Password, string.Join('\n', registro.Mensajes), StringComparison.Ordinal);
        Assert.DoesNotContain(result!.Token, string.Join('\n', registro.Mensajes), StringComparison.Ordinal);
        Assert.Contains(registro.Mensajes, m => m.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FalloDeAuditoria_NoConvierteElRechazoEnError()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context, new AuditoriaQueFalla(), new RegistroDeLog().Para<AuthService>());
        var usuario = await AgregarUsuarioAsync(context, "admin@empresa.test");

        var result = await service.LoginAsync(new LoginRequestDto
        {
            Email = usuario.Correo,
            Password = "ClaveMal1!"
        });

        Assert.Null(result);
        Assert.Equal(1, usuario.IntentosFallidos);
        Assert.Empty(await context.AuditoriaSeguridad.ToListAsync());
    }

    [Fact]
    public async Task FalloDeAuditoria_NoRevierteElCambioDeContrasena()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context, new AuditoriaQueFalla(), new RegistroDeLog().Para<AuthService>());
        var usuario = await AgregarUsuarioAsync(context, "admin@empresa.test");

        var cambio = await service.CambiarPasswordAsync(usuario.Id, new CambiarPasswordRequestDto
        {
            PasswordActual = Password,
            NuevaPassword = "NuevaClave123!",
            ConfirmarPassword = "NuevaClave123!"
        });

        Assert.True(cambio);
        var guardado = await context.Usuario.SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<Usuario>().VerifyHashedPassword(guardado, guardado.PasswordHash, "NuevaClave123!"));
        Assert.Empty(context.ChangeTracker.Entries<AuditoriaSeguridad>());
    }

    [Fact]
    public async Task FalloDeAuditoria_NoDejaElAltaNiElBootstrapSinRespuesta()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context, new AuditoriaQueFalla(), new RegistroDeLog().Para<AuthService>());

        var bootstrap = await service.RegistrarUsuarioAsync(new UsuarioRegistroDto
        {
            EmpresaId = 1,
            NombreUsuario = "admin.local",
            Email = "bootstrap@empresa.test",
            Password = Password,
            Rol = "ADMIN"
        }, bootstrap: true);
        var alta = await service.RegistrarUsuarioAsync(new UsuarioRegistroDto
        {
            EmpresaId = 1,
            NombreUsuario = "rrhh.local",
            Email = "alta@empresa.test",
            Password = Password,
            Rol = "RRHH"
        }, bootstrap: false);

        Assert.NotNull(bootstrap);
        Assert.NotNull(alta);
        AssertContrato(bootstrap!);
        AssertContrato(alta!);
        Assert.Equal(2, await context.Usuario.CountAsync());
        Assert.Empty(await context.AuditoriaSeguridad.ToListAsync());
        Assert.Empty(context.ChangeTracker.Entries<AuditoriaSeguridad>());
    }

    [Fact]
    public async Task Desbloqueo_RegistraUsuarioDesbloqueo()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        var usuario = await AgregarUsuarioAsync(context, "admin@empresa.test");
        usuario.IntentosFallidos = 5;
        usuario.BloqueadoHasta = DateTime.UtcNow.AddMinutes(10);
        await context.SaveChangesAsync();

        var ok = await service.DesbloquearAsync(usuario.Id);

        Assert.True(ok);
        Assert.Null(usuario.BloqueadoHasta);
        var evento = Assert.Single(await context.AuditoriaSeguridad.ToListAsync());
        Assert.Equal("usuario_desbloqueo", evento.Evento);
        Assert.Equal("aceptado", evento.Resultado);
        Assert.Equal(usuario.Id, evento.UsuarioId);
    }

    [Fact]
    public async Task ContrasenaIgualAUnResultado_NoCambiaElResultadoNiSeGuarda()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        await AgregarUsuarioAsync(context, "admin@empresa.test");

        Assert.Null(await service.LoginAsync(new LoginRequestDto
        {
            Email = "admin@empresa.test",
            Password = "aceptado"
        }));
        Assert.Null(await service.LoginAsync(new LoginRequestDto
        {
            Email = "admin@empresa.test",
            Password = "rechazado"
        }));

        var eventos = await context.AuditoriaSeguridad.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, eventos.Count);
        Assert.All(eventos, evento => Assert.Equal("rechazado", evento.Resultado));
        Assert.All(eventos, evento => Assert.Equal("login_fallido", evento.Evento));
        Assert.DoesNotContain(eventos, evento => evento.Correo == "aceptado" || evento.Detalle == "aceptado" || evento.UserAgent == "aceptado");
        Assert.DoesNotContain(eventos, evento => evento.Correo == "rechazado" || evento.Detalle == "rechazado" || evento.UserAgent == "rechazado");
    }

    [Fact]
    public async Task CorreoYUserAgent_SeNormalizanYSeRecortan()
    {
        await using var context = CrearContexto();
        var accessor = new HttpContextAccessor();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.20");
        http.Request.Headers.UserAgent = "\r" + new string('B', 300) + "\n";
        accessor.HttpContext = http;
        var auditoria = CrearAuditoria(context, accessor);

        await auditoria.RegistrarAsync(
            "login_fallido",
            "rechazado",
            null,
            "\n" + new string('c', 120) + "\t",
            null);

        var evento = Assert.Single(await context.AuditoriaSeguridad.ToListAsync());
        Assert.Equal(new string('c', 100), evento.Correo);
        Assert.Equal(new string('B', 256), evento.UserAgent);
        Assert.Equal("192.0.2.20", evento.Ip);
        Assert.Equal("login_fallido", evento.Evento);
        Assert.Equal("rechazado", evento.Resultado);
    }

    [Fact]
    public async Task ValoresNoControlados_NoSePersisten()
    {
        await using var context = CrearContexto();
        var auditoria = CrearAuditoria(context, new HttpContextAccessor());

        await auditoria.RegistrarAsync("login_ok", "aceptado", null, null, "cuerpo completo del request");

        Assert.Empty(await context.AuditoriaSeguridad.ToListAsync());
    }

    [Fact]
    public async Task Agente_ConservaElContratoYNoGeneraAuditoriaWeb()
    {
        await using var context = CrearContexto();
        var configuration = CrearConfiguracion();
        var sucursal = new Sucursal
        {
            Nombre = "Central",
            EmpresaId = 1,
            SerialLector = "lector-local"
        };
        context.Sucursal.Add(sucursal);
        await context.SaveChangesAsync();
        var agente = new AgenteInstalacion
        {
            ClientId = "agente-local",
            Nombre = "Agente",
            SucursalId = sucursal.Id,
            Activo = true
        };
        const string secreto = "SecretoAgente1234";
        agente.SecretHash = new PasswordHasher<AgenteInstalacion>().HashPassword(agente, secreto);
        context.AgenteInstalacion.Add(agente);
        await context.SaveChangesAsync();

        var controller = new AuthController(
            CrearServicio(context, configuracion: configuration),
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(configuration)));

        var result = await controller.LoginAgente(new AgenteLoginDto
        {
            ClientId = "agente-local",
            ClientSecret = secreto
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AuthResponseDto>(ok.Value);
        AssertContrato(body);
        Assert.False(body.RequiereCambioPassword);
        Assert.Equal("Autenticación exitosa", body.Mensaje);
        Assert.Empty(await context.AuditoriaSeguridad.ToListAsync());
        AssertNoContieneSecretos(context, secreto, body.Token);
    }

    [Fact]
    public void Proxy_SoloConfiaEnDireccionesConfiguradas()
    {
        var registro = new RegistroDeLog();
        var desactivado = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:Habilitado"] = "false",
                ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.10"
            })
            .Build();

        var apagado = ProxyConfiable.Evaluar(desactivado.GetSection(ProxyConfiable.Seccion), registro);
        Assert.False(apagado.Aplicado);

        var activado = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:Habilitado"] = "true",
                ["ForwardedHeaders:KnownProxies:0"] = "192.0.2.10",
                ["ForwardedHeaders:KnownProxies:1"] = "no-es-una-ip",
                ["ForwardedHeaders:KnownNetworks:0"] = "192.0.2.0/24"
            })
            .Build();
        var encendido = ProxyConfiable.Evaluar(activado.GetSection(ProxyConfiable.Seccion), registro);
        Assert.True(encendido.Aplicado);
        Assert.Equal("192.0.2.10", encendido.Proxy);
        Assert.Equal(1, encendido.Redes);
        Assert.True(encendido.SoloEncabezadoFor);
        Assert.Contains(registro.Mensajes, m => m.Contains("no es una dirección IP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FalloDelContextoDeAuditoria_RegistraTipoSinSecretos()
    {
        var registro = new RegistroDeLog();
        var auditoria = new AuditoriaSeguridadService(
            new FabricaQueFalla(),
            registro.Para<AuditoriaSeguridadService>(),
            new HttpContextAccessor());

        await auditoria.RegistrarAsync("login_ok", "aceptado", null, "persona@empresa.test", null);

        var texto = string.Join('\n', registro.Mensajes);
        Assert.Contains("login_ok", texto, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Password123!", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("persona@empresa.test", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", texto, StringComparison.Ordinal);
    }

    private static void AssertContrato(AuthResponseDto result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var documento = JsonDocument.Parse(json);
        var nombres = documento.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["mensaje", "requiereCambioPassword", "token"], nombres);
    }

    private static void AssertNoContieneSecretos(AppDbContext context, params string[] secretos)
    {
        foreach (var evento in context.AuditoriaSeguridad)
        {
            var texto = string.Join('\n', evento.Correo, evento.Evento, evento.Resultado, evento.Ip, evento.UserAgent, evento.Detalle);
            foreach (var secreto in secretos)
            {
                Assert.False(string.IsNullOrEmpty(secreto));
                Assert.DoesNotContain(secreto, texto, StringComparison.Ordinal);
            }
        }
    }

    private static readonly ConditionalWeakTable<AppDbContext, OpcionesGuardadas> Opciones = new();
    private static readonly InMemoryDatabaseRoot Raiz = new();

    private static AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), Raiz)
            .Options;
        var context = new AppDbContext(options);
        Opciones.Add(context, new OpcionesGuardadas { Options = options });
        context.Empresa.Add(new Empresa
        {
            Id = 1,
            NombreFantasia = "Empresa",
            RazonSocial = "Empresa S.A.",
            CUIT = "30-00000000-0"
        });
        context.SaveChanges();
        return context;
    }

    private static AuditoriaSeguridadService CrearAuditoria(AppDbContext context, IHttpContextAccessor accessor)
    {
        return new AuditoriaSeguridadService(
            new AuditoriaDbContextFactory(Opciones.GetValue(context, _ => throw new InvalidOperationException()).Options),
            new RegistroDeLog().Para<AuditoriaSeguridadService>(),
            accessor);
    }

    private static AuthService CrearServicio(
        AppDbContext context,
        IAuditoriaSeguridad? auditoria = null,
        ILogger<AuthService>? logger = null,
        IConfiguration? configuracion = null)
    {
        auditoria ??= CrearAuditoria(context, new HttpContextAccessor());
        return new AuthService(
            context,
            new PasswordHasher<Usuario>(),
            new JwtTokenService(configuracion ?? CrearConfiguracion()),
            auditoria,
            logger);
    }

    private static IConfiguration CrearConfiguracion()
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

    private sealed class OpcionesGuardadas
    {
        public required DbContextOptions<AppDbContext> Options { get; init; }
    }

    private sealed class AuditoriaQueFalla : IAuditoriaSeguridad
    {
        public Task RegistrarAsync(
            string evento,
            string resultado,
            int? usuarioId,
            string? correo,
            string? detalle,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Password123! eyJhbGci");
        }
    }

    private sealed class FabricaQueFalla : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            throw new InvalidOperationException("Password123! eyJhbGci persona@empresa.test");
        }
    }

    private sealed class RegistroDeLog : ILogger
    {
        public List<string> Mensajes { get; } = [];

        public ILogger<T> Para<T>() => new RegistroTipado<T>(this);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Vacio.Instancia;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Mensajes.Add(formatter(state, exception));
        }

        private sealed class RegistroTipado<T> : ILogger<T>
        {
            private readonly RegistroDeLog _inner;

            public RegistroTipado(RegistroDeLog inner) => _inner = inner;

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        private sealed class Vacio : IDisposable
        {
            public static readonly Vacio Instancia = new();

            public void Dispose()
            {
            }
        }
    }

    private static async Task<Usuario> AgregarUsuarioAsync(AppDbContext context, string correo, string rol = "ADMIN")
    {
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "Usuario",
            Correo = correo,
            Rol = rol,
            Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Password);
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }
}
