using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Tests;

public class EmpleadosReactivacionTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        context.Empresa.AddRange(
            new Empresa { Id = 1, NombreFantasia = "Uno", RazonSocial = "Uno S.A.", CUIT = "30-11111111-1" },
            new Empresa { Id = 2, NombreFantasia = "Dos", RazonSocial = "Dos S.A.", CUIT = "30-22222222-2" });
        context.Empleado.AddRange(
            new Empleado { Id = 10, EmpresaId = 1, DNI = "10101010", CUIL = "20-10101010-9", Nombre = "Ana", Apellido = "Activa", Activo = true },
            new Empleado { Id = 11, EmpresaId = 1, DNI = "11111111", CUIL = "20-11111111-9", Nombre = "Beto", Apellido = "Baja", Activo = false },
            new Empleado { Id = 20, EmpresaId = 2, DNI = "20202020", CUIL = "20-20202020-9", Nombre = "Ciro", Apellido = "Ajeno", Activo = false });
        context.SaveChanges();
        return context;
    }

    private static ClaimsPrincipal CrearUsuario(string rol, int? empresaId, string tokenUse = "web")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, rol),
            new("token_use", tokenUse)
        };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresa_id", empresaId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
    }

    private static EmpleadosController CrearController(AppDbContext context, ClaimsPrincipal user, string? empresaHeader = null)
    {
        if (EmpresaAccess.IsSuperAdmin(user))
        {
            var headers = new HeaderDictionary();
            if (!string.IsNullOrWhiteSpace(empresaHeader))
                headers["X-Empresa-Id"] = empresaHeader;
            EmpresaAccess.ApplyEmpresaContext(user, headers);
        }

        return new EmpleadosController(new EmpleadoService(context))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    [Fact]
    public async Task GetEmpleados_SinParametro_SoloActivosDeLaEmpresa()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, 1));

        var result = await controller.GetEmpleados();
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IEnumerable<ControlFichajes.API.DTOs.EmpleadoDto>>(ok.Value).ToList();

        Assert.Single(items);
        Assert.True(items[0].Activo);
        Assert.Equal(10, items[0].Id);
    }

    [Fact]
    public async Task GetEmpleados_IncluirInactivosFalse_SoloActivos()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));

        var result = await controller.GetEmpleados(incluirInactivos: false);
        var items = Assert.IsAssignableFrom<IEnumerable<ControlFichajes.API.DTOs.EmpleadoDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.Single(items);
        Assert.DoesNotContain(items, e => !e.Activo);
    }

    [Fact]
    public async Task GetEmpleados_IncluirInactivosTrue_ActivosEInactivosDeLaEmpresa()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, 1));

        var result = await controller.GetEmpleados(incluirInactivos: true);
        var items = Assert.IsAssignableFrom<IEnumerable<ControlFichajes.API.DTOs.EmpleadoDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, e => e.EmpresaId != 1);
        Assert.Contains(items, e => e.Id == 11 && !e.Activo);
    }

    [Fact]
    public async Task GetEmpleados_SuperAdminUsaEmpresaDelHeader()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.SuperAdmin, null), empresaHeader: "1");

        var result = await controller.GetEmpleados(incluirInactivos: true);
        var items = Assert.IsAssignableFrom<IEnumerable<ControlFichajes.API.DTOs.EmpleadoDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.Equal(2, items.Count);
        Assert.All(items, e => Assert.Equal(1, e.EmpresaId));
    }

    [Fact]
    public async Task GetEmpleados_AdminIgnoraEmpleadosDeOtraEmpresa()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, 1));

        var result = await controller.GetEmpleados(incluirInactivos: true);
        var items = Assert.IsAssignableFrom<IEnumerable<ControlFichajes.API.DTOs.EmpleadoDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).ToList();

        Assert.DoesNotContain(items, e => e.Id == 20);
    }

    [Fact]
    public async Task Reactivar_EmpleadoInactivo_Ok()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Rrhh, 1));

        var result = await controller.ReactivarEmpleado(11);

        Assert.IsType<OkObjectResult>(result);
        Assert.True((await context.Empleado.FindAsync(11))!.Activo);
    }

    [Fact]
    public async Task Reactivar_YaActivo_409()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, 1));

        var result = await controller.ReactivarEmpleado(10);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var payload = JsonSerializer.Serialize(conflict.Value);
        Assert.Contains("mensaje", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activo", payload, StringComparison.OrdinalIgnoreCase);
        Assert.True((await context.Empleado.FindAsync(10))!.Activo);
        Assert.Equal("Ana", (await context.Empleado.FindAsync(10))!.Nombre);
    }

    [Fact]
    public async Task Reactivar_IdInexistente_404()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, 1));

        var result = await controller.ReactivarEmpleado(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Reactivar_OtraEmpresa_404SinCambiar()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, 1));

        var result = await controller.ReactivarEmpleado(20);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.False((await context.Empleado.FindAsync(20))!.Activo);
    }

    [Fact]
    public async Task Reactivar_SinEmpresa_Forbid()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.SuperAdmin, null));

        var result = await controller.ReactivarEmpleado(11);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public void Reactivar_UsaAuthorizePorDefectoComoLaBaja()
    {
        var reactivar = typeof(EmpleadosController).GetMethod(nameof(EmpleadosController.ReactivarEmpleado));
        var borrar = typeof(EmpleadosController).GetMethod(nameof(EmpleadosController.DeleteEmpleado));
        var enrolar = typeof(EmpleadosController).GetMethod(nameof(EmpleadosController.EnrolarEmpleado));

        var reactivarAttr = reactivar!.GetCustomAttribute<AuthorizeAttribute>();
        var borrarAttr = borrar!.GetCustomAttribute<AuthorizeAttribute>();
        var enrolarAttr = enrolar!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(reactivarAttr);
        Assert.Equal(borrarAttr!.Policy, reactivarAttr!.Policy);
        Assert.Equal("SoloAgente", enrolarAttr!.Policy);
        Assert.True(string.IsNullOrEmpty(reactivarAttr.Policy));
    }

    [Fact]
    public async Task GetEmpleados_SuperAdminSinEmpresaDeContexto_Forbid()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.SuperAdmin, null));

        var result = await controller.GetEmpleados(incluirInactivos: true);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task DeleteEmpleado_Activo_SigueDandoDeBaja()
    {
        await using var context = CreateContext();
        var controller = CrearController(context, CrearUsuario(AppRoles.Admin, 1));

        var result = await controller.DeleteEmpleado(10);

        Assert.IsType<OkObjectResult>(result);
        Assert.False((await context.Empleado.FindAsync(10))!.Activo);
        Assert.NotNull(await context.Empleado.FindAsync(10));
    }

    [Fact]
    public void GetEmpleados_UsaWebOAgente_ReactivarUsaPoliticaWeb()
    {
        var get = typeof(EmpleadosController).GetMethod(nameof(EmpleadosController.GetEmpleados));
        var reactivar = typeof(EmpleadosController).GetMethod(nameof(EmpleadosController.ReactivarEmpleado));

        Assert.Equal("WebOAgente", get!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.True(string.IsNullOrEmpty(reactivar!.GetCustomAttribute<AuthorizeAttribute>()!.Policy));
    }
}
