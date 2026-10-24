using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Tests;

public class JornadaValidacionFinalTests
{
    private static readonly DateTime Dia = new(2026, 10, 22);
    private static readonly DateTime Ahora = new(2026, 10, 22, 23, 0, 0);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Cantidad_PrimeraEntradaYUltimaSalida(int cantidad)
    {
        var fichadas = Enumerable.Range(0, cantidad)
            .Select(i => new FichadaParaClasificar(i + 1, 1, 1, Dia.AddHours(8 + i), false))
            .ToList();
        var clases = JornadaClasificador.Clasificar(fichadas, Ahora);
        Assert.Equal(cantidad, clases.Count);
        if (cantidad == 0)
            return;
        Assert.Equal(JornadaClasificador.Entrada, clases.Single(c => c.Id == 1).MovimientoVisual);
        if (cantidad > 1)
            Assert.Equal(JornadaClasificador.Salida, clases.Single(c => c.Id == cantidad).MovimientoVisual);
        Assert.Equal(Math.Max(0, cantidad - 2), clases.Count(c => c.MovimientoVisual == JornadaClasificador.Intermedio));
    }

    [Fact]
    public void Orden_InversoYMezclado_NoCambiaLaJornada()
    {
        var ordenado = Horas(8, 12, 15, 17);
        var inverso = Horas(17, 15, 12, 8);
        var mezclado = Horas(15, 8, 17, 12);
        var a = Visuales(ordenado);
        Assert.Equal(a, Visuales(inverso));
        Assert.Equal(a, Visuales(mezclado));
    }

    [Fact]
    public void TimestampPorDefectoYFuturo_QuedanEnRevisar()
    {
        var defecto = JornadaClasificador.Clasificar(
            [new FichadaParaClasificar(1, 1, 1, default, false)], Ahora).Single();
        Assert.Equal(JornadaClasificador.Revisar, defecto.JornadaEstado);
        Assert.Equal("Inconsistencia temporal", defecto.JornadaMotivo);
        var futuro = JornadaClasificador.Clasificar(
            [new FichadaParaClasificar(1, 1, 1, Ahora.AddSeconds(1), false)], Ahora).Single();
        Assert.Equal(JornadaClasificador.Revisar, futuro.JornadaEstado);
        Assert.Equal("Timestamp futuro", futuro.JornadaMotivo);
    }

