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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFichajes.API.Tests;

public class EliminarCuentaUsuarioTests
{
    [Fact]
    public async Task EliminarCuentaDesactivadaLaSacaDelListadoYConservaHistorial()
    {
        await using var context = await ContextoAsync();
        var objetivo = await UsuarioAsync(context, AppRoles.Rrhh, 1, "baja");
        objetivo.Activo = false;
        objetivo.TokenVersion = 3;
        context.Fichada.Add(new Fichada
        {
            EmpleadoId = 41,
            FechaHora = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc),
            TipoRegistro = "Entrada",
            Metodo = "Manual"
        });
        context.AuditoriaSeguridad.Add(new AuditoriaSeguridad
        {
            FechaUtc = DateTime.UtcNow,
            UsuarioId = objetivo.Id,
            Evento = "usuario_estado",
            Resultado = "aceptado",
            Detalle = "inactivo"
        });
        context.RefreshToken.Add(new RefreshToken
        {
            UsuarioId = objetivo.Id,
            TokenHash = "hash-sesion-baja",
            FamiliaId = Guid.NewGuid(),
            ExpiraEn = DateTime.UtcNow.AddDays(1),
            CreadaEn = DateTime.UtcNow,
            TokenVersion = 3
        });
        await context.SaveChangesAsync();

        var respuesta = Assert.IsType<OkObjectResult>(await Controlador(context, 9, AppRoles.SuperAdmin).Eliminar(objetivo.Id));
        Assert.Contains("Cuenta eliminada", JsonSerializer.Serialize(respuesta.Value), StringComparison.Ordinal);

        var guardado = await context.Usuario.SingleAsync(u => u.Id == objetivo.Id);
        Assert.False(guardado.Activo);
        Assert.NotNull(guardado.EliminadoEn);
        Assert.Equal(4, guardado.TokenVersion);
        Assert.NotNull(Assert.Single(context.RefreshToken).RevocadaEn);
        Assert.Equal(1, await context.Fichada.CountAsync());
        Assert.Equal(1, await context.AuditoriaSeguridad.CountAsync(a => a.Evento == "usuario_estado"));

        var listed = Assert.IsType<OkObjectResult>(await Controlador(context, 9, AppRoles.SuperAdmin).GetUsuarios(null, null, null, null, null));
        var usuarios = Assert.IsAssignableFrom<IEnumerable<UsuarioListItemDto>>(listed.Value);
        Assert.DoesNotContain(usuarios, u => u.Id == objetivo.Id);

        var ingreso = await new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(Configuracion()), configuration: Configuracion())
            .IngresarAsync(new LoginRequestDto { Email = objetivo.Correo, Password = "LocalWeb123!" }, "tradicional", null, null);
        Assert.NotEqual(EstadoIngreso.Autenticado, ingreso.Estado);

        guardado.Activo = true;
        await context.SaveChangesAsync();
        var forzado = await new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(Configuracion()), configuration: Configuracion())
            .IngresarAsync(new LoginRequestDto { Email = objetivo.Correo, Password = "LocalWeb123!" }, "tradicional", null, null);
        Assert.NotEqual(EstadoIngreso.Autenticado, forzado.Estado);

        var reactivar = Assert.IsType<NotFoundObjectResult>(
            await Controlador(context, 9, AppRoles.SuperAdmin).CambiarEstado(objetivo.Id, new CambiarEstadoUsuarioDto { Activo = true }));
        Assert.Contains("no encontrado", JsonSerializer.Serialize(reactivar.Value), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull((await context.Usuario.SingleAsync(u => u.Id == objetivo.Id)).EliminadoEn);
    }

    [Fact]
    public async Task NoEliminaCuentaActivaNiInvitacionPendiente()
    {
        await using var context = await ContextoAsync();
        var activa = await UsuarioAsync(context, AppRoles.Rrhh, 1, "activa");
        var invitada = await UsuarioAsync(context, AppRoles.Rrhh, 1, "invitada");
        invitada.Activo = false;
        invitada.InvitadoEn = DateTime.UtcNow;
        var desactivada = await UsuarioAsync(context, AppRoles.Rrhh, 1, "desactivada");
        desactivada.Activo = false;
        desactivada.InvitadoEn = DateTime.UtcNow.AddDays(-2);
        desactivada.InvitacionAceptadaEn = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        var controller = Controlador(context, 9, AppRoles.SuperAdmin);
        var activaResultado = Assert.IsType<ConflictObjectResult>(await controller.Eliminar(activa.Id));
        Assert.Contains("cuenta_activa", JsonSerializer.Serialize(activaResultado.Value), StringComparison.Ordinal);
        Assert.Null((await context.Usuario.SingleAsync(u => u.Id == activa.Id)).EliminadoEn);

        var invitacion = Assert.IsType<ConflictObjectResult>(await controller.Eliminar(invitada.Id));
        Assert.Contains("invitacion_pendiente", JsonSerializer.Serialize(invitacion.Value), StringComparison.Ordinal);
        Assert.Null((await context.Usuario.SingleAsync(u => u.Id == invitada.Id)).EliminadoEn);

        var listed = Assert.IsType<OkObjectResult>(await controller.GetUsuarios(null, null, null, null, null));
        var usuarios = Assert.IsAssignableFrom<IEnumerable<UsuarioListItemDto>>(listed.Value).ToList();
        Assert.True(Assert.Single(usuarios, u => u.Id == invitada.Id).InvitacionPendiente);
        Assert.False(Assert.Single(usuarios, u => u.Id == desactivada.Id).InvitacionPendiente);
        Assert.False(Assert.Single(usuarios, u => u.Id == activa.Id).InvitacionPendiente);
        Assert.DoesNotContain("EliminadoEn", JsonSerializer.Serialize(usuarios), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelarInvitacionPendienteLaSacaDelListadoEInvalidaElEnlace()
    {
        await using var context = await ContextoAsync();
        var operador = await UsuarioAsync(context, AppRoles.SuperAdmin, 1, "operador");
        var invitada = await UsuarioAsync(context, AppRoles.Rrhh, 1, "invitada-mfa");
        invitada.Activo = false;
        invitada.InvitadoEn = DateTime.UtcNow.AddHours(-1);
        invitada.MfaObligatorio = true;
        invitada.RequiereCambioPassword = true;
        var token = TokenOpaco.Crear();
        context.InvitacionUsuario.Add(new InvitacionUsuario
        {
            UsuarioId = invitada.Id,
            TokenHash = TokenOpaco.Hash(token),
            ExpiraEn = DateTime.UtcNow.AddHours(12),
            CreadaEn = DateTime.UtcNow,
            CreadaPorUsuarioId = operador.Id
        });
        context.AuditoriaSeguridad.Add(new AuditoriaSeguridad
        {
            FechaUtc = DateTime.UtcNow,
            UsuarioId = invitada.Id,
            Evento = "invitacion_creada",
            Resultado = "aceptado"
        });
        var aceptada = await UsuarioAsync(context, AppRoles.Rrhh, 1, "ya-aceptada");
        aceptada.Activo = false;
        aceptada.InvitadoEn = DateTime.UtcNow.AddDays(-2);
        aceptada.InvitacionAceptadaEn = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        var controller = Controlador(context, operador.Id, AppRoles.SuperAdmin);
        var respuesta = Assert.IsType<OkObjectResult>(await controller.CancelarInvitacion(invitada.Id));
        Assert.Contains("cancelada", JsonSerializer.Serialize(respuesta.Value), StringComparison.OrdinalIgnoreCase);

        var guardada = await context.Usuario.SingleAsync(u => u.Id == invitada.Id);
        Assert.False(guardada.Activo);
        Assert.NotNull(guardada.EliminadoEn);
        Assert.True(Assert.Single(context.InvitacionUsuario).ExpiraEn <= DateTime.UtcNow);
        Assert.Equal(1, await context.AuditoriaSeguridad.CountAsync(a => a.Evento == "invitacion_creada"));

        var listed = Assert.IsType<OkObjectResult>(await controller.GetUsuarios(null, null, null, null, null));
        var usuarios = Assert.IsAssignableFrom<IEnumerable<UsuarioListItemDto>>(listed.Value);
        Assert.DoesNotContain(usuarios, u => u.Id == invitada.Id);

        var aceptacion = await new InvitacionUsuarioService(
                context,
                new PasswordHasher<Usuario>(),
                new CorreoNulo(),
                new AuditoriaNula(),
                Configuracion(),
                NullLogger<InvitacionUsuarioService>.Instance)
            .AceptarAsync(token, "ClaveLocal1!");
        Assert.Equal(EstadoAceptacion.Rechazada, aceptacion.Estado);
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == invitada.Id)).Activo);
        Assert.Null((await context.Usuario.SingleAsync(u => u.Id == invitada.Id)).InvitacionAceptadaEn);

        var repetida = Assert.IsType<NotFoundObjectResult>(await controller.CancelarInvitacion(invitada.Id));
        Assert.Contains("no encontrado", JsonSerializer.Serialize(repetida.Value), StringComparison.OrdinalIgnoreCase);
        var noPendiente = Assert.IsType<ConflictObjectResult>(await controller.CancelarInvitacion(aceptada.Id));
        Assert.Contains("no_pendiente", JsonSerializer.Serialize(noPendiente.Value), StringComparison.Ordinal);

        var correo = invitada.Correo;
        var reinvito = await new InvitacionUsuarioService(
                context,
                new PasswordHasher<Usuario>(),
                new CorreoNulo(),
                new AuditoriaNula(),
                Configuracion(),
                NullLogger<InvitacionUsuarioService>.Instance)
            .InvitarAsync(1, "invitada-mfa", correo, AppRoles.Rrhh, operador.Id);
        Assert.Equal(EstadoInvitacion.Enviada, reinvito.Estado);
        Assert.NotNull(invitada.EliminadoEn);
        Assert.NotEqual(correo, invitada.Correo);
        var nueva = await context.Usuario.SingleAsync(u => u.Correo == correo);
        Assert.NotEqual(invitada.Id, nueva.Id);
        Assert.Null(nueva.EliminadoEn);
        Assert.False(nueva.Activo);
    }

    [Fact]
    public async Task EliminaCuentaDesactivadaAunqueMfaEstePendiente()
    {
        await using var context = await ContextoAsync();
        var objetivo = await UsuarioAsync(context, AppRoles.Rrhh, 1, "mfa-pendiente");
        objetivo.Activo = false;
        objetivo.MfaObligatorio = true;
        objetivo.TotpHabilitado = false;
        await context.SaveChangesAsync();

        var respuesta = Assert.IsType<OkObjectResult>(
            await Controlador(context, 9, AppRoles.SuperAdmin).Eliminar(objetivo.Id));
        Assert.Contains("Cuenta eliminada", JsonSerializer.Serialize(respuesta.Value), StringComparison.Ordinal);
        Assert.NotNull((await context.Usuario.SingleAsync(u => u.Id == objetivo.Id)).EliminadoEn);
    }

    [Fact]
    public async Task RespetaEmpresaYNoTocaAlSuperAdmin()
    {
        await using var context = await ContextoAsync();
        var super = await UsuarioAsync(context, AppRoles.SuperAdmin, 1, "super");
        super.Activo = false;
        var ajena = await UsuarioAsync(context, AppRoles.Rrhh, 2, "ajena");
        ajena.Activo = false;
        var propia = await UsuarioAsync(context, AppRoles.Rrhh, 1, "propia");
        propia.Activo = false;
        var admin = await UsuarioAsync(context, AppRoles.Admin, 1, "admin");
        await context.SaveChangesAsync();

        var controller = Controlador(context, admin.Id, AppRoles.Admin, 1);
        Assert.IsType<NotFoundObjectResult>(await controller.Eliminar(super.Id));
        Assert.IsType<NotFoundObjectResult>(await controller.Eliminar(ajena.Id));
        Assert.IsType<NotFoundObjectResult>(await controller.Eliminar(admin.Id));
        Assert.IsType<OkObjectResult>(await controller.Eliminar(propia.Id));
        Assert.Null((await context.Usuario.SingleAsync(u => u.Id == super.Id)).EliminadoEn);
        Assert.Null((await context.Usuario.SingleAsync(u => u.Id == ajena.Id)).EliminadoEn);
        Assert.NotNull((await context.Usuario.SingleAsync(u => u.Id == propia.Id)).EliminadoEn);
    }

    private static IConfiguration Configuracion()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
                ["Jwt:Issuer"] = "ControlFichajes.Tests",
                ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
                ["App:PublicBaseUrl"] = "http://127.0.0.1:5173"
            })
            .Build();
    }

    private static UsuariosController Controlador(AppDbContext context, int operadorId, string rol, int? empresaId = null)
    {
        var configuration = Configuracion();
        var servicio = new AuthService(context, new PasswordHasher<Usuario>(), new JwtTokenService(configuration), configuration: configuration);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, operadorId.ToString()),
            new(ClaimTypes.Role, rol),
            new("token_use", "web")
        };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresa_id", empresaId.Value.ToString()));

        return new UsuariosController(servicio, configuration: configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role))
                }
            }
        };
    }

    private static async Task<AppDbContext> ContextoAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
        context.Empresa.AddRange(
            new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno", CUIT = "30-11111111-1" },
            new Empresa { Id = 2, NombreFantasia = "Dos", RazonSocial = "Dos", CUIT = "30-22222222-2" });
        await context.SaveChangesAsync();
        return context;
    }

    private sealed class CorreoNulo : ICorreoSaliente
    {
        public Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AuditoriaNula : IAuditoriaSeguridad
    {
        public Task RegistrarAsync(
            string evento,
            string resultado,
            int? usuarioId,
            string? correo,
            string? detalle,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<Usuario> UsuarioAsync(AppDbContext context, string rol, int empresaId, string slug)
    {
        var usuario = new Usuario
        {
            EmpresaId = empresaId,
            NombreUsuario = slug,
            Correo = $"{slug}@example.test",
            PasswordHash = "hash",
            Rol = rol,
            Activo = true,
            TokenVersion = 0
        };
        context.Usuario.Add(usuario);
        await context.SaveChangesAsync();
        return usuario;
    }
}
