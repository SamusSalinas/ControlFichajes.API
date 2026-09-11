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

namespace ControlFichajes.API.Tests;

public class UsuarioIdentidadTests
{
    [Fact]
    public async Task SuperAdmin_EditaIdentidadDeAdmin()
    {
        var fx = await CreateFixtureAsync();
        var body = AssertOk(await fx.SuperAdminController.CambiarIdentidad(
            fx.AdminA.Id,
            Identidad("admin.norte", "admin.norte@example.test")));
        Assert.Equal("admin.norte", body.Usuario.NombreUsuario);
        Assert.Equal("admin.norte@example.test", body.Usuario.Correo);
        Assert.Equal(AppRoles.Admin, body.Usuario.Rol);
    }

    [Fact]
    public async Task SuperAdmin_EditaIdentidadDeRrhh()
    {
        var fx = await CreateFixtureAsync();
        var body = AssertOk(await fx.SuperAdminController.CambiarIdentidad(
            fx.RrhhA.Id,
            Identidad("rrhh.norte", "rrhh.norte@example.test")));
        Assert.Equal("rrhh.norte", body.Usuario.NombreUsuario);
        Assert.Equal("rrhh.norte@example.test", body.Usuario.Correo);
    }

    [Fact]
    public async Task Admin_EditaRrhhPropio()
    {
        var fx = await CreateFixtureAsync();
        var body = AssertOk(await fx.AdminAController.CambiarIdentidad(
            fx.RrhhA.Id,
            Identidad("rrhh.propio", "rrhh.propio@example.test")));
        Assert.Equal("rrhh.propio", body.Usuario.NombreUsuario);
        Assert.Equal(1, body.Usuario.EmpresaId);
    }

