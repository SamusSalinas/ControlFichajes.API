using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Tests;

public class FichadaBiometricaServiceTests
{
    private static AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task RegistrarAsync_PrimeraFichadaDeCadaDiaEsEntrada()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(new Empleado
        {
            Id = 1,
            EmpresaId = 1,
            DNI = "DNI-1",
            CUIL = "CUIL-1",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = true
        });
        await context.SaveChangesAsync();

        var ayer = DateTime.Today.AddDays(-1).AddHours(23).AddMinutes(45);
        var service = new FichadaBiometricaService(context);
        var resultado = await service.RegistrarAsync(
        [
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = ayer },
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = ayer.Date.AddDays(1).AddMinutes(10) }
        ],
        empresaId: 1);

        var fichadas = await context.Fichada.OrderBy(f => f.FechaHora).ToListAsync();
        Assert.Null(resultado.Error);
        Assert.Equal(2, resultado.Recibidas);
        Assert.All(fichadas, f => Assert.Equal("Entrada", f.TipoRegistro));
        Assert.Equal("Incompleto", fichadas[0].Estado);
        Assert.Equal(15, fichadas[0].MinutosHastaCorte);
        Assert.Equal("Biometrico", fichadas[0].Metodo);
    }

    [Fact]
    public async Task RegistrarAsync_SalidaMismoDiaCompletaEntradaAbierta()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(new Empleado
        {
            Id = 1,
            EmpresaId = 1,
            DNI = "DNI-1",
            CUIL = "CUIL-1",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = true
        });
        await context.SaveChangesAsync();

        var fecha = DateTime.Today.AddHours(9);
        var service = new FichadaBiometricaService(context);
        await service.RegistrarAsync(
        [
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = fecha },
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = fecha.AddHours(8) }
        ],
        empresaId: 1);

        var fichadas = await context.Fichada.OrderBy(f => f.FechaHora).ToListAsync();
        Assert.Equal(new[] { "Entrada", "Salida" }, fichadas.Select(f => f.TipoRegistro));
        Assert.Equal("Completada", fichadas[0].Estado);
        Assert.Null(fichadas[0].MinutosHastaCorte);
    }

    [Fact]
    public async Task RegistrarAsync_RechazaEmpleadoFueraDeEmpresa()
    {
        await using var context = CrearContexto();
        context.Empleado.Add(new Empleado
        {
            Id = 1,
            EmpresaId = 2,
            DNI = "DNI-1",
            CUIL = "CUIL-1",
            Nombre = "Ada",
            Apellido = "Lovelace",
            Activo = true
        });
        await context.SaveChangesAsync();

        var service = new FichadaBiometricaService(context);
        var resultado = await service.RegistrarAsync(
        [
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = DateTime.Today }
        ],
        empresaId: 1);

        Assert.Equal("El lote contiene empleados inexistentes o inactivos.", resultado.Error);
        Assert.Empty(await context.Fichada.ToListAsync());
    }
}