    [Fact]
    public void Limites_DoceHorasYMedianoche_AlSegundo()
    {
        var ocho = Dia.AddHours(8);
        Assert.Equal(JornadaClasificador.EnCurso, Estado(ocho, ocho.AddHours(12).AddSeconds(-1)));
        Assert.Equal(JornadaClasificador.Incompleta, Estado(ocho, ocho.AddHours(12)));
        Assert.Equal(JornadaClasificador.Incompleta, Estado(ocho, ocho.AddHours(12).AddSeconds(1)));

        var noche = Dia.AddHours(23).AddMinutes(50);
        var medianoche = Dia.AddDays(1);
        Assert.Equal(JornadaClasificador.EnCurso, Estado(noche, medianoche.AddSeconds(-1)));
        Assert.Equal(JornadaClasificador.Incompleta, Estado(noche, medianoche));
        Assert.Equal(JornadaClasificador.Incompleta, Estado(noche, medianoche.AddSeconds(1)));
        Assert.Equal(JornadaClasificador.MotivoFaltaSalida, JornadaClasificador.Clasificar(
            [new FichadaParaClasificar(1, 1, 1, noche, false)], medianoche).Single().JornadaMotivo);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(59, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Duplicado_VentanaInclusiveDe60Segundos(int segundos, bool duplicado)
    {
        var baseHora = Dia.AddHours(8);
        var clases = JornadaClasificador.Clasificar(
            [
                new FichadaParaClasificar(1, 1, 1, baseHora, false),
                new FichadaParaClasificar(2, 1, 1, baseHora.AddSeconds(segundos), false)
            ],
            Ahora);
        Assert.Equal(duplicado, clases.Single(c => c.Id == 2).EsDuplicado);
        Assert.Equal(duplicado ? JornadaClasificador.Intermedio : JornadaClasificador.Salida, clases.Single(c => c.Id == 2).MovimientoVisual);
        if (duplicado)
            Assert.Equal(0, JornadaClasificador.Contar(clases).Salidas);
    }

    [Fact]
    public void Duplicado_NoCruzaEmpleadoNiEmpresa_YManualConservaElDato()
    {
        var hora = Dia.AddHours(8);
        var empleados = JornadaClasificador.Clasificar(
            [
                new FichadaParaClasificar(1, 1, 1, hora, false),
                new FichadaParaClasificar(2, 2, 1, hora, false)
            ], Ahora);
        var empresas = JornadaClasificador.Clasificar(
            [
                new FichadaParaClasificar(1, 1, 1, hora, false),
                new FichadaParaClasificar(2, 1, 2, hora, false)
            ], Ahora);
        Assert.All(empleados.Concat(empresas), c => Assert.False(c.EsDuplicado));

        var manual = new Fichada
        {
            Id = 9,
            EmpleadoId = 1,
            FechaHora = hora.AddHours(9),
            TipoRegistro = "Salida",
            EsManual = true
        };
        var clases = JornadaClasificador.Clasificar(
            [
                new FichadaParaClasificar(1, 1, 1, hora, false),
                new FichadaParaClasificar(manual.Id, 1, 1, manual.FechaHora, true)
            ], Ahora);
        Assert.Equal("Salida", manual.TipoRegistro);
        Assert.Equal(JornadaClasificador.Salida, clases.Single(c => c.Id == 9).MovimientoVisual);
    }

    [Fact]
    public void Zona_UtcYArgentina_ProducenLaMismaJornada()
    {
        var utc = new DateTime(2026, 10, 24, 2, 30, 0, DateTimeKind.Utc);
        var pared = LaborTime.Pared(utc);
        Assert.Equal(new DateTime(2026, 10, 23, 23, 30, 0), pared);
        var porInstante = JornadaClasificador.Clasificar(
            [new FichadaParaClasificar(1, 1, 1, utc, false)],
            new DateTime(2026, 10, 24, 12, 0, 0));
        var porPared = JornadaClasificador.Clasificar(
            [new FichadaParaClasificar(1, 1, 1, pared, false)],
            new DateTime(2026, 10, 24, 12, 0, 0));
        Assert.Equal(porPared.Single().JornadaFecha, porInstante.Single().JornadaFecha);
        Assert.Equal(new DateOnly(2026, 10, 23), porInstante.Single().JornadaFecha);

        var ahoraUtc = new DateTime(2026, 10, 23, 2, 0, 0, DateTimeKind.Utc);
        var ahoraArgentina = LaborTime.DesdeUtc(ahoraUtc);
        var fichadaArgentina = new DateTime(2026, 10, 23, 0, 30, 0);
        var futuro = JornadaClasificador.Clasificar(
            [new FichadaParaClasificar(1, 1, 1, fichadaArgentina, false)],
            ahoraArgentina).Single();
        Assert.Equal(JornadaClasificador.Revisar, futuro.JornadaEstado);
        Assert.Equal(new DateTime(2026, 10, 22, 23, 0, 0), ahoraArgentina);

        var conOffset = DateTimeOffset.Parse("2026-10-22T22:00:00-03:00").UtcDateTime;
        Assert.Equal(new DateTime(2026, 10, 22, 22, 0, 0), LaborTime.DesdeUtc(conOffset));
    }

    [Fact]
    public void Nocturno_ToleranciaYEmpleadoSinAsignacion()
    {
        HorarioPrevisto? Horario(int _, DateOnly fecha) =>
            fecha == new DateOnly(2026, 10, 21)
                ? new HorarioPrevisto(new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0), 15)
                : null;
        var dentro = new DateTime(2026, 10, 22, 6, 10, 0);
        var fuera = new DateTime(2026, 10, 22, 6, 16, 0);
        var conTurno = JornadaClasificador.Clasificar(
            [
                new FichadaParaClasificar(1, 1, 1, new DateTime(2026, 10, 21, 22, 0, 0), false),
                new FichadaParaClasificar(2, 1, 1, dentro, false)
            ],
            new DateTime(2026, 10, 22, 12, 0, 0),
            Horario);
        Assert.All(conTurno, c => Assert.Equal(new DateOnly(2026, 10, 21), c.JornadaFecha));
        var madrugadaFuera = JornadaClasificador.Clasificar(
            [new FichadaParaClasificar(3, 1, 1, fuera, false)],
            new DateTime(2026, 10, 22, 12, 0, 0),
            Horario).Single();
        Assert.Equal(new DateOnly(2026, 10, 22), madrugadaFuera.JornadaFecha);
        var sinAsignacion = JornadaClasificador.Clasificar(
            [new FichadaParaClasificar(4, 1, 1, dentro, false)],
            new DateTime(2026, 10, 22, 12, 0, 0)).Single();
        Assert.Equal(new DateOnly(2026, 10, 22), sinAsignacion.JornadaFecha);
    }

    [Fact]
    public async Task Prioridad_AsignacionExcepcionLegadoYSinHorario()
    {
        await using var context = Contexto();
        context.Empleado.AddRange(
            Empleado(1, "09:00-18:00"),
            Empleado(2, "09:00-18:00"),
            Empleado(3, "no-es-un-horario"),
            Empleado(4, null));
        var turno = Turno(new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), 60);
        context.Turno.Add(turno);
        context.EmpleadoTurno.Add(new EmpleadoTurno { EmpleadoId = 1, Turno = turno, FechaInicio = Dia.AddDays(-1), FechaFin = Dia.AddDays(1) });
        context.EmpleadoJornadaExcepcion.Add(new EmpleadoJornadaExcepcion
        {
            EmpleadoId = 1,
            Fecha = Dia,
            EsDiaLibre = false,
            HoraEntrada = new TimeSpan(10, 0, 0),
            HoraSalida = new TimeSpan(16, 0, 0)
        });
        await context.SaveChangesAsync();
        var horario = await new JornadaClasificacionConsulta(context).CargarHorariosAsync(1, [1, 2, 3, 4], Dia, Dia.AddDays(1));
        var excepcion = horario(1, DateOnly.FromDateTime(Dia));
        Assert.Equal(new TimeSpan(10, 0, 0), excepcion!.Entrada);
        Assert.Null(horario(1, DateOnly.FromDateTime(Dia.AddDays(-1))));
        Assert.Equal(new TimeSpan(9, 0, 0), horario(2, DateOnly.FromDateTime(Dia))!.Entrada);
        Assert.Null(horario(3, DateOnly.FromDateTime(Dia)));
        Assert.Null(horario(4, DateOnly.FromDateTime(Dia)));
        Assert.Equal(new TimeSpan(9, 0, 0), horario(1, DateOnly.FromDateTime(Dia.AddDays(3)))!.Entrada);
    }

