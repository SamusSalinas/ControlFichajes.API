using System.Text.Json;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
    public async Task QuintoIntento_RegistraFalloYBloqueo()
    {
        await using var context = CrearContexto();
        var service = CrearServicio(context);
        var usuario = await AgregarUsuarioAsync(context, "admin@empresa.test");
        usuario.IntentosFallidos = 4;
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
        AssertNoContieneSecretos(context, "ClaveMal1!", Password);
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

    private static AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
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

    private static AuthService CrearServicio(AppDbContext context)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
                ["Jwt:Issuer"] = "ControlFichajes.Tests",
                ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
                ["Jwt:ExpireMinutes"] = "60"
            })
            .Build();
        return new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(configuration));
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
