using System.Runtime.CompilerServices;
using System.Text.Json;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ControlFichajes.API.Tests;

public class BootstrapSeguridadTests
{
    private const string Password = "Password123!";

    [Fact]
    public async Task FlagApagadoYTablaVacia_RechazaSinCrearUsuarioNiToken()
    {
        await using var context = CrearContexto();
        var controller = CrearControlador(context, permitir: false);

        var result = await controller.Bootstrap(Solicitud("admin.local", "bootstrap@empresa.test", "SuperAdmin"));

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, await context.Usuario.CountAsync());
        var evento = Assert.Single(await context.AuditoriaSeguridad.ToListAsync());
        Assert.Equal("bootstrap_deshabilitado", evento.Evento);
        Assert.Equal("rechazado", evento.Resultado);
        Assert.Null(evento.Correo);
        Assert.Null(evento.Detalle);
        AssertNoContiene(context, Password, "bootstrap@empresa.test", "SuperAdmin");
    }

    [Fact]
    public async Task FlagApagadoYTablaConUsuarios_Rechaza()
    {
        await using var context = CrearContexto();
        await AgregarUsuarioAsync(context, "admin@empresa.test");
        var controller = CrearControlador(context, permitir: false);

        var result = await controller.Bootstrap(Solicitud("otro.admin", "otro@empresa.test", "ADMIN"));

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(1, await context.Usuario.CountAsync());
        Assert.DoesNotContain(await context.Usuario.Select(u => u.Correo).ToListAsync(), c => c == "otro@empresa.test");
    }

    [Fact]
    public async Task FlagEncendidoYTablaVacia_ConservaElAltaControlada()
    {
        await using var context = CrearContexto();
        var controller = CrearControlador(context, permitir: true);

        var result = await controller.Bootstrap(Solicitud("admin.local", "bootstrap@empresa.test", "ADMIN"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AuthResponseDto>(ok.Value);
        AssertContrato(body);
        var usuario = Assert.Single(await context.Usuario.ToListAsync());
        Assert.Equal(AppRoles.Admin, usuario.Rol);
        var evento = Assert.Single(await context.AuditoriaSeguridad.ToListAsync());
        Assert.Equal("usuario_bootstrap", evento.Evento);
        Assert.Equal("aceptado", evento.Resultado);
        AssertNoContiene(context, Password, body.Token);
    }

    [Fact]
    public async Task FlagEncendidoYTablaConUsuarios_Rechaza()
    {
        await using var context = CrearContexto();
        await AgregarUsuarioAsync(context, "admin@empresa.test");
        var controller = CrearControlador(context, permitir: true);

        var result = await controller.Bootstrap(Solicitud("otro.admin", "otro@empresa.test", "ADMIN"));

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(1, await context.Usuario.CountAsync());
        Assert.Empty(await context.AuditoriaSeguridad.ToListAsync());
    }

    [Fact]
    public async Task CuerpoNoPuedeImponerSuperAdmin()
    {
        await using var context = CrearContexto();
        var controller = CrearControlador(context, permitir: true);

        var result = await controller.Bootstrap(Solicitud("admin.local", "bootstrap@empresa.test", "SuperAdmin"));

        Assert.IsType<OkObjectResult>(result);
        var usuario = Assert.Single(await context.Usuario.ToListAsync());
        Assert.Equal(AppRoles.Admin, usuario.Rol);
        Assert.NotEqual(AppRoles.SuperAdmin, usuario.Rol);
    }

    [Fact]
    public async Task FalloDeAuditoria_NoCambiaElRechazoNiElAlta()
    {
        await using var vacio = CrearContexto();
        var registro = new RegistroDeLog();
        var apagado = CrearControlador(vacio, permitir: false, new AuditoriaQueFalla(), registro.Para<AuthController>());
        var rechazo = await apagado.Bootstrap(Solicitud("admin.local", "bootstrap@empresa.test", "ADMIN"));
        Assert.IsType<NotFoundResult>(rechazo);
        Assert.Equal(0, await vacio.Usuario.CountAsync());
        var texto = string.Join('\n', registro.Mensajes);
        Assert.Contains("bootstrap_deshabilitado", texto, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap@empresa.test", texto, StringComparison.Ordinal);

        await using var alta = CrearContexto();
        var encendido = CrearControlador(alta, permitir: true, new AuditoriaQueFalla(), new RegistroDeLog().Para<AuthController>());
        var creado = await encendido.Bootstrap(Solicitud("admin.local", "bootstrap@empresa.test", "ADMIN"));
        var ok = Assert.IsType<OkObjectResult>(creado);
        AssertContrato(Assert.IsType<AuthResponseDto>(ok.Value));
        Assert.Equal(1, await alta.Usuario.CountAsync());
        Assert.Empty(alta.ChangeTracker.Entries<AuditoriaSeguridad>());
    }

    [Fact]
    public async Task LoginYAgente_ConservanSuContrato()
    {
        await using var context = CrearContexto();
        var configuration = CrearConfiguracion();
        await AgregarUsuarioAsync(context, "admin@empresa.test");
        var sucursal = new Sucursal { Nombre = "Central", EmpresaId = 1, SerialLector = "lector-local" };
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
            CrearServicio(context, configuration),
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(configuration)),
            ConfiguracionBootstrap(false));

        var login = await controller.Login(new LoginRequestDto
        {
            Email = "admin@empresa.test",
            Password = Password
        });
        var loginOk = Assert.IsType<OkObjectResult>(login);
        AssertContrato(Assert.IsType<AuthResponseDto>(loginOk.Value));

        var agent = await controller.LoginAgente(new AgenteLoginDto
        {
            ClientId = "agente-local",
            ClientSecret = secreto
        });
        var agentOk = Assert.IsType<OkObjectResult>(agent);
        var body = Assert.IsType<AuthResponseDto>(agentOk.Value);
        AssertContrato(body);
        Assert.False(body.RequiereCambioPassword);
        Assert.DoesNotContain(
            await context.AuditoriaSeguridad.Select(a => a.Evento).ToListAsync(),
            evento => evento == "usuario_bootstrap" || evento == "bootstrap_deshabilitado");
    }

    private static UsuarioRegistroDto Solicitud(string nombre, string correo, string rol)
    {
        return new UsuarioRegistroDto
        {
            EmpresaId = 1,
            NombreUsuario = nombre,
            Email = correo,
            Password = Password,
            Rol = rol
        };
    }

    private static void AssertContrato(AuthResponseDto result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var documento = JsonDocument.Parse(json);
        var nombres = documento.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(["mensaje", "requiereCambioPassword", "token"], nombres);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
    }

    private static void AssertNoContiene(AppDbContext context, params string[] secretos)
    {
        foreach (var evento in context.AuditoriaSeguridad)
        {
            var texto = string.Join('\n', evento.Correo, evento.Evento, evento.Resultado, evento.Ip, evento.UserAgent, evento.Detalle);
            foreach (var secreto in secretos)
                Assert.DoesNotContain(secreto, texto, StringComparison.Ordinal);
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

    private static AuthController CrearControlador(
        AppDbContext context,
        bool permitir,
        IAuditoriaSeguridad? auditoria = null,
        ILogger<AuthController>? logger = null)
    {
        var configuration = CrearConfiguracion();
        auditoria ??= new AuditoriaSeguridadService(
            new AuditoriaDbContextFactory(Opciones.GetValue(context, _ => throw new InvalidOperationException()).Options),
            new RegistroDeLog().Para<AuditoriaSeguridadService>(),
            new HttpContextAccessor());
        return new AuthController(
            CrearServicio(context, configuration, auditoria),
            new AgenteAuthService(context, new PasswordHasher<AgenteInstalacion>(), new JwtTokenService(configuration)),
            ConfiguracionBootstrap(permitir),
            auditoria,
            logger);
    }

    private static AuthService CrearServicio(
        AppDbContext context,
        IConfiguration configuration,
        IAuditoriaSeguridad? auditoria = null)
    {
        return new AuthService(
            context,
            new PasswordHasher<Usuario>(),
            new JwtTokenService(configuration),
            auditoria);
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

    private static IConfiguration ConfiguracionBootstrap(bool permitir)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:PermitirBootstrap"] = permitir ? "true" : "false"
            })
            .Build();
    }

    private static async Task AgregarUsuarioAsync(AppDbContext context, string correo)
    {
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = "admin.local",
            Correo = correo,
            Rol = AppRoles.Admin,
            Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Password);
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
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
            public bool IsEnabled(LogLevel logLevel) => true;
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
