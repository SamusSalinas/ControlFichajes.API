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

public class FichadasManualesControllerTests
{
    private static (AppDbContext Context, FichadasManualesController Controller) CrearController(int empresaId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
        var identity = new ClaimsIdentity(
        [
            new Claim("empresa_id", empresaId.ToString()),
            new Claim(ClaimTypes.Name, "Admin de prueba"),
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "ADMIN"),
            new Claim("token_use", "web")
        ], "test");
        var controller = new FichadasManualesController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
        return (context, controller);
    }

    [Fact]
    public async Task InsertarFichadaManual_CreaMarcaManualConAuditoria()
    {
        var (context, controller) = CrearController(1);
        context.Empleado.Add(new Empleado
        {
            Id = 1,
            EmpresaId = 1,
            DNI = "10000001",
            CUIL = "20000000001",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = true
        });
        await context.SaveChangesAsync();

        var result = await controller.InsertarFichadaManual(new FichadaManualCrearDto
        {
            EmpleadoId = 1,
            FechaHora = new DateTime(2026, 10, 1, 9, 0, 0),
            Tipo = "Entrada",
            Motivo = "OlvidoDeFichaje",
            Detalle = "El lector no registró la entrada"
        });

        Assert.IsType<CreatedResult>(result);
        var fichada = await context.Fichada.Include(f => f.Observacion).SingleAsync();
        Assert.True(fichada.EsManual);
        Assert.Equal("Manual", fichada.Metodo);
        Assert.Equal("Completada", fichada.Estado);
        Assert.Equal("OlvidoDeFichaje", fichada.Observacion!.Motivo);
        Assert.Equal("Admin de prueba", fichada.Observacion.CreadoPorNombre);
        Assert.NotEqual(default, fichada.Observacion.CreadoEn);
    }

    [Fact]
    public async Task InsertarFichadaManual_RechazaEmpleadoDeOtraEmpresa()
    {
        var (context, controller) = CrearController(1);
        context.Empleado.Add(new Empleado
        {
            Id = 2,
            EmpresaId = 2,
            DNI = "10000002",
            CUIL = "20000000002",
            Nombre = "Grace",
            Apellido = "Hopper",
            Activo = true
        });
        await context.SaveChangesAsync();

        var result = await controller.InsertarFichadaManual(new FichadaManualCrearDto
        {
            EmpleadoId = 2,
            FechaHora = new DateTime(2026, 10, 1, 9, 0, 0),
            Tipo = "Entrada",
            Motivo = "OlvidoDeFichaje",
            Detalle = "Prueba de aislamiento"
        });

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(await context.Fichada.ToListAsync());
    }
}
