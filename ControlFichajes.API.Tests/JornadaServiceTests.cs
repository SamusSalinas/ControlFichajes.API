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
        Assert.Equal(530, resumen.ActividadRegistrada);
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
        Assert.Equal(2, historial[1].TurnoId);
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

    [Fact]
    public async Task Resumen_JornadaExactaDescuentaAlmuerzoUnaVezYSaldoCero()
    {
        var fecha = new DateTime(2026, 10, 22);
        var resumen = await ResumenDe(fecha, new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), 60, null,
            fecha.AddHours(8), fecha.AddHours(17));

        Assert.Equal(540, resumen.ActividadRegistrada);
        Assert.Equal(480, resumen.MinutosProgramados);
        Assert.Equal(480, resumen.MinutosTrabajados);
        Assert.Equal(0, resumen.SaldoMinutos);
    }

    [Fact]
    public async Task Resumen_LlegadaTardeDescuentaAlmuerzoSobrePrimeraYUltima()
    {
        var fecha = new DateTime(2026, 10, 22);
        var resumen = await ResumenDe(fecha, new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), 60, null,
            fecha.AddHours(9).AddMinutes(10), fecha.AddHours(18));

        Assert.Equal(530, resumen.ActividadRegistrada);
        Assert.Equal(480, resumen.MinutosProgramados);
        Assert.Equal(470, resumen.MinutosTrabajados);
        Assert.Equal(-10, resumen.SaldoMinutos);
    }

    [Fact]
    public async Task Resumen_ReposicionSeSumaAlSaldoNeto()
    {
        var fecha = new DateTime(2026, 10, 22);
        var resumen = await ResumenDe(fecha, new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), 60, 30,
            fecha.AddHours(9).AddMinutes(10), fecha.AddHours(18));

        Assert.Equal(470, resumen.MinutosTrabajados);
        Assert.Equal(30, resumen.MinutosRepuestos);
        Assert.Equal(20, resumen.SaldoMinutos);
    }

    [Fact]
    public async Task Resumen_PresenciaMenorAlAlmuerzoNoDevuelveMinutosNegativos()
    {
        var fecha = new DateTime(2026, 10, 22);
        var resumen = await ResumenDe(fecha, new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), 60, null,
            fecha.AddHours(8), fecha.AddHours(8).AddMinutes(20));

        Assert.Equal(20, resumen.ActividadRegistrada);
        Assert.Equal(0, resumen.MinutosTrabajados);
        Assert.True(resumen.MinutosTrabajados >= 0);
        Assert.True(resumen.MinutosProgramados >= 0);
    }

    [Fact]
    public async Task Resumen_TurnoNocturnoCompletoQuedaEnCero()
    {
        var fecha = new DateTime(2026, 10, 21);
        var resumen = await ResumenDe(fecha, new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), 60, null,
            fecha.AddHours(22), fecha.AddDays(1).AddHours(6));

        Assert.Equal(480, resumen.ActividadRegistrada);
        Assert.Equal(420, resumen.MinutosProgramados);
        Assert.Equal(420, resumen.MinutosTrabajados);
        Assert.Equal(0, resumen.SaldoMinutos);
    }

    private static async Task<ResumenHorasDto> ResumenDe(
        DateTime fecha,
        TimeSpan entrada,
        TimeSpan salida,
        int almuerzo,
        int? reposicion,
        params DateTime[] fichadas)
    {
        using var context = CrearContexto();
        var turno = new Turno
        {
            Id = 1,
            EmpresaId = 1,
            Nombre = "Horario",
            ToleranciaMinutos = 0,
            Activo = true,
            Dias =
            [
                new TurnoDia
                {
                    DiaSemana = (int)fecha.DayOfWeek,
                    HoraEntrada = entrada,
                    HoraSalida = salida,
                    MinutosAlmuerzo = almuerzo
                }
            ]
        };
        context.Empleado.Add(CrearEmpleado());
        context.Turno.Add(turno);
        context.EmpleadoTurno.Add(new EmpleadoTurno { EmpleadoId = 1, Turno = turno, FechaInicio = fecha });
        context.Fichada.AddRange(fichadas.Select((hora, indice) => new Fichada
        {
            Id = indice + 1,
            EmpleadoId = 1,
            FechaHora = hora,
            TipoRegistro = "SinTipo"
        }));
        if (reposicion.HasValue)
        {
            context.ReposicionHoras.Add(new ReposicionHoras
            {
                EmpleadoId = 1,
                Fecha = fecha,
                Minutos = reposicion.Value,
                Detalle = "Reposición",
                CreadoPorNombre = "Admin",
                CreadoEn = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        var resumen = await new JornadaService(context).ObtenerResumenAsync(1, 1, fecha, fecha);
        Assert.NotNull(resumen);
        return resumen;
    }
}
