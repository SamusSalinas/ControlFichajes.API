using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ControlFichajes.API.Tests;

public class JornadaServiceTests
{
    private static AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Empleado CrearEmpleado(int id = 1) => new()
    {
        Id = id,
        EmpresaId = 1,
        DNI = $"DNI-{id}",
        CUIL = $"CUIL-{id}",
        Nombre = "Ada",
        Apellido = "Lovelace",
        Activo = true
    };

    [Fact]
    public async Task ObtenerResumenAsync_CuentaFichadasRealesYReposicionYAplicaTolerancia()
    {
        using var context = CrearContexto();
        var fecha = new DateTime(2026, 10, 1);
        var empleado = CrearEmpleado();
        var turno = new Turno
        {
            Id = 1,
            EmpresaId = 1,
            Nombre = "Diurno",
            ToleranciaMinutos = 5,
            Activo = true,
            Dias = new List<TurnoDia>
            {
                new()
                {
                    DiaSemana = (int)fecha.DayOfWeek,
                    HoraEntrada = new TimeSpan(9, 0, 0),
                    HoraSalida = new TimeSpan(18, 0, 0),
                    MinutosAlmuerzo = 60
                }
            }
        };
        context.Empleado.Add(empleado);
        context.Turno.Add(turno);
        context.EmpleadoTurno.Add(new EmpleadoTurno
        {
            EmpleadoId = empleado.Id,
            Turno = turno,
            FechaInicio = fecha
        });
        context.Fichada.AddRange(
            new Fichada { EmpleadoId = 1, FechaHora = fecha.AddHours(9).AddMinutes(10), TipoRegistro = "Entrada" },
            new Fichada { EmpleadoId = 1, FechaHora = fecha.AddHours(12), TipoRegistro = "Salida" },
            new Fichada { EmpleadoId = 1, FechaHora = fecha.AddHours(13), TipoRegistro = "Entrada" },
            new Fichada { EmpleadoId = 1, FechaHora = fecha.AddHours(18), TipoRegistro = "Salida" });
        context.ReposicionHoras.Add(new ReposicionHoras
        {
            EmpleadoId = 1,
            Fecha = fecha,
            Minutos = 30,
            Detalle = "Reposición autorizada",
            CreadoPorNombre = "Admin",
            CreadoEn = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var service = new JornadaService(context);
        var resumen = await service.ObtenerResumenAsync(1, 1, fecha, fecha);

        Assert.NotNull(resumen);
        Assert.Equal(480, resumen.MinutosProgramados);
        Assert.Equal(470, resumen.MinutosTrabajados);
        Assert.Equal(5, resumen.Dias.Single().MinutosTarde);
        Assert.Equal(30, resumen.MinutosRepuestos);
        Assert.Equal(20, resumen.SaldoMinutos);
    }

    [Fact]
    public async Task ObtenerCalendarioAsync_ExcepcionSoloReemplazaFechaExacta()
    {
        using var context = CrearContexto();
        var fecha = new DateTime(2026, 10, 1);
        var empleado = CrearEmpleado();
        var turno = new Turno
        {
            Id = 1,
            EmpresaId = 1,
            Nombre = "Diurno",
            ToleranciaMinutos = 10,
            Activo = true,
            Dias = Enumerable.Range(0, 7).Select(d => new TurnoDia
            {
                DiaSemana = d,
                HoraEntrada = new TimeSpan(9, 0, 0),
                HoraSalida = new TimeSpan(18, 0, 0),
                MinutosAlmuerzo = 60
            }).ToList()
        };
        context.Empleado.Add(empleado);
        context.Turno.Add(turno);
        context.EmpleadoTurno.Add(new EmpleadoTurno { EmpleadoId = 1, Turno = turno, FechaInicio = fecha });
        context.EmpleadoJornadaExcepcion.Add(new EmpleadoJornadaExcepcion
        {
            EmpleadoId = 1,
            Fecha = fecha,
            HoraEntrada = new TimeSpan(10, 0, 0),
            HoraSalida = new TimeSpan(16, 0, 0),
            ToleranciaMinutos = null
        });
        await context.SaveChangesAsync();

        var service = new JornadaService(context);
        var calendario = await service.ObtenerCalendarioAsync(1, 1, fecha, fecha.AddDays(1));

        Assert.NotNull(calendario);
        Assert.Equal("10:00", calendario[0].HoraEntrada);
        Assert.True(calendario[0].EsExcepcionManual);
        Assert.Equal(10, calendario[0].ToleranciaMinutos);
        Assert.Equal("09:00", calendario[1].HoraEntrada);
        Assert.False(calendario[1].EsExcepcionManual);
    }

    [Fact]
    public async Task AsignarTurnoAsync_CierraHistorialAnteriorDesdeFechaDeCambio()
    {
        using var context = CrearContexto();
        var empleado = CrearEmpleado();
        var primerTurno = new Turno { Id = 1, EmpresaId = 1, Nombre = "Uno", Activo = true };
        var segundoTurno = new Turno { Id = 2, EmpresaId = 1, Nombre = "Dos", Activo = true };
        var fechaInicio = new DateTime(2026, 10, 1);
        var fechaCambio = new DateTime(2026, 10, 5);
        context.Empleado.Add(empleado);
        context.Turno.AddRange(primerTurno, segundoTurno);
        context.EmpleadoTurno.Add(new EmpleadoTurno
        {
            EmpleadoId = 1,
            Turno = primerTurno,
            FechaInicio = fechaInicio
        });
        await context.SaveChangesAsync();

        var service = new JornadaService(context);
        var asignacion = await service.AsignarTurnoAsync(1, 1, new TurnoAsignacionCrearDto
        {
            TurnoId = 2,
            FechaInicio = fechaCambio
        });

        Assert.NotNull(asignacion);
        Assert.Equal(2, asignacion.TurnoId);
        var historial = await context.EmpleadoTurno.OrderBy(a => a.FechaInicio).ToListAsync();
        Assert.Equal(fechaCambio.AddDays(-1), historial[0].FechaFin);
        Assert.Equal(fechaCambio, historial[1].FechaInicio);
        Assert.Null(historial[1].FechaFin);
        var empleadoActual = await context.Empleado.FindAsync(1);
        Assert.Equal(2, empleadoActual!.TurnoId);
    }

    [Fact]
    public async Task ObtenerCalendarioAsync_TurnoSinDiasNoSeInterpretaComoDescanso()
    {
        using var context = CrearContexto();
        var fecha = new DateTime(2026, 10, 1);
        var empleado = CrearEmpleado();
        var turno = new Turno { Id = 1, EmpresaId = 1, Nombre = "Pendiente configurar", Activo = true };
        context.Empleado.Add(empleado);
        context.Turno.Add(turno);
        context.EmpleadoTurno.Add(new EmpleadoTurno { EmpleadoId = 1, Turno = turno, FechaInicio = fecha });
        await context.SaveChangesAsync();

        var service = new JornadaService(context);
        var calendario = await service.ObtenerCalendarioAsync(1, 1, fecha, fecha);

        Assert.NotNull(calendario);
        Assert.True(calendario[0].TieneTurnoAsignado);
        Assert.False(calendario[0].TieneHorarioConfigurado);
        Assert.False(calendario[0].EsDiaLibre);
    }
}
