using System.Security.Claims;
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
using Microsoft.Extensions.Configuration;

namespace ControlFichajes.API.Tests;

public class ExigirMfaUsuarioTests
{
    [Fact]
    public async Task ElListadoInformaLosTresEstadosSinSecretos()
    {
        await using var context = await ContextoAsync();
        var con = await UsuarioAsync(context, AppRoles.Rrhh, 1, "con");
        var sin = await UsuarioAsync(context, AppRoles.Rrhh, 1, "sin");
        var pendiente = await UsuarioAsync(context, AppRoles.Rrhh, 1, "pendiente");
        con.TotpHabilitado = true;
        con.TotpSecretoCifrado = "secreto-no-visible";
        pendiente.MfaObligatorio = true;
        await context.SaveChangesAsync();

        var listed = Assert.IsType<OkObjectResult>(await Controlador(context, true, 1, AppRoles.SuperAdmin).GetUsuarios(null, null, null, null, null));
        var usuarios = Assert.IsAssignableFrom<IEnumerable<UsuarioListItemDto>>(listed.Value);
        Assert.True(Assert.Single(usuarios, u => u.Id == con.Id).TotpHabilitado);
        Assert.False(Assert.Single(usuarios, u => u.Id == sin.Id).TotpHabilitado);
        Assert.False(Assert.Single(usuarios, u => u.Id == sin.Id).MfaObligatorio);
        Assert.True(Assert.Single(usuarios, u => u.Id == pendiente.Id).MfaObligatorio);
        Assert.False(Assert.Single(usuarios, u => u.Id == pendiente.Id).TotpHabilitado);
        var json = JsonSerializer.Serialize(usuarios);
        Assert.DoesNotContain("secreto-no-visible", json, StringComparison.Ordinal);
        Assert.DoesNotContain("TotpSecreto", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExigirRevocaLaSesionYQuedaPendiente()
    {
        await using var context = await ContextoAsync();
        var objetivo = await UsuarioAsync(context, AppRoles.Admin, 1, "objetivo");
        context.RefreshToken.Add(new RefreshToken
        {
            UsuarioId = objetivo.Id,
            TokenHash = "hash-sesion",
            FamiliaId = Guid.NewGuid(),
            ExpiraEn = DateTime.UtcNow.AddDays(1),
            CreadaEn = DateTime.UtcNow,
            TokenVersion = 0
        });
        await context.SaveChangesAsync();

        var respuesta = Assert.IsType<OkObjectResult>(await Controlador(context, true, 9, AppRoles.SuperAdmin).ExigirMfa(objetivo.Id));
        var cuerpo = Assert.IsType<UsuarioActualizadoResponseDto>(respuesta.Value);
        Assert.True(cuerpo.Usuario.MfaObligatorio);
        Assert.False(cuerpo.Usuario.TotpHabilitado);
        var guardado = await context.Usuario.SingleAsync(u => u.Id == objetivo.Id);
        Assert.Equal(1, guardado.TokenVersion);
        Assert.NotNull(Assert.Single(context.RefreshToken).RevocadaEn);
        var json = JsonSerializer.Serialize(cuerpo);
        Assert.DoesNotContain("hash-sesion", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoExigeSiLaCuentaEstaInactivaOElEnrolamientoEstaApagado()
    {
        await using var context = await ContextoAsync();
        var inactiva = await UsuarioAsync(context, AppRoles.Rrhh, 1, "inactiva");
        inactiva.Activo = false;
        var libre = await UsuarioAsync(context, AppRoles.Rrhh, 1, "libre");
        await context.SaveChangesAsync();

        var inactivo = Assert.IsType<ConflictObjectResult>(await Controlador(context, true, 9, AppRoles.SuperAdmin).ExigirMfa(inactiva.Id));
        Assert.Contains("activa", JsonSerializer.Serialize(inactivo.Value), StringComparison.OrdinalIgnoreCase);
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == inactiva.Id)).MfaObligatorio);

        var apagado = Assert.IsType<ConflictObjectResult>(await Controlador(context, false, 9, AppRoles.SuperAdmin).ExigirMfa(libre.Id));
        var json = JsonSerializer.Serialize(apagado.Value);
        Assert.Contains("mfa_enrolamiento_deshabilitado", json, StringComparison.Ordinal);
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == libre.Id)).MfaObligatorio);
    }

    [Fact]
    public async Task RespetaEmpresaYNoTocaAlSuperAdmin()
    {
        await using var context = await ContextoAsync();
        var super = await UsuarioAsync(context, AppRoles.SuperAdmin, 1, "super");
        var ajeno = await UsuarioAsync(context, AppRoles.Rrhh, 2, "ajeno");
        var propio = await UsuarioAsync(context, AppRoles.Rrhh, 1, "propio");
        var admin = await UsuarioAsync(context, AppRoles.Admin, 1, "admin");
        await context.SaveChangesAsync();

        var adminController = Controlador(context, true, admin.Id, AppRoles.Admin, 1);
        Assert.IsType<NotFoundObjectResult>(await adminController.ExigirMfa(super.Id));
        Assert.IsType<NotFoundObjectResult>(await adminController.ExigirMfa(ajeno.Id));
        Assert.IsType<NotFoundObjectResult>(await adminController.ExigirMfa(admin.Id));
        Assert.IsType<OkObjectResult>(await adminController.ExigirMfa(propio.Id));
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == super.Id)).MfaObligatorio);
        Assert.False((await context.Usuario.SingleAsync(u => u.Id == ajeno.Id)).MfaObligatorio);
        Assert.True((await context.Usuario.SingleAsync(u => u.Id == propio.Id)).MfaObligatorio);
    }

    [Fact]
    public async Task ExigirConAutenticadorYaConfiguradoNoDependeDelEnrolamiento()
    {
        await using var context = await ContextoAsync();
        var objetivo = await UsuarioAsync(context, AppRoles.Rrhh, 1, "con-autenticador");
        objetivo.TotpHabilitado = true;
        await context.SaveChangesAsync();

        var respuesta = Assert.IsType<OkObjectResult>(await Controlador(context, false, 9, AppRoles.SuperAdmin).ExigirMfa(objetivo.Id));
        var cuerpo = Assert.IsType<UsuarioActualizadoResponseDto>(respuesta.Value);
        Assert.True(cuerpo.Usuario.TotpHabilitado);
        Assert.True(cuerpo.Usuario.MfaObligatorio);
        var guardado = await context.Usuario.SingleAsync(u => u.Id == objetivo.Id);
        Assert.Equal(1, guardado.TokenVersion);
    }

    private static UsuariosController Controlador(AppDbContext context, bool enrolamiento, int operadorId, string rol, int? empresaId = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave-super-secreta-de-pruebas-1234567890",
                ["Jwt:Issuer"] = "ControlFichajes.Tests",
                ["Jwt:Audience"] = "ControlFichajes.Frontend.Tests",
                ["Auth:PermitirEnrolamientoMfa"] = enrolamiento ? "true" : "false"
            })
            .Build();
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
