using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ControlFichajes.API.Tests;

public class TurnoServiceTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task CrearAsync_CreaTurnoConDiasYRetornaDto()
    {
        using var context = CreateContext();
        var service = new TurnoService(context);

        var dto = new TurnoCrearDto
        {
            Nombre = "Turno Rotativo",
            ToleranciaMinutos = 10,
            Dias = new List<TurnoDiaDto>
            {
                new() { DiaSemana = 1, HoraEntrada = "08:00", HoraSalida = "16:00" },
                new() { DiaSemana = 2, HoraEntrada = "08:00", HoraSalida = "16:00" }
            }
        };

        var resultado = await service.CrearAsync(1, dto);

        Assert.NotNull(resultado);
        Assert.True(resultado.Id > 0);
        Assert.Equal("Turno Rotativo", resultado.Nombre);
        Assert.Equal(2, resultado.Dias.Count);

        var entidadDb = await context.Turno.Include(t => t.Dias).FirstOrDefaultAsync(t => t.Id == resultado.Id);
        Assert.NotNull(entidadDb);
        Assert.True(entidadDb.Activo);
        Assert.Equal(1, entidadDb.EmpresaId);
        Assert.Equal(2, entidadDb.Dias.Count);
        Assert.Equal(60, entidadDb.Dias.Single(d => d.DiaSemana == 1).MinutosAlmuerzo);
    }

    [Fact]
    public async Task ObtenerPorEmpresaAsync_SoloRetornaTurnosActivosDeLaEmpresa()
    {
        using var context = CreateContext();
        context.Turno.AddRange(
            new Turno { Id = 1, EmpresaId = 1, Nombre = "Turno 1", Activo = true },
            new Turno { Id = 2, EmpresaId = 1, Nombre = "Turno Inactivo", Activo = false },
            new Turno { Id = 3, EmpresaId = 2, Nombre = "Turno Otra Empresa", Activo = true }
        );
        await context.SaveChangesAsync();

        var service = new TurnoService(context);
        var turnos = (await service.ObtenerPorEmpresaAsync(1)).ToList();

        Assert.Single(turnos);
        Assert.Equal(1, turnos[0].Id);
    }

    [Fact]
    public async Task ActualizarAsync_ModificaDatosYDias()
    {
        using var context = CreateContext();
        var turno = new Turno
        {
            Id = 1,
            EmpresaId = 1,
            Nombre = "Original",
            ToleranciaMinutos = 5,
            Activo = true,
            Dias = new List<TurnoDia>
            {
                new() { DiaSemana = 1, HoraEntrada = new TimeSpan(8, 0, 0), HoraSalida = new TimeSpan(17, 0, 0), MinutosAlmuerzo = 60 }
            }
        };
        context.Turno.Add(turno);
        await context.SaveChangesAsync();

        var service = new TurnoService(context);
        var updateDto = new TurnoCrearDto
        {
            Nombre = "Actualizado",
            ToleranciaMinutos = 15,
            Dias = new List<TurnoDiaDto>
            {
                new() { DiaSemana = 2, HoraEntrada = "09:00", HoraSalida = "18:00" },
                new() { DiaSemana = 3, HoraEntrada = "09:00", HoraSalida = "18:00" }
            }
        };

        var ok = await service.ActualizarAsync(1, 1, updateDto);

        Assert.True(ok);
        var turnoDb = await context.Turno.Include(t => t.Dias).FirstOrDefaultAsync(t => t.Id == 1);
        Assert.NotNull(turnoDb);
        Assert.Equal("Actualizado", turnoDb.Nombre);
        Assert.Equal(15, turnoDb.ToleranciaMinutos);
        Assert.Equal(2, turnoDb.Dias.Count);
    }

    [Fact]
    public async Task EliminarAsync_RealizaBorradoLogico()
    {
        using var context = CreateContext();
        var turno = new Turno { Id = 1, EmpresaId = 1, Nombre = "Para Borrar", Activo = true };
        context.Turno.Add(turno);
        await context.SaveChangesAsync();

        var service = new TurnoService(context);
        var ok = await service.EliminarAsync(1, 1);

        Assert.True(ok);
        var turnoDb = await context.Turno.FindAsync(1);
        Assert.NotNull(turnoDb);
        Assert.False(turnoDb.Activo);
    }
}
