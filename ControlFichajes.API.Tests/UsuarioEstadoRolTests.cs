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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ControlFichajes.API.Tests;

public class UsuarioEstadoRolTests
{
    [Fact]
    public async Task SuperAdmin_DesactivaAdmin()
    {
        var fx = await CreateFixtureAsync();
        var result = await fx.SuperAdminController.CambiarEstado(fx.AdminA.Id, new CambiarEstadoUsuarioDto { Activo = false });
        var body = AssertOk(result);
        Assert.False(body.Usuario.Activo);
        Assert.Equal(1, (await ReloadAsync(fx.Context, fx.AdminA.Id)).TokenVersion);
    }

    [Fact]
    public async Task SuperAdmin_DesactivaRrhh()
    {
        var fx = await CreateFixtureAsync();
        var result = await fx.SuperAdminController.CambiarEstado(fx.RrhhA.Id, new CambiarEstadoUsuarioDto { Activo = false });
        Assert.False(AssertOk(result).Usuario.Activo);
    }

    [Fact]
    public async Task SuperAdmin_ReactivaAdminYRrhh()
    {
        var fx = await CreateFixtureAsync();
        fx.AdminA.Activo = false;
        fx.RrhhA.Activo = false;
        await fx.Context.SaveChangesAsync();

        Assert.True(AssertOk(await fx.SuperAdminController.CambiarEstado(fx.AdminA.Id, new CambiarEstadoUsuarioDto { Activo = true })).Usuario.Activo);
        Assert.True(AssertOk(await fx.SuperAdminController.CambiarEstado(fx.RrhhA.Id, new CambiarEstadoUsuarioDto { Activo = true })).Usuario.Activo);
    }

    [Fact]
    public async Task SuperAdmin_CambiaAdminARrhh()
    {
        var fx = await CreateFixtureAsync();
        var body = AssertOk(await fx.SuperAdminController.CambiarRol(fx.AdminA.Id, new CambiarRolUsuarioDto { Rol = AppRoles.Rrhh }));
        Assert.Equal(AppRoles.Rrhh, body.Usuario.Rol);
        Assert.Equal(1, (await ReloadAsync(fx.Context, fx.AdminA.Id)).TokenVersion);
    }

    [Fact]
    public async Task SuperAdmin_CambiaRrhhAAdmin()
    {
        var fx = await CreateFixtureAsync();
        var body = AssertOk(await fx.SuperAdminController.CambiarRol(fx.RrhhA.Id, new CambiarRolUsuarioDto { Rol = AppRoles.Admin }));
        Assert.Equal(AppRoles.Admin, body.Usuario.Rol);
    }