    [Fact]
    public async Task AsignarTurno_NoModificaHorarioLegado()
    {
        await using var context = Contexto();
        context.Empleado.Add(Empleado(1, "09:00-18:00"));
        context.Turno.Add(Turno(new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), 60));
        await context.SaveChangesAsync();
        await new JornadaService(context).AsignarTurnoAsync(1, 1, new TurnoAsignacionCrearDto
        {
            TurnoId = 1,
            FechaInicio = Dia
        });
        Assert.Equal("09:00-18:00", (await context.Empleado.FindAsync(1))!.Horario);
    }

    [Fact]
    public async Task Resumen_MatrizDeAlmuerzoReposicionYEstados()
    {
        var exacto = await Resumen(60, null, Dia.AddHours(8), Dia.AddHours(17));
        Assert.Equal((540, 480, 480, 0, 0), Numeros(exacto));

        var tarde = await Resumen(60, null, Dia.AddHours(9).AddMinutes(10), Dia.AddHours(18));
        Assert.Equal((530, 480, 470, 0, -10), Numeros(tarde));

        var repuesta = await Resumen(60, [30], Dia.AddHours(9).AddMinutes(10), Dia.AddHours(18));
        Assert.Equal((530, 480, 470, 30, 20), Numeros(repuesta));

        var varias = await Resumen(60, [10, 20], Dia.AddHours(8), Dia.AddHours(17));
        Assert.Equal(30, varias.MinutosRepuestos);
        Assert.Equal(30, varias.SaldoMinutos);

        var cero = await Resumen(0, [0], Dia.AddHours(8), Dia.AddHours(17));
        Assert.Equal((540, 540, 540, 0, 0), Numeros(cero));

        var corta = await Resumen(60, null, Dia.AddHours(8), Dia.AddHours(8).AddMinutes(20));
        Assert.Equal(20, corta.ActividadRegistrada);
        Assert.Equal(0, corta.MinutosTrabajados);

        var una = await Resumen(60, null, Dia.AddHours(8));
        Assert.Equal(0, una.ActividadRegistrada);
        Assert.Equal(480, una.MinutosProgramados);
        Assert.Equal(0, una.MinutosTrabajados);
        Assert.Equal(-480, una.SaldoMinutos);
        Assert.True(una.Dias.Single().Incompleta);

        var sinTurno = await Resumen(null, null, Dia.AddHours(8), Dia.AddHours(17));
        Assert.Equal(540, sinTurno.ActividadRegistrada);
        Assert.Equal(0, sinTurno.MinutosProgramados);
        Assert.Equal(540, sinTurno.MinutosTrabajados);
        Assert.Equal(540, sinTurno.SaldoMinutos);

        var duplicado = await Resumen(60, null, Dia.AddHours(8), Dia.AddHours(8).AddSeconds(30));
        Assert.Equal(0, duplicado.ActividadRegistrada);
        Assert.Equal(0, duplicado.MinutosTrabajados);
    }

    [Fact]
    public async Task Resumen_AlmuerzoMayorQueElTurnoDiaLibreYExcepcion()
    {
        var corto = await ResumenHorario(new TimeSpan(8, 0, 0), new TimeSpan(8, 30, 0), 90, null, Dia.AddHours(8), Dia.AddHours(8).AddMinutes(30));
        Assert.Equal(0, corto.MinutosProgramados);
        Assert.Equal(30, corto.ActividadRegistrada);
        Assert.Equal(0, corto.MinutosTrabajados);
        Assert.Equal(0, corto.SaldoMinutos);

        await using var context = Contexto();
        context.Empleado.Add(Empleado(1, null));
        var turno = Turno(new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), 60);
        context.Turno.Add(turno);
        context.EmpleadoTurno.Add(new EmpleadoTurno { EmpleadoId = 1, Turno = turno, FechaInicio = Dia });
        context.EmpleadoJornadaExcepcion.Add(new EmpleadoJornadaExcepcion
        {
            EmpleadoId = 1,
            Fecha = Dia,
            EsDiaLibre = true
        });
        context.Fichada.Add(new Fichada { EmpleadoId = 1, FechaHora = Dia.AddHours(9), TipoRegistro = "SinTipo" });
        await context.SaveChangesAsync();
        var libre = await new JornadaService(context).ObtenerResumenAsync(1, 1, Dia, Dia);
        Assert.NotNull(libre);
        Assert.Equal(0, libre.MinutosProgramados);
        Assert.Equal(0, libre.MinutosTrabajados);

        await using var otro = Contexto();
        otro.Empleado.Add(Empleado(1, null));
        var baseTurno = Turno(new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), 60);
        otro.Turno.Add(baseTurno);
        otro.EmpleadoTurno.Add(new EmpleadoTurno { EmpleadoId = 1, Turno = baseTurno, FechaInicio = Dia });
        otro.EmpleadoJornadaExcepcion.Add(new EmpleadoJornadaExcepcion
        {
            EmpleadoId = 1,
            Fecha = Dia,
            HoraEntrada = new TimeSpan(10, 0, 0),
            HoraSalida = new TimeSpan(16, 0, 0)
        });
        otro.Fichada.AddRange(
            new Fichada { Id = 1, EmpleadoId = 1, FechaHora = Dia.AddHours(10), TipoRegistro = "SinTipo" },
            new Fichada { Id = 2, EmpleadoId = 1, FechaHora = Dia.AddHours(16), TipoRegistro = "SinTipo" });
        await otro.SaveChangesAsync();
        var excepcion = await new JornadaService(otro).ObtenerResumenAsync(1, 1, Dia, Dia);
        Assert.Equal((360, 300, 300, 0, 0), Numeros(excepcion!));
    }

    [Fact]
    public async Task Biometrico_ReenvioYDesorden_SonDeterministas()
    {
        await using var context = Contexto();
        context.Empleado.Add(Empleado(1, null));
        await context.SaveChangesAsync();
        var service = new FichadaBiometricaService(context);
        var lote = new[]
        {
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = Dia.AddHours(17) },
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = Dia.AddHours(8) },
            new FichadaRequestDto { EmpleadoId = 1, FechaHora = Dia.AddHours(8) }
        };
        var primero = await service.RegistrarAsync(lote, 1);
        var segundo = await service.RegistrarAsync(lote, 1);
        Assert.Null(primero.Error);
        Assert.Null(segundo.Error);
        Assert.Equal(3, primero.Recibidas);
        Assert.Equal(3, segundo.Recibidas);
        var guardadas = await context.Fichada.OrderBy(f => f.FechaHora).ToListAsync();
        Assert.Equal(2, guardadas.Count);
        Assert.All(guardadas, f => Assert.Equal("SinTipo", f.TipoRegistro));
    }

    private static string Estado(DateTime fichada, DateTime ahora) =>
        JornadaClasificador.Clasificar([new FichadaParaClasificar(1, 1, 1, fichada, false)], ahora).Single().JornadaEstado;

    private static List<FichadaParaClasificar> Horas(params int[] horas) =>
        horas.Select((hora, i) => new FichadaParaClasificar(i + 1, 1, 1, Dia.AddHours(hora), false)).ToList();

    private static string Visuales(List<FichadaParaClasificar> fichadas) =>
        string.Join("|", JornadaClasificador.Clasificar(fichadas, Ahora)
            .OrderBy(c => fichadas.Single(f => f.Id == c.Id).FechaHora)
            .Select(c => c.MovimientoVisual));

    private static (int Actividad, int Programados, int Trabajados, int Repuestos, int Saldo) Numeros(ResumenHorasDto resumen) =>
        (resumen.ActividadRegistrada, resumen.MinutosProgramados, resumen.MinutosTrabajados, resumen.MinutosRepuestos, resumen.SaldoMinutos);

    private static Task<ResumenHorasDto> Resumen(int? almuerzo, int[]? reposiciones, params DateTime[] fichadas) =>
        almuerzo.HasValue
            ? ResumenHorario(new TimeSpan(8, 0, 0), new TimeSpan(17, 0, 0), almuerzo.Value, reposiciones, fichadas)
            : ResumenHorario(null, null, 0, reposiciones, fichadas);

    private static async Task<ResumenHorasDto> ResumenHorario(
        TimeSpan? entrada,
        TimeSpan? salida,
        int almuerzo,
        int[]? reposiciones,
        params DateTime[] fichadas)
    {
        await using var context = Contexto();
        context.Empleado.Add(Empleado(1, null));
        if (entrada.HasValue && salida.HasValue)
        {
            var turno = Turno(entrada.Value, salida.Value, almuerzo);
            context.Turno.Add(turno);
            context.EmpleadoTurno.Add(new EmpleadoTurno { EmpleadoId = 1, Turno = turno, FechaInicio = Dia });
        }

        context.Fichada.AddRange(fichadas.Select((hora, i) => new Fichada
        {
            Id = i + 1,
            EmpleadoId = 1,
            FechaHora = hora,
            TipoRegistro = "SinTipo"
        }));
        if (reposiciones != null)
        {
            context.ReposicionHoras.AddRange(reposiciones.Where(m => m > 0).Select(minutos => new ReposicionHoras
            {
                EmpleadoId = 1,
                Fecha = Dia,
                Minutos = minutos,
                Detalle = "Reposición",
                CreadoPorNombre = "Admin",
                CreadoEn = DateTime.UtcNow
            }));
        }

        await context.SaveChangesAsync();
        var resumen = await new JornadaService(context).ObtenerResumenAsync(1, 1, Dia, Dia);
        Assert.NotNull(resumen);
        return resumen;
    }

    private static AppDbContext Contexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options);
    }

    private static Empleado Empleado(int id, string? horario) => new()
    {
        Id = id,
        EmpresaId = 1,
        DNI = $"DNI-{id}",
        CUIL = $"CUIL-{id}",
        Nombre = "Ada",
        Apellido = "Lovelace",
        Horario = horario,
        Activo = true
    };

    private static Turno Turno(TimeSpan entrada, TimeSpan salida, int almuerzo) => new()
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
                DiaSemana = (int)Dia.DayOfWeek,
                HoraEntrada = entrada,
                HoraSalida = salida,
                MinutosAlmuerzo = almuerzo
            }
        ]
    };
}
