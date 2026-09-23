using System.Security.Claims;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
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

    [Fact]
    public async Task PostDepartamento_CreatesSuccessfully_ReturnsFlatDto()
    {
        // Arrange
        await using var context = CreateContext();
        var controller = CreateController(context);
        var nuevoDepto = new Departamento { Nombre = "Sistemas", SucursalId = 4 };

        // Act
        var result = await controller.PostDepartamento(nuevoDepto);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<DepartamentoDto>(createdResult.Value);
        
        Assert.Equal("Sistemas", dto.Nombre);
        Assert.Equal(4, dto.SucursalId);
    }

    [Fact]
    public async Task PostDepartamento_DuplicateInSameSucursal_ReturnsConflict()
    {
        // Arrange
        await using var context = CreateContext();
        var controller = CreateController(context);
        context.Departamento.Add(new Departamento { Id = 1, Nombre = "Sistemas", SucursalId = 4 });
        await context.SaveChangesAsync();

        var duplicateDepto = new Departamento { Nombre = "Sistemas", SucursalId = 4 };

        // Act
        var result = await controller.PostDepartamento(duplicateDepto);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        var value = conflictResult.Value;
        Assert.NotNull(value);
        var msgProp = value.GetType().GetProperty("mensaje");
        Assert.Equal("Ya existe un departamento con ese nombre en esa sucursal.", msgProp?.GetValue(value));
    }

    [Fact]
    public async Task PostDepartamento_SameNameDifferentSucursal_CreatesSuccessfully()
    {
        // Arrange
        await using var context = CreateContext();
        var controller = CreateController(context);
        context.Departamento.Add(new Departamento { Id = 1, Nombre = "Sistemas", SucursalId = 4 });
        await context.SaveChangesAsync();

        var newDepto = new Departamento { Nombre = "Sistemas", SucursalId = 5 };

        // Act
        var result = await controller.PostDepartamento(newDepto);

        // Assert
        Assert.IsType<CreatedAtActionResult>(result);
        var count = await context.Departamento.CountAsync(d => d.Nombre == "Sistemas");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetDepartamento_ReturnsFlatDto()
    {
        // Arrange
        await using var context = CreateContext();
        var controller = CreateController(context);
        context.Departamento.Add(new Departamento { Id = 10, Nombre = "RRHH", SucursalId = 4 });
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetDepartamento(10);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<DepartamentoDto>(okResult.Value);

        Assert.Equal(10, dto.Id);
        Assert.Equal("RRHH", dto.Nombre);
        Assert.Equal(4, dto.SucursalId);
    }
}
