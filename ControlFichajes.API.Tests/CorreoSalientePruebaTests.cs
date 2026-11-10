using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFichajes.API.Tests;

public class CorreoSalientePruebaTests
{
    private const string ClaveSmtp = "clave-smtp-que-no-sale";

    [Fact]
    public async Task AdminYRrhhNoConsultanNiEnvian()
    {
        await using var context = CrearContexto();
        var admin = await AgregarUsuarioAsync(context, AppRoles.Admin);
        var rrhh = await AgregarUsuarioAsync(context, AppRoles.Rrhh);
        var buzon = new Buzon();

        foreach (var usuario in new[] { admin, rrhh })
        {
            var controller = CrearControlador(context, Principal(usuario.Id, usuario.Rol), buzon, ConfiguracionCompleta());
            Assert.IsType<UnauthorizedObjectResult>(controller.Estado());
            Assert.IsType<UnauthorizedObjectResult>(await controller.Probar(Solicitud("destino@empresa.test")));
        }

        Assert.Empty(buzon.Mensajes);
        Assert.Empty(await context.AuditoriaSeguridad.ToListAsync());
    }

    [Fact]
    public async Task ConfiguracionIncompletaNoEnviaNiDevuelveLaClave()
    {
        await using var context = CrearContexto();
        var super = await AgregarUsuarioAsync(context, AppRoles.SuperAdmin);
        var buzon = new Buzon();
        var controller = CrearControlador(context, Principal(super.Id, AppRoles.SuperAdmin), buzon, ConfiguracionIncompleta(ClaveSmtp));

        var estado = Assert.IsType<OkObjectResult>(controller.Estado());
        var dto = Assert.IsType<EstadoCorreoSalienteDto>(estado.Value);
        Assert.False(dto.Configurado);
        Assert.Equal("no-reply@controldeaccesos.online", dto.Remitente);
        var envio = Assert.IsType<ObjectResult>(await controller.Probar(Solicitud("destino@empresa.test")));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, envio.StatusCode);

        var json = JsonSerializer.Serialize(new[] { estado.Value, envio.Value }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("smtp.hostinger.com", dto.Servidor);
        Assert.Equal(465, dto.Puerto);
        Assert.Equal(ConexionSmtpReglas.TlsConexion, dto.Cifrado);
        Assert.True(dto.ContrasenaConfigurada);
        Assert.False(dto.Guardada);
        Assert.DoesNotContain(ClaveSmtp, json, StringComparison.Ordinal);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(buzon.Mensajes);
        var auditoria = Assert.Single(await context.AuditoriaSeguridad.ToListAsync());
        Assert.Equal("correo_prueba_rechazado", auditoria.Evento);
        Assert.Equal("rechazado", auditoria.Resultado);
        Assert.Equal("configuracion", auditoria.Detalle);
        Assert.Equal(super.Id, auditoria.UsuarioId);
        Assert.Equal(super.Correo, auditoria.Correo);
        Assert.DoesNotContain("destino@empresa.test", auditoria.Correo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuperAdminEnviaUnMensajeFijoCuandoElServidorAcepta()
    {
        await using var context = CrearContexto();
        var super = await AgregarUsuarioAsync(context, AppRoles.SuperAdmin);
        var buzon = new Buzon();
        var controller = CrearControlador(context, Principal(super.Id, AppRoles.SuperAdmin), buzon, ConfiguracionCompleta());

        var estado = Assert.IsType<EstadoCorreoSalienteDto>(Assert.IsType<OkObjectResult>(controller.Estado()).Value);
        Assert.True(estado.Configurado);
        var envio = Assert.IsType<OkObjectResult>(await controller.Probar(Solicitud(" destino@empresa.test ")));
        var respuesta = Assert.IsType<ProbarCorreoSalienteRespuestaDto>(envio.Value);
        Assert.Equal(CorreoPruebaService.MensajeEnviado, respuesta.Mensaje);
        Assert.NotEqual("Configurado", respuesta.Mensaje);

        var mensaje = Assert.Single(buzon.Mensajes);
        Assert.Equal("destino@empresa.test", mensaje.Destinatario);
        Assert.Equal(CorreoPruebaService.Asunto, mensaje.Asunto);
        Assert.Equal(CorreoPruebaService.Texto, mensaje.Texto);
        Assert.Equal("<p>" + CorreoPruebaService.Texto + "</p>", mensaje.Html);
        Assert.DoesNotContain("http", mensaje.Texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", mensaje.Texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", mensaje.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ClaveSmtp, mensaje.Texto, StringComparison.Ordinal);
        var auditoria = Assert.Single(await context.AuditoriaSeguridad.ToListAsync());
        Assert.Equal("correo_prueba_enviado", auditoria.Evento);
        Assert.Equal("aceptado", auditoria.Resultado);
        Assert.Null(auditoria.Detalle);
        Assert.Equal(super.Id, auditoria.UsuarioId);
        Assert.Equal(super.Correo, auditoria.Correo);
    }

    [Fact]
    public async Task FalloDelServidorNoSeInformaComoEnviadoNiGuardaElSecreto()
    {
        await using var context = CrearContexto();
        var super = await AgregarUsuarioAsync(context, AppRoles.SuperAdmin);
        var controller = CrearControlador(context, Principal(super.Id, AppRoles.SuperAdmin), new SmtpQueFalla(), ConfiguracionCompleta());

        var envio = Assert.IsType<ObjectResult>(await controller.Probar(Solicitud("destino@empresa.test")));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, envio.StatusCode);
        var json = JsonSerializer.Serialize(envio.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("No se pudo enviar el correo de prueba.", json, StringComparison.Ordinal);
        Assert.DoesNotContain(CorreoPruebaService.MensajeEnviado, json, StringComparison.Ordinal);
        Assert.DoesNotContain("secreto-del-servidor", json, StringComparison.Ordinal);
        var auditoria = Assert.Single(await context.AuditoriaSeguridad.ToListAsync());
        Assert.Equal("correo_prueba_rechazado", auditoria.Evento);
        Assert.Equal("smtp", auditoria.Detalle);
        Assert.DoesNotContain("secreto-del-servidor", auditoria.Detalle ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void LaPruebaTieneLimiteDeFrecuencia()
    {
        var atributo = typeof(CorreoSalienteController)
            .GetMethod(nameof(CorreoSalienteController.Probar))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();
        Assert.Equal(CorreoSalienteController.PoliticaLimitePrueba, atributo!.PolicyName);
        Assert.Equal(5, AuthPolicy.CorreoPruebaLimite(new ConfigurationBuilder().Build()));
        Assert.Equal(60, AuthPolicy.CorreoPruebaVentanaSegundos(new ConfigurationBuilder().Build()));
    }

    [Fact]
    public async Task LaPruebaDelBorradorNoGuardaYUnDestinoAjenoSeRechaza()
    {
        await using var context = CrearContexto();
        var super = await AgregarUsuarioAsync(context, AppRoles.SuperAdmin);
        var archivo = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var buzon = new Buzon();
        var controller = CrearControlador(context, Principal(super.Id, AppRoles.SuperAdmin), buzon, ConfiguracionConArchivo(archivo));

        var ajeno = Assert.IsType<BadRequestObjectResult>(await controller.Probar(new ProbarCorreoSalienteDto
        {
            Destinatario = "destino@empresa.test",
            UsarBorrador = true,
            Servidor = "smtp.otro.test",
            Puerto = 465,
            Cifrado = ConexionSmtpReglas.TlsConexion,
            Remitente = "no-reply@controldeaccesos.online"
        }));
        var jsonAjeno = JsonSerializer.Serialize(ajeno.Value);
        Assert.DoesNotContain(ClaveSmtp, jsonAjeno, StringComparison.Ordinal);
        Assert.False(File.Exists(archivo));
        Assert.Empty(buzon.Mensajes);

        var prueba = Assert.IsType<OkObjectResult>(await controller.Probar(new ProbarCorreoSalienteDto
        {
            Destinatario = "destino@empresa.test",
            UsarBorrador = true,
            Servidor = "127.0.0.1",
            Puerto = 1025,
            Cifrado = ConexionSmtpReglas.Ninguno,
            Remitente = "no-reply@controldeaccesos.online"
        }));
        Assert.Equal(CorreoPruebaService.MensajeEnviado, Assert.IsType<ProbarCorreoSalienteRespuestaDto>(prueba.Value).Mensaje);
        Assert.False(File.Exists(archivo));
        var estado = Assert.IsType<EstadoCorreoSalienteDto>(Assert.IsType<OkObjectResult>(controller.Estado()).Value);
        Assert.Equal("smtp.hostinger.com", estado.Servidor);
        Assert.False(estado.Guardada);
    }

    [Fact]
    public async Task GuardarCifraLaContrasenaYUnaVaciaConservaLaAnterior()
    {
        await using var context = CrearContexto();
        var super = await AgregarUsuarioAsync(context, AppRoles.SuperAdmin);
        var archivo = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var configuracion = ConfiguracionConArchivo(archivo);
        var controller = CrearControlador(context, Principal(super.Id, AppRoles.SuperAdmin), new Buzon(), configuracion);

        var guardado = Assert.IsType<OkObjectResult>(await controller.Guardar(new GuardarConfiguracionCorreoDto
        {
            Servidor = "127.0.0.1",
            Puerto = 1025,
            Cifrado = ConexionSmtpReglas.Ninguno,
            Usuario = "captura",
            Remitente = "no-reply@controldeaccesos.online",
            CambiarContrasena = true,
            Contrasena = "clave-nueva-local"
        }));
        var cuerpo = Assert.IsType<GuardarConfiguracionCorreoRespuestaDto>(guardado.Value);
        Assert.Equal(CorreoSalienteController.MensajeGuardado, cuerpo.Mensaje);
        Assert.True(cuerpo.Configuracion.ContrasenaConfigurada);
        Assert.True(cuerpo.Configuracion.Guardada);
        Assert.DoesNotContain("clave-nueva-local", File.ReadAllText(archivo), StringComparison.Ordinal);

        var conservado = Assert.IsType<OkObjectResult>(await controller.Guardar(new GuardarConfiguracionCorreoDto
        {
            Servidor = "127.0.0.1",
            Puerto = 1025,
            Cifrado = ConexionSmtpReglas.Ninguno,
            Usuario = "captura",
            Remitente = "no-reply@controldeaccesos.online",
            CambiarContrasena = false,
            Contrasena = ""
        }));
        var servicio = new ConfiguracionCorreoServicio(
            configuracion,
            new ProtectorContrasenaSmtp(configuracion),
            NullLogger<ConfiguracionCorreoServicio>.Instance,
            archivo);
        Assert.Equal("clave-nueva-local", servicio.Efectiva().Contrasena);
        Assert.DoesNotContain("clave-nueva-local", File.ReadAllText(archivo), StringComparison.Ordinal);
        var publico = JsonSerializer.Serialize(Assert.IsType<GuardarConfiguracionCorreoRespuestaDto>(conservado.Value));
        Assert.DoesNotContain("clave-nueva-local", publico, StringComparison.Ordinal);
        File.Delete(archivo);
    }

    private static CorreoSalienteController CrearControlador(
        AppDbContext context,
        ClaimsPrincipal user,
        ICorreoSaliente correo,
        IConfiguration configuration)
    {
        var auditoria = new AuditoriaSeguridadService(
            new AuditoriaDbContextFactory(OpcionesDe(context)),
            NullLogger<AuditoriaSeguridadService>.Instance,
            new HttpContextAccessor());
        var activa = new ConfiguracionCorreoServicio(
            configuration,
            new ProtectorContrasenaSmtp(configuration),
            NullLogger<ConfiguracionCorreoServicio>.Instance,
            configuration["Smtp:ArchivoPanel"]);
        var directo = correo as ICorreoConConexion ?? throw new InvalidOperationException();
        var prueba = new CorreoPruebaService(
            context,
            configuration,
            activa,
            correo,
            directo,
            auditoria,
            NullLogger<CorreoPruebaService>.Instance);
        return new CorreoSalienteController(configuration, activa, prueba, context, auditoria)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static IConfiguration ConfiguracionCompleta()
    {
        return Configuracion("no-reply@controldeaccesos.online", "clave-completa");
    }

    private static IConfiguration ConfiguracionIncompleta(string clave)
    {
        return Configuracion(string.Empty, clave);
    }

    private static IConfiguration ConfiguracionConArchivo(string archivo)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smtp:Host"] = "smtp.hostinger.com",
                ["Smtp:Port"] = "465",
                ["Smtp:User"] = "no-reply@controldeaccesos.online",
                ["Smtp:Password"] = string.Empty,
                ["Smtp:From"] = "no-reply@controldeaccesos.online",
                ["Smtp:EnableSsl"] = "true",
                ["Smtp:PermitirCapturadorLocal"] = "true",
                ["Smtp:ArchivoPanel"] = archivo,
                ["Smtp:ProtectionKey"] = Convert.ToBase64String(new byte[32])
            })
            .Build();
    }

    private static IConfiguration Configuracion(string usuario, string clave)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smtp:Host"] = "smtp.hostinger.com",
                ["Smtp:Port"] = "465",
                ["Smtp:User"] = usuario,
                ["Smtp:Password"] = clave,
                ["Smtp:From"] = "no-reply@controldeaccesos.online",
                ["Smtp:EnableSsl"] = "true"
            })
            .Build();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AppDbContext, OpcionesGuardadas> Opciones = new();

    private static DbContextOptions<AppDbContext> OpcionesDe(AppDbContext context)
    {
        return Opciones.TryGetValue(context, out var guardadas)
            ? guardadas.Options
            : throw new InvalidOperationException();
    }

    private static AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
        Opciones.Add(context, new OpcionesGuardadas { Options = options });
        context.Empresa.Add(new Empresa
        {
            Id = 1,
            NombreFantasia = "Uno",
            RazonSocial = "Uno S.A.",
            CUIT = "30-11111111-1"
        });
        context.SaveChanges();
        return context;
    }

    private static async Task<Usuario> AgregarUsuarioAsync(AppDbContext context, string rol)
    {
        var usuario = new Usuario
        {
            EmpresaId = 1,
            NombreUsuario = rol.ToLowerInvariant() + ".local",
            Correo = rol.ToLowerInvariant() + "@empresa.test",
            Rol = rol,
            Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, "Password123!");
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }

    private static ClaimsPrincipal Principal(int usuarioId, string rol)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
                new Claim(ClaimTypes.Role, rol),
                new Claim("token_use", "web")
            ],
            "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    private static ProbarCorreoSalienteDto Solicitud(string destinatario)
    {
        return new ProbarCorreoSalienteDto { Destinatario = destinatario };
    }

    private sealed class OpcionesGuardadas
    {
        public required DbContextOptions<AppDbContext> Options { get; init; }
    }

    private sealed class Buzon : ICorreoSaliente, ICorreoConConexion
    {
        public List<CorreoSaliente> Mensajes { get; } = [];

        public Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            Mensajes.Add(mensaje);
            return Task.CompletedTask;
        }

        public Task EnviarAsync(ConexionSmtp conexion, CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            return EnviarAsync(mensaje, cancellationToken);
        }
    }

    private sealed class SmtpQueFalla : ICorreoSaliente, ICorreoConConexion
    {
        public Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("secreto-del-servidor");
        }

        public Task EnviarAsync(ConexionSmtp conexion, CorreoSaliente mensaje, CancellationToken cancellationToken = default)
        {
            return EnviarAsync(mensaje, cancellationToken);
        }
    }
}