    [Fact]
    public async Task Admin_NoEditaAdmin()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<NotFoundObjectResult>(await fx.AdminAController.CambiarIdentidad(
            fx.AdminA.Id,
            Identidad("otro.admin", "otro.admin@example.test")));
        Assert.Equal("admin-a", (await ReloadAsync(fx.Context, fx.AdminA.Id)).NombreUsuario);
    }

    [Fact]
    public async Task Admin_NoEditaOtraEmpresa()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<NotFoundObjectResult>(await fx.AdminAController.CambiarIdentidad(
            fx.RrhhB.Id,
            Identidad("rrhh.ajeno", "rrhh.ajeno@example.test")));
        Assert.Equal("rrhh-b", (await ReloadAsync(fx.Context, fx.RrhhB.Id)).NombreUsuario);
    }

    [Fact]
    public async Task Rrhh_NoEdita()
    {
        var fx = await CreateFixtureAsync();
        var controller = CrearController(fx.Context, Principal(fx.RrhhA.Id, AppRoles.Rrhh, 1));
        Assert.IsType<NotFoundObjectResult>(await controller.CambiarIdentidad(
            fx.RrhhB.Id,
            Identidad("no.debe", "no.debe@example.test")));
    }

    [Fact]
    public async Task Self_NoEdita()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<NotFoundObjectResult>(await fx.SuperAdminController.CambiarIdentidad(
            fx.SuperAdmin.Id,
            Identidad("yo.mismo", "yo.mismo@example.test")));
        Assert.IsType<NotFoundObjectResult>(await fx.AdminAController.CambiarIdentidad(
            fx.AdminA.Id,
            Identidad("yo.admin", "yo.admin@example.test")));
    }

    [Fact]
    public async Task SuperAdminObjetivo_NoSeEdita()
    {
        var fx = await CreateFixtureAsync();
        Assert.IsType<NotFoundObjectResult>(await fx.SuperAdminController.CambiarIdentidad(
            fx.OtroSuperAdmin.Id,
            Identidad("otro.sa", "otro.sa@example.test")));
    }

    [Fact]
    public void NombreValidoYVariantesInvalidas()
    {
        foreach (var value in new[] { "martin", "martin.eloy", "martin_eloy", "martin-eloy", "martin2026", "rrhh.central" })
            Assert.Equal(string.Empty, UsuarioIdentidadRules.ValidateNombre(value));

        Assert.Equal(UsuarioIdentidadRules.NombreCaracteres, UsuarioIdentidadRules.ValidateNombre("martin!!!"));
        Assert.Equal(UsuarioIdentidadRules.NombreEspacios, UsuarioIdentidadRules.ValidateNombre("martin eloy"));
        Assert.Equal(UsuarioIdentidadRules.NombreEspacios, UsuarioIdentidadRules.ValidateNombre(" martin.eloy"));
        Assert.Equal(UsuarioIdentidadRules.NombreEspacios, UsuarioIdentidadRules.ValidateNombre("martin.eloy "));
        Assert.Equal(UsuarioIdentidadRules.NombreSeparadores, UsuarioIdentidadRules.ValidateNombre("martin..eloy"));
        Assert.Equal(UsuarioIdentidadRules.NombreSeparadores, UsuarioIdentidadRules.ValidateNombre("martin__eloy"));
        Assert.Equal(UsuarioIdentidadRules.NombreSeparadores, UsuarioIdentidadRules.ValidateNombre("martin--eloy"));
        Assert.Equal(UsuarioIdentidadRules.NombreLongitud, UsuarioIdentidadRules.ValidateNombre("ma"));
        Assert.Equal(UsuarioIdentidadRules.NombreLongitud, UsuarioIdentidadRules.ValidateNombre(new string('a', 51)));
        Assert.Equal(UsuarioIdentidadRules.NombreLetraInicial, UsuarioIdentidadRules.ValidateNombre(".martin"));
        Assert.Equal(UsuarioIdentidadRules.NombreTerminacion, UsuarioIdentidadRules.ValidateNombre("martin."));
        Assert.Equal(UsuarioIdentidadRules.NombreTerminacion, UsuarioIdentidadRules.ValidateNombre("martin-"));
        Assert.Equal(UsuarioIdentidadRules.NombreCaracteres, UsuarioIdentidadRules.ValidateNombre("martin@empresa"));
        Assert.Equal(UsuarioIdentidadRules.NombreVacio, UsuarioIdentidadRules.ValidateNombre(""));
    }

    [Fact]
    public async Task NombreInvalido_NoEscribe()
    {
        var fx = await CreateFixtureAsync();
        var result = await fx.SuperAdminController.CambiarIdentidad(fx.RrhhA.Id, Identidad("martin!!!", fx.RrhhA.Correo));
        var bad = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, bad.StatusCode);
        Assert.Equal("rrhh-a", (await ReloadAsync(fx.Context, fx.RrhhA.Id)).NombreUsuario);
        Assert.Equal(0, (await ReloadAsync(fx.Context, fx.RrhhA.Id)).TokenVersion);
    }

    [Fact]
    public void CorreoValidoEInvalido()
    {
        Assert.Equal(string.Empty, UsuarioIdentidadRules.ValidateCorreo("martin@example.test"));
        Assert.Equal(UsuarioIdentidadRules.CorreoFormato, UsuarioIdentidadRules.ValidateCorreo("martin"));
        Assert.Equal(UsuarioIdentidadRules.CorreoVacio, UsuarioIdentidadRules.ValidateCorreo(""));
        Assert.Equal(UsuarioIdentidadRules.CorreoLongitud, UsuarioIdentidadRules.ValidateCorreo($"{new string('a', 90)}@example.test"));
    }

    [Fact]
    public async Task CorreoInvalido_NoEscribe()
    {
        var fx = await CreateFixtureAsync();
        var result = await fx.SuperAdminController.CambiarIdentidad(fx.RrhhA.Id, Identidad("rrhh-a", "no-es-correo"));
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("rrhh-a@example.test", (await ReloadAsync(fx.Context, fx.RrhhA.Id)).Correo);
    }

    [Fact]
    public async Task CorreoDuplicado_409SinModificar()
    {
        var fx = await CreateFixtureAsync();
        var result = await fx.SuperAdminController.CambiarIdentidad(
            fx.RrhhA.Id,
            Identidad("rrhh-a", fx.AdminA.Correo));
        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("rrhh-a@example.test", (await ReloadAsync(fx.Context, fx.RrhhA.Id)).Correo);
        Assert.Equal(0, (await ReloadAsync(fx.Context, fx.RrhhA.Id)).TokenVersion);
    }

    [Fact]
    public async Task MismoCorreoDelUsuario_EsValido()
    {
        var fx = await CreateFixtureAsync();
        var body = AssertOk(await fx.SuperAdminController.CambiarIdentidad(
            fx.RrhhA.Id,
            Identidad("rrhh.renombrado", fx.RrhhA.Correo)));
        Assert.Equal("rrhh.renombrado", body.Usuario.NombreUsuario);
        Assert.Equal(fx.RrhhA.Correo, body.Usuario.Correo);
        Assert.Equal(1, (await ReloadAsync(fx.Context, fx.RrhhA.Id)).TokenVersion);
    }

    [Fact]
    public void DtoIdentidad_NoIncluyeEmpresaRolNiEstado()
    {
        var names = typeof(CambiarIdentidadUsuarioDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Correo", "NombreUsuario" }, names);
    }

    [Fact]
    public async Task CambioEfectivo_IncrementaTokenVersionUnaVezYNoTocaElResto()
    {
        var fx = await CreateFixtureAsync();
        fx.RrhhA.PasswordHash = "hash-original";
        fx.RrhhA.RequiereCambioPassword = true;
        fx.RrhhA.IntentosFallidos = 3;
        fx.RrhhA.BloqueadoHasta = DateTime.UtcNow.AddMinutes(10);
        fx.RrhhA.Activo = true;
        await fx.Context.SaveChangesAsync();

        AssertOk(await fx.SuperAdminController.CambiarIdentidad(
            fx.RrhhA.Id,
            Identidad("rrhh.nuevo", "rrhh.nuevo@example.test")));
        var usuario = await ReloadAsync(fx.Context, fx.RrhhA.Id);
        Assert.Equal(1, usuario.TokenVersion);
        Assert.Equal(1, usuario.EmpresaId);
        Assert.Equal(AppRoles.Rrhh, usuario.Rol);
        Assert.True(usuario.Activo);
        Assert.Equal("hash-original", usuario.PasswordHash);
        Assert.True(usuario.RequiereCambioPassword);
        Assert.Equal(3, usuario.IntentosFallidos);
        Assert.NotNull(usuario.BloqueadoHasta);
    }

    [Fact]
    public async Task NoOp_NoEscribeNiIncrementa()
    {
        var fx = await CreateFixtureAsync();
        AssertOk(await fx.SuperAdminController.CambiarIdentidad(
            fx.RrhhA.Id,
            Identidad(fx.RrhhA.NombreUsuario, fx.RrhhA.Correo)));
        var usuario = await ReloadAsync(fx.Context, fx.RrhhA.Id);
        Assert.Equal(0, usuario.TokenVersion);
        Assert.Equal("rrhh-a", usuario.NombreUsuario);
    }

    [Fact]
    public async Task DtoNoDevuelveSecretos()
    {
        var fx = await CreateFixtureAsync();
        fx.RrhhA.PasswordHash = "secreto-hash";
        await fx.Context.SaveChangesAsync();
        var body = AssertOk(await fx.SuperAdminController.CambiarIdentidad(
            fx.RrhhA.Id,
            Identidad("rrhh.ok", "rrhh.ok@example.test")));
        var json = JsonSerializer.Serialize(body);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secreto-hash", json);
        Assert.DoesNotContain("TokenVersion", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteYCambioDirectoDePassword_NoExisten()
    {
        var methods = typeof(UsuariosController).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        Assert.DoesNotContain(methods, method => method.GetCustomAttributes<HttpDeleteAttribute>(true).Any());
        Assert.DoesNotContain(
            methods.SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>()).Select(a => a.Template),
            template => template == "{id}/cambiar-password");
        Assert.Contains(
            methods.SelectMany(method => method.GetCustomAttributes<HttpPatchAttribute>()).Select(a => a.Template),
            template => template == "{id}/identidad");
    }

    [Fact]
    public async Task AltaEstadoRolResetUnlockYLogin_SiguenFuncionando()
    {
        var fx = await CreateFixtureAsync();
        var alta = await fx.AdminAController.Crear(new UsuarioRegistroDto
        {
            EmpresaId = 99,
            NombreUsuario = "rrhh.alta",
            Email = "rrhh.alta@example.test",
            Password = "Password123!",
            Rol = AppRoles.Rrhh
        });
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(alta).StatusCode);

        AssertOk(await fx.SuperAdminController.CambiarEstado(fx.RrhhA.Id, new CambiarEstadoUsuarioDto { Activo = false }));
        AssertOk(await fx.SuperAdminController.CambiarEstado(fx.RrhhA.Id, new CambiarEstadoUsuarioDto { Activo = true }));
        AssertOk(await fx.SuperAdminController.CambiarRol(fx.RrhhA.Id, new CambiarRolUsuarioDto { Rol = AppRoles.Admin }));
        Assert.NotNull(await fx.Service.LoginAsync(new LoginRequestDto
        {
            Email = fx.AdminA.Correo,
            Password = "Password123!"
        }));
        Assert.NotNull(await fx.Service.RestablecerPasswordAsync(fx.AdminA.Id));
        fx.AdminA.BloqueadoHasta = DateTime.UtcNow.AddMinutes(5);
        await fx.Context.SaveChangesAsync();
        Assert.True(await fx.Service.DesbloquearAsync(fx.AdminA.Id));
    }

    private static CambiarIdentidadUsuarioDto Identidad(string nombre, string correo)
    {
        return new CambiarIdentidadUsuarioDto { NombreUsuario = nombre, Correo = correo };
    }

    private static UsuarioActualizadoResponseDto AssertOk(IActionResult result)
    {
        var ok = result as ObjectResult ?? Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode ?? StatusCodes.Status200OK);
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
        var hasher = new PasswordHasher<Usuario>();
        var usuario = new Usuario
        {
            EmpresaId = empresaId,
            NombreUsuario = slug,
            Correo = $"{slug}@example.test",
            Rol = rol,
            Activo = true,
            TokenVersion = 0
        };
        usuario.PasswordHash = hasher.HashPassword(usuario, "Password123!");
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
