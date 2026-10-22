using System.Reflection;
using System.Security.Claims;
using ControlFichajes.API.Controllers;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Tests;

public class FichadaBiometricaControllerTests
{
    [Fact]
    public async Task PostBulkBiometricas_SinEmpresaEnToken_RechazaSolicitud()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, CrearPrincipal(empresaId: null));

        var result = await controller.PostBulkBiometricas(
            [new FichadaRequestDto { EmpleadoId = 1, FechaHora = DateTime.Today }]);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await context.Fichada.ToListAsync());
    }

    [Fact]
    public async Task PostBulkBiometricas_LoteVacio_RechazaSolicitud()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, CrearPrincipal(empresaId: 1));

        var result = await controller.PostBulkBiometricas([]);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await context.Fichada.ToListAsync());
    }

    [Fact]
    public async Task PostBulkBiometricas_LoteMayorA500_RechazaSolicitud()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, CrearPrincipal(empresaId: 1));
        var requests = Enumerable.Range(1, 501)
            .Select(id => new FichadaRequestDto
            {
                EmpleadoId = id,
                FechaHora = new DateTime(2026, 10, 1).AddMinutes(id)
            })
            .ToList();

        var result = await controller.PostBulkBiometricas(requests);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await context.Fichada.ToListAsync());
    }

    [Fact]
    public async Task PostBulkBiometricas_EmpleadoValido_DevuelveCantidadConfirmada()
    {
        await using var context = CreateContext();
        context.Empleado.Add(new Empleado
        {
            Id = 1,
            EmpresaId = 1,
            DNI = "TEST-1",
            CUIL = "TEST-CUIL-1",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = true
        });
        await context.SaveChangesAsync();
        var controller = CreateController(context, CrearPrincipal(empresaId: 1));

        var result = await controller.PostBulkBiometricas(
        [
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = new DateTime(2026, 10, 13, 9, 0, 0) }
        ]);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, ok.Value!.GetType().GetProperty("recibidas")!.GetValue(ok.Value));
        Assert.Equal("Entrada", (await context.Fichada.SingleAsync()).TipoRegistro);
    }

    [Fact]
    public void PostBulkBiometricas_ExigePoliticaDeAgente()
    {
        var method = typeof(FichadasController).GetMethod(nameof(FichadasController.PostBulkBiometricas));

        var authorization = Assert.Single(method!.GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("SoloAgente", authorization.Policy);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static FichadasController CreateController(AppDbContext context, ClaimsPrincipal user)
    {
        return new FichadasController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static ClaimsPrincipal CrearPrincipal(int? empresaId)
    {
        var claims = new List<Claim>
        {
            new("token_use", "agent"),
            new(ClaimTypes.Role, "AGENTE_SUCURSAL")
        };
        if (empresaId.HasValue)
            claims.Add(new Claim("empresa_id", empresaId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