    [Fact]
    public async Task SuperAdmin_NoOperaSobreSuperAdmin()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<NotFoundObjectResult>(
            await fx.SuperAdminController.CambiarEstado(fx.OtroSuperAdmin.Id, new CambiarEstadoUsuarioDto { Activo = false }));
        Assert.IsType<NotFoundObjectResult>(
            await fx.SuperAdminController.CambiarRol(fx.OtroSuperAdmin.Id, new CambiarRolUsuarioDto { Rol = AppRoles.Admin }));
    }

    [Fact]
    public async Task SuperAdmin_NoSeModificaASiMismo()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<NotFoundObjectResult>(
            await fx.SuperAdminController.CambiarEstado(fx.SuperAdmin.Id, new CambiarEstadoUsuarioDto { Activo = false }));
        Assert.IsType<NotFoundObjectResult>(
            await fx.SuperAdminController.CambiarRol(fx.SuperAdmin.Id, new CambiarRolUsuarioDto { Rol = AppRoles.Admin }));
    }

    [Fact]
    public async Task Admin_DesactivaYReactivaRrhhPropio()
    {
        var fx = await CreateFixtureAsync();
        Assert.False(AssertOk(await fx.AdminAController.CambiarEstado(fx.RrhhA.Id, new CambiarEstadoUsuarioDto { Activo = false })).Usuario.Activo);
        Assert.True(AssertOk(await fx.AdminAController.CambiarEstado(fx.RrhhA.Id, new CambiarEstadoUsuarioDto { Activo = true })).Usuario.Activo);
    }

    [Fact]
    public async Task Admin_NoOperaSobreAdmin()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<NotFoundObjectResult>(
            await fx.AdminAController.CambiarEstado(fx.AdminB.Id, new CambiarEstadoUsuarioDto { Activo = false }));
        Assert.IsType<NotFoundObjectResult>(
            await fx.AdminAController.CambiarEstado(fx.AdminA.Id, new CambiarEstadoUsuarioDto { Activo = false }));
    }

    [Fact]
    public async Task Admin_NoOperaSobreOtraEmpresa()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<NotFoundObjectResult>(
            await fx.AdminAController.CambiarEstado(fx.RrhhB.Id, new CambiarEstadoUsuarioDto { Activo = false }));
    }

    [Fact]
    public async Task Admin_NoCambiaRoles()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<ForbidResult>(
            await fx.AdminAController.CambiarRol(fx.RrhhA.Id, new CambiarRolUsuarioDto { Rol = AppRoles.Admin }));
        Assert.Equal(AppRoles.Rrhh, (await ReloadAsync(fx.Context, fx.RrhhA.Id)).Rol);
        Assert.Equal(0, (await ReloadAsync(fx.Context, fx.RrhhA.Id)).TokenVersion);
    }

    [Fact]
    public async Task Rrhh_NoOpera()
    {
        var fx = await CreateFixtureAsync();
        var controller = CrearController(fx.Context, Principal(fx.RrhhA.Id, AppRoles.Rrhh, 1));
        Assert.IsType<NotFoundObjectResult>(
            await controller.CambiarEstado(fx.RrhhB.Id, new CambiarEstadoUsuarioDto { Activo = false }));
        Assert.IsType<NotFoundObjectResult>(
            await controller.CambiarRol(fx.AdminA.Id, new CambiarRolUsuarioDto { Rol = AppRoles.Rrhh }));
    }

    [Fact]
    public async Task RolDesconocidoYSuperAdminDestino_Rechazados()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<BadRequestObjectResult>(
            await fx.SuperAdminController.CambiarRol(fx.RrhhA.Id, new CambiarRolUsuarioDto { Rol = "AUDITOR" }));
        Assert.IsType<BadRequestObjectResult>(
            await fx.SuperAdminController.CambiarRol(fx.RrhhA.Id, new CambiarRolUsuarioDto { Rol = AppRoles.SuperAdmin }));
        Assert.Equal(AppRoles.Rrhh, (await ReloadAsync(fx.Context, fx.RrhhA.Id)).Rol);
    }

    [Fact]
    public async Task CambioIdentico_EsIdempotenteYNoIncrementaTokenVersion()
    {
        var fx = await CreateFixtureAsync();
        AssertOk(await fx.SuperAdminController.CambiarRol(fx.AdminA.Id, new CambiarRolUsuarioDto { Rol = AppRoles.Admin }));
        AssertOk(await fx.SuperAdminController.CambiarEstado(fx.AdminA.Id, new CambiarEstadoUsuarioDto { Activo = true }));
        var usuario = await ReloadAsync(fx.Context, fx.AdminA.Id);
        Assert.Equal(0, usuario.TokenVersion);
        Assert.Equal(AppRoles.Admin, usuario.Rol);
        Assert.True(usuario.Activo);
    }

    [Fact]
    public async Task CambioEfectivo_IncrementaTokenVersionUnaSolaVez()
    {
        var fx = await CreateFixtureAsync();
        AssertOk(await fx.SuperAdminController.CambiarEstado(fx.RrhhA.Id, new CambiarEstadoUsuarioDto { Activo = false }));
        Assert.Equal(1, (await ReloadAsync(fx.Context, fx.RrhhA.Id)).TokenVersion);
        AssertOk(await fx.SuperAdminController.CambiarRol(fx.AdminA.Id, new CambiarRolUsuarioDto { Rol = AppRoles.Rrhh }));
        Assert.Equal(1, (await ReloadAsync(fx.Context, fx.AdminA.Id)).TokenVersion);
    }

    [Fact]
    public async Task Desactivar_NoModificaPasswordBloqueoNiCambioRequerido()
    {
        var fx = await CreateFixtureAsync();
        fx.RrhhA.PasswordHash = "hash-original";
        fx.RrhhA.RequiereCambioPassword = true;
        fx.RrhhA.IntentosFallidos = 4;
        fx.RrhhA.BloqueadoHasta = DateTime.UtcNow.AddMinutes(10);
        await fx.Context.SaveChangesAsync();

        AssertOk(await fx.SuperAdminController.CambiarEstado(fx.RrhhA.Id, new CambiarEstadoUsuarioDto { Activo = false }));
        var usuario = await ReloadAsync(fx.Context, fx.RrhhA.Id);
        Assert.False(usuario.Activo);
        Assert.Equal("hash-original", usuario.PasswordHash);
        Assert.True(usuario.RequiereCambioPassword);
        Assert.Equal(4, usuario.IntentosFallidos);
        Assert.NotNull(usuario.BloqueadoHasta);
    }

    [Fact]
    public async Task Reactivar_NoReviveJwtAnterior()
    {
        var fx = await CreateFixtureAsync();
        fx.RrhhA.Activo = false;
        fx.RrhhA.TokenVersion = 3;
        await fx.Context.SaveChangesAsync();

        AssertOk(await fx.SuperAdminController.CambiarEstado(fx.RrhhA.Id, new CambiarEstadoUsuarioDto { Activo = true }));
        var usuario = await ReloadAsync(fx.Context, fx.RrhhA.Id);
        Assert.True(usuario.Activo);
        Assert.Equal(4, usuario.TokenVersion);

        var httpContext = new DefaultHttpContext
        {
            User = WebPrincipal(usuario.Id, 3),
            RequestServices = new ServiceCollection().AddOptions().BuildServiceProvider()
        };
        httpContext.Request.Path = "/api/usuarios";
        httpContext.Request.Method = "GET";
        httpContext.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new WebSessionSecurityMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(httpContext, fx.Context);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task UsuarioInactivo_ContinuaEnGetYDtoSinSecretos()
    {
        var fx = await CreateFixtureAsync();
        fx.RrhhA.Activo = false;
        fx.RrhhA.PasswordHash = "no-debe-salir";
        await fx.Context.SaveChangesAsync();

        var listed = Assert.IsType<OkObjectResult>(await fx.SuperAdminController.GetUsuarios(null, null, null, null, null));
        var usuarios = Assert.IsAssignableFrom<IEnumerable<UsuarioListItemDto>>(listed.Value);
        var item = Assert.Single(usuarios, u => u.Id == fx.RrhhA.Id);
        Assert.False(item.Activo);

        var json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no-debe-salir", json);
        Assert.DoesNotContain("Token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordTemporal", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_NoPuedeCrearAdminManipulandoElBody()
    {
        var fx = await CreateFixtureAsync();
        var request = new UsuarioRegistroDto
        {
            EmpresaId = 99,
            NombreUsuario = "falso.admin",
            Email = "falso.admin@example.test",
            Password = "Password123!",
            Rol = AppRoles.Admin
        };
        var result = await fx.AdminAController.Crear(request);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(await fx.Context.Usuario.AnyAsync(u => u.Correo == request.Email));
    }

    [Fact]
    public async Task Admin_CreaRrhhPropioIgnorandoEmpresaDelBody()
    {
        var fx = await CreateFixtureAsync();
        var request = new UsuarioRegistroDto
        {
            EmpresaId = 2,
            NombreUsuario = "nuevo.rrhh",
            Email = "nuevo.rrhh@example.test",
            Password = "Password123!",
            Rol = AppRoles.Rrhh
        };
        var result = await fx.AdminAController.Crear(request);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(result).StatusCode);
        var creado = await fx.Context.Usuario.SingleAsync(u => u.Correo == request.Email);
        Assert.Equal(1, creado.EmpresaId);
        Assert.Equal(AppRoles.Rrhh, creado.Rol);
    }

    [Fact]
    public async Task SuperAdmin_CreaAdminYRrhh()
    {
        var fx = await CreateFixtureAsync();
        var adminRequest = new UsuarioRegistroDto
        {
            EmpresaId = 2,
            NombreUsuario = "nuevo.admin",
            Email = "nuevo.admin@example.test",
            Password = "Password123!",
            Rol = AppRoles.Admin
        };
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(await fx.SuperAdminController.Crear(adminRequest)).StatusCode);
        var rrhhRequest = new UsuarioRegistroDto
        {
            EmpresaId = 2,
            NombreUsuario = "nuevo.rrhh2",
            Email = "nuevo.rrhh2@example.test",
            Password = "Password123!",
            Rol = AppRoles.Rrhh
        };
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(await fx.SuperAdminController.Crear(rrhhRequest)).StatusCode);
        Assert.Equal(AppRoles.Admin, (await fx.Context.Usuario.SingleAsync(u => u.Correo == adminRequest.Email)).Rol);
        Assert.Equal(AppRoles.Rrhh, (await fx.Context.Usuario.SingleAsync(u => u.Correo == rrhhRequest.Email)).Rol);
    }

    [Fact]
    public void DeleteYCambiarPasswordAdministrativo_NoExisten()
    {
        var methods = typeof(UsuariosController).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        Assert.DoesNotContain(methods, method => method.GetCustomAttributes<HttpDeleteAttribute>(true).Any());
        Assert.DoesNotContain(
            methods.SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>()).Select(a => a.Template),
            template => template == "{id}/cambiar-password");
    }

    [Fact]
    public async Task ResetUnlockYLoginSiguenFuncionando()
    {
        var fx = await CreateFixtureAsync();
        var reset = await fx.Service.RestablecerPasswordAsync(fx.RrhhA.Id);
        Assert.NotNull(reset);
        Assert.False(string.IsNullOrWhiteSpace(reset!.PasswordTemporal));

        fx.RrhhA.BloqueadoHasta = DateTime.UtcNow.AddMinutes(5);
        fx.RrhhA.IntentosFallidos = 5;
        await fx.Context.SaveChangesAsync();
        Assert.True(await fx.Service.DesbloquearAsync(fx.RrhhA.Id));

        var login = await fx.Service.LoginAsync(new LoginRequestDto
        {
            Email = fx.RrhhA.Correo,
            Password = reset.PasswordTemporal
        });
        Assert.NotNull(login);
        Assert.True(login!.RequiereCambioPassword);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
    }

    private static UsuarioActualizadoResponseDto AssertOk(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<UsuarioActualizadoResponseDto>(ok.Value);
    }

    private static async Task<Usuario> ReloadAsync(AppDbContext context, int id)
    {
        await context.Entry(context.Usuario.Single(u => u.Id == id)).ReloadAsync();
        return await context.Usuario.SingleAsync(u => u.Id == id);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var context = CreateContext();
        var superAdmin = await AddUsuarioAsync(context, AppRoles.SuperAdmin, 1, "sa");
        var adminA = await AddUsuarioAsync(context, AppRoles.Admin, 1, "admin-a");
        var rrhhA = await AddUsuarioAsync(context, AppRoles.Rrhh, 1, "rrhh-a");
        var adminB = await AddUsuarioAsync(context, AppRoles.Admin, 2, "admin-b");
        var rrhhB = await AddUsuarioAsync(context, AppRoles.Rrhh, 2, "rrhh-b");
        var otroSa = await AddUsuarioAsync(context, AppRoles.SuperAdmin, 1, "sa-2");

        return new Fixture(
            context,
            CreateService(context),
            superAdmin,
            adminA,
            rrhhA,
            adminB,
            rrhhB,
            otroSa,
            CrearController(context, Principal(superAdmin.Id, AppRoles.SuperAdmin, empresaId: 2)),
            CrearController(context, Principal(adminA.Id, AppRoles.Admin, 1)));
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
        context.Empresa.AddRange(
            new Empresa { Id = 1, NombreFantasia = "Empresa Uno", RazonSocial = "Uno S.A.", CUIT = "30-11111111-1" },
            new Empresa { Id = 2, NombreFantasia = "Empresa Dos", RazonSocial = "Dos S.A.", CUIT = "30-22222222-2" });
        context.SaveChanges();
        return context;
    }

    private static AuthService CreateService(AppDbContext context)
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

    private static UsuariosController CrearController(AppDbContext context, ClaimsPrincipal user)
    {
        return new UsuariosController(CreateService(context))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static async Task<Usuario> AddUsuarioAsync(AppDbContext context, string rol, int empresaId, string slug)
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

    private static ClaimsPrincipal Principal(int usuarioId, string rol, int? empresaId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new(ClaimTypes.Role, rol),
            new("token_use", "web")
        };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresa_id", empresaId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role));
    }

    private static ClaimsPrincipal WebPrincipal(int usuarioId, int tokenVersion)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
                new Claim("token_use", "web"),
                new Claim("token_version", tokenVersion.ToString())
            },
            "Test",
            ClaimTypes.Name,
            ClaimTypes.Role));
    }

    private sealed record Fixture(
        AppDbContext Context,
        AuthService Service,
        Usuario SuperAdmin,
        Usuario AdminA,
        Usuario RrhhA,
        Usuario AdminB,
        Usuario RrhhB,
        Usuario OtroSuperAdmin,
        UsuariosController SuperAdminController,
        UsuariosController AdminAController);
}
