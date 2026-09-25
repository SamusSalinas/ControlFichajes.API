using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ControlFichajes.API.Tests;

public class DepartamentosControllerTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);
        
        context.Empresa.Add(new Empresa { Id = 1, NombreFantasia = "Test", RazonSocial = "Test", CUIT = "30" });
        context.Sucursal.Add(new Sucursal { Id = 4, EmpresaId = 1, Nombre = "Sucursal Test" });
        context.Sucursal.Add(new Sucursal { Id = 5, EmpresaId = 1, Nombre = "Otra Sucursal" });
        context.SaveChanges();
        
        return context;
    }

    private static DepartamentosController CreateController(AppDbContext context, int empresaId = 1, string role = "ADMIN")
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("empresa_id", empresaId.ToString()),
            new Claim(ClaimTypes.Role, role)
        }, "TestAuthentication"));

        var controller = new DepartamentosController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };

        return controller;
    }

    // --- PRUEBAS DE SEGURIDAD Y ROLES ---

    [Fact]
    public void PostDepartamento_BloqueaA_RRHH_RequiereRolesAdminOSuperAdmin()
    {
        var methodInfo = typeof(DepartamentosController).GetMethod(nameof(DepartamentosController.PostDepartamento));
        var classInfo = typeof(DepartamentosController);

        var authorizeAttributes = methodInfo?.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Concat(classInfo.GetCustomAttributes(typeof(AuthorizeAttribute), true))
            .Cast<AuthorizeAttribute>()
            .ToList();

        Assert.NotNull(authorizeAttributes);
        Assert.NotEmpty(authorizeAttributes);
        
        bool tieneRestriccionValida = authorizeAttributes.Any(attr => 
            (attr.Roles != null && attr.Roles.Contains("ADMIN") && attr.Roles.Contains("SuperAdmin") && !attr.Roles.Contains("RRHH")) ||
            (attr.Policy == "PuedeAdministrarDepartamentos" || attr.Policy == "SoloSuperadmin"));

        Assert.True(tieneRestriccionValida, "El endpoint POST no tiene un atributo [Authorize] que restrinja correctamente el acceso a RRHH.");
    }

    // --- PRUEBAS DE CREACIÓN (POST) ---

    [Fact]
    public async Task PostDepartamento_CreatesSuccessfully_ReturnsFlatDto()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        var nuevoDepto = new DepartamentoCrearDto { Nombre = "Sistemas", SucursalId = 4 };

        var result = await controller.PostDepartamento(nuevoDepto);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<DepartamentoDto>(createdResult.Value);
        
        Assert.Equal("Sistemas", dto.Nombre);
        Assert.Equal(4, dto.SucursalId);
        Assert.True(dto.Id > 0);
    }

    [Fact]
    public async Task PostDepartamento_DuplicateInSameSucursal_ReturnsConflict()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        context.Departamento.Add(new Departamento { Id = 1, Nombre = "Sistemas", SucursalId = 4 });
        await context.SaveChangesAsync();

        var duplicateDepto = new DepartamentoCrearDto { Nombre = "Sistemas", SucursalId = 4 };

        var result = await controller.PostDepartamento(duplicateDepto);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        var value = conflictResult.Value;
        Assert.NotNull(value);
        var msgProp = value.GetType().GetProperty("mensaje");
        Assert.Equal("Ya existe un departamento con ese nombre en esa sucursal.", msgProp?.GetValue(value));
    }

    [Fact]
    public async Task PostDepartamento_SameNameDifferentSucursal_CreatesSuccessfully()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        context.Departamento.Add(new Departamento { Id = 1, Nombre = "Sistemas", SucursalId = 4 });
        await context.SaveChangesAsync();

        var newDepto = new DepartamentoCrearDto { Nombre = "Sistemas", SucursalId = 5 };

        var result = await controller.PostDepartamento(newDepto);

        Assert.IsType<CreatedAtActionResult>(result);
        var count = await context.Departamento.CountAsync(d => d.Nombre == "Sistemas");
        Assert.Equal(2, count);
    }

    // --- PRUEBAS DE LECTURA (GET) ---

    [Fact]
    public async Task GetDepartamento_ReturnsFlatDto()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, role: "RRHH"); 
        context.Departamento.Add(new Departamento { Id = 10, Nombre = "RRHH", SucursalId = 4 });
        await context.SaveChangesAsync();

        var result = await controller.GetDepartamento(10);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<DepartamentoDto>(okResult.Value);

        Assert.Equal(10, dto.Id);
        Assert.Equal("RRHH", dto.Nombre);
        Assert.Equal(4, dto.SucursalId);
    }

    // --- PRUEBAS DE MODIFICACIÓN (PUT) ---

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("SuperAdmin")]
    public async Task PutDepartamento_AsAdminOrSuperAdmin_UpdatesSuccessfully_ReturnsNoContent(string role)
    {
        await using var context = CreateContext();
        var controller = CreateController(context, empresaId: 1, role: role);
        var depto = new Departamento { Id = 20, Nombre = "Marketing", SucursalId = 4 };
        context.Departamento.Add(depto);
        await context.SaveChangesAsync();

        // Sin propiedad Id porque el DTO nos protege
        var updatedDepto = new DepartamentoCrearDto { Nombre = "Marketing y Ventas", SucursalId = 4 };

        var result = await controller.PutDepartamento(20, updatedDepto);

        Assert.IsType<NoContentResult>(result);
        var dbDepto = await context.Departamento.FindAsync(20);
        Assert.NotNull(dbDepto);
        Assert.Equal("Marketing y Ventas", dbDepto.Nombre);
    }

    [Fact]
    public async Task PutDepartamento_NotFound_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var controller = CreateController(context);
        var updatedDepto = new DepartamentoCrearDto { Nombre = "Inexistente", SucursalId = 4 };

        var result = await controller.PutDepartamento(99, updatedDepto);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task PutDepartamento_DifferentEmpresa_ReturnsForbid()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, empresaId: 2, role: "ADMIN");
        var depto = new Departamento { Id = 30, Nombre = "IT", SucursalId = 4 };
        context.Departamento.Add(depto);
        await context.SaveChangesAsync();

        var updatedDepto = new DepartamentoCrearDto { Nombre = "IT Modificado", SucursalId = 4 };

        var result = await controller.PutDepartamento(30, updatedDepto);

        Assert.IsType<ForbidResult>(result);
    }
}