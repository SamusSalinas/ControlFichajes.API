using System.Globalization;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public class JornadaService
{
    private readonly AppDbContext _context;

    public JornadaService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<TurnoAsignacionDto>?> ObtenerAsignacionesAsync(int empleadoId, int empresaId)
    {
        if (!await EmpleadoExisteAsync(empleadoId, empresaId))
            return null;

        return await _context.EmpleadoTurno
            .AsNoTracking()
            .Where(a => a.EmpleadoId == empleadoId && a.Empleado!.EmpresaId == empresaId)
            .OrderByDescending(a => a.FechaInicio)
            .Select(a => new TurnoAsignacionDto
            {
                Id = a.Id,
                TurnoId = a.TurnoId,
                NombreTurno = a.Turno!.Nombre,
                FechaInicio = a.FechaInicio,
                FechaFin = a.FechaFin
            })
            .ToListAsync();
    }

    public async Task<TurnoAsignacionDto?> AsignarTurnoAsync(
        int empleadoId,
        int empresaId,
        TurnoAsignacionCrearDto dto)
    {
        var inicio = dto.FechaInicio.Date;
        var fin = dto.FechaFin?.Date;
        if (fin.HasValue && fin.Value < inicio)
            throw new ArgumentException("FechaFin debe ser igual o posterior a FechaInicio.");

        var empleado = await _context.Empleado.FirstOrDefaultAsync(e =>
            e.Id == empleadoId && e.EmpresaId == empresaId && e.Activo);
        var turnoValido = await _context.Turno.AnyAsync(t =>
            t.Id == dto.TurnoId && t.EmpresaId == empresaId && t.Activo);
        if (empleado == null || !turnoValido)
            return null;

        var asignacionesEnConflicto = await _context.EmpleadoTurno
            .Where(a => a.EmpleadoId == empleadoId &&
                        a.FechaInicio <= (fin ?? DateTime.MaxValue) &&
                        (!a.FechaFin.HasValue || a.FechaFin.Value >= inicio))
            .ToListAsync();

        foreach (var anterior in asignacionesEnConflicto)
        {
            if (anterior.FechaInicio.Date == inicio)
            {
                anterior.TurnoId = dto.TurnoId;
                anterior.FechaFin = fin;
                empleado.TurnoId = dto.TurnoId;
                await _context.SaveChangesAsync();
                return await ObtenerAsignacionPorIdAsync(anterior.Id);
            }

            if (anterior.FechaInicio.Date < inicio &&
                (!anterior.FechaFin.HasValue || anterior.FechaFin.Value.Date >= inicio))
            {
                if (fin.HasValue && anterior.FechaFin.HasValue && anterior.FechaFin.Value.Date > fin.Value)
                    throw new InvalidOperationException("La nueva asignación se cruza con una asignación futura.");
                anterior.FechaFin = inicio.AddDays(-1);
                continue;
            }

            throw new InvalidOperationException("La asignación se cruza con otra asignación futura.");
        }

        var asignacion = new EmpleadoTurno
        {
            EmpleadoId = empleadoId,
            TurnoId = dto.TurnoId,
            FechaInicio = inicio,
            FechaFin = fin
        };
        empleado.TurnoId = dto.TurnoId;
        _context.EmpleadoTurno.Add(asignacion);
        await _context.SaveChangesAsync();
        return await ObtenerAsignacionPorIdAsync(asignacion.Id);
    }

    public async Task<List<JornadaDiaDto>?> ObtenerCalendarioAsync(
        int empleadoId,
        int empresaId,
        DateTime desde,
        DateTime hasta)
    {
        ValidarRango(desde, hasta);
        if (!await EmpleadoExisteAsync(empleadoId, empresaId))
            return null;

        var inicio = desde.Date;
        var fin = hasta.Date;
        var asignaciones = await _context.EmpleadoTurno
            .AsNoTracking()
            .Include(a => a.Turno)
                .ThenInclude(t => t!.Dias)
            .Where(a => a.EmpleadoId == empleadoId &&
                        a.FechaInicio <= fin &&
                        (!a.FechaFin.HasValue || a.FechaFin.Value >= inicio))
            .ToListAsync();
        var excepciones = await _context.EmpleadoJornadaExcepcion
            .AsNoTracking()
            .Where(e => e.EmpleadoId == empleadoId && e.Fecha >= inicio && e.Fecha <= fin)
            .ToDictionaryAsync(e => e.Fecha.Date);

        var resultado = new List<JornadaDiaDto>();
        for (var fecha = inicio; fecha <= fin; fecha = fecha.AddDays(1))
        {
            var asignacion = asignaciones
                .Where(a => a.FechaInicio.Date <= fecha && (!a.FechaFin.HasValue || a.FechaFin.Value.Date >= fecha))
                .OrderByDescending(a => a.FechaInicio)
                .FirstOrDefault();
            if (excepciones.TryGetValue(fecha, out var excepcion))
            {
                resultado.Add(MapearExcepcion(
                    excepcion,
                    excepcion.ToleranciaMinutos ?? asignacion?.Turno?.ToleranciaMinutos ?? 0,
                    asignacion != null));
                continue;
            }

            if (asignacion == null)
            {
                resultado.Add(new JornadaDiaDto { Fecha = fecha });
                continue;
            }

            if (asignacion.Turno!.Dias.Count == 0)
            {
                resultado.Add(new JornadaDiaDto { Fecha = fecha, TieneTurnoAsignado = true });
                continue;
            }

            var diaTurno = asignacion.Turno.Dias.FirstOrDefault(d => d.DiaSemana == (int)fecha.DayOfWeek);
            resultado.Add(diaTurno == null
                ? new JornadaDiaDto { Fecha = fecha, TieneTurnoAsignado = true, TieneHorarioConfigurado = true, EsDiaLibre = true }
                : MapearDiaTurno(fecha, diaTurno, asignacion.Turno.ToleranciaMinutos));
        }

        return resultado;
    }

    public async Task<bool?> GuardarExcepcionAsync(
        int empleadoId,
        int empresaId,
        DateTime fecha,
        JornadaExcepcionEscribirDto dto)
    {
        if (!await EmpleadoExisteAsync(empleadoId, empresaId))
            return null;

        var jornada = CrearExcepcion(empleadoId, fecha.Date, dto);
        var existente = await _context.EmpleadoJornadaExcepcion
            .FirstOrDefaultAsync(e => e.EmpleadoId == empleadoId && e.Fecha == fecha.Date);

        if (existente == null)
        {
            _context.EmpleadoJornadaExcepcion.Add(jornada);
        }
        else
        {
            existente.EsDiaLibre = jornada.EsDiaLibre;
            existente.ToleranciaMinutos = jornada.ToleranciaMinutos;
            existente.HoraEntrada = jornada.HoraEntrada;
            existente.HoraSalida = jornada.HoraSalida;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool?> EliminarExcepcionAsync(int empleadoId, int empresaId, DateTime fecha)
    {
        if (!await EmpleadoExisteAsync(empleadoId, empresaId))
            return null;

        var excepcion = await _context.EmpleadoJornadaExcepcion
            .FirstOrDefaultAsync(e => e.EmpleadoId == empleadoId && e.Fecha == fecha.Date);
        if (excepcion == null)
            return false;

        _context.EmpleadoJornadaExcepcion.Remove(excepcion);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<ReposicionHorasDto?> RegistrarReposicionAsync(
        int empleadoId,
        int empresaId,
        DateTime fecha,
        ReposicionHorasCrearDto dto,
        int? usuarioId,
        string usuarioNombre)
    {
        if (!await EmpleadoExisteAsync(empleadoId, empresaId))
            return null;
        if (dto.Minutos is < 1 or > 1440)
            throw new ArgumentException("Minutos debe estar entre 1 y 1440.");
        var detalle = dto.Detalle.Trim();
        if (detalle.Length == 0 || detalle.Length > 500)
            throw new ArgumentException("Detalle es obligatorio y no puede superar 500 caracteres.");

        var reposicion = new ReposicionHoras
        {
            EmpleadoId = empleadoId,
            Fecha = fecha.Date,
            Minutos = dto.Minutos,
            Detalle = detalle,
            CreadoPorUsuarioId = usuarioId,
            CreadoPorNombre = usuarioNombre,
            CreadoEn = DateTime.UtcNow
        };
        _context.ReposicionHoras.Add(reposicion);
        await _context.SaveChangesAsync();
        return MapearReposicion(reposicion);
    }

    public async Task<List<ReposicionHorasDto>?> ObtenerReposicionesAsync(
        int empleadoId,
        int empresaId,
        DateTime desde,
        DateTime hasta)
    {
        ValidarRango(desde, hasta);
        if (!await EmpleadoExisteAsync(empleadoId, empresaId))
            return null;

        return await _context.ReposicionHoras
            .AsNoTracking()
            .Where(r => r.EmpleadoId == empleadoId && r.Fecha >= desde.Date && r.Fecha <= hasta.Date)
            .OrderBy(r => r.Fecha)
            .Select(r => new ReposicionHorasDto
            {
                Id = r.Id,
                Fecha = r.Fecha,
                Minutos = r.Minutos,
                Detalle = r.Detalle,
                CreadoPor = r.CreadoPorNombre,
                CreadoEn = r.CreadoEn
            })
            .ToListAsync();
    }

    public async Task<ResumenHorasDto?> ObtenerResumenAsync(
        int empleadoId,
        int empresaId,
        DateTime desde,
        DateTime hasta)
    {
        var calendario = await ObtenerCalendarioAsync(empleadoId, empresaId, desde, hasta);
        if (calendario == null)
            return null;

        var inicio = desde.Date;
        var finExclusivo = hasta.Date.AddDays(1);
        var fichadas = await _context.Fichada
            .AsNoTracking()
            .Where(f => f.EmpleadoId == empleadoId && f.FechaHora >= inicio && f.FechaHora < finExclusivo)
            .OrderBy(f => f.FechaHora)
            .ToListAsync();
        var reposiciones = await _context.ReposicionHoras
            .AsNoTracking()
            .Where(r => r.EmpleadoId == empleadoId && r.Fecha >= inicio && r.Fecha < finExclusivo)
            .SumAsync(r => (int?)r.Minutos) ?? 0;
        var fichadasPorDia = fichadas.GroupBy(f => f.FechaHora.Date).ToDictionary(g => g.Key, g => g.ToList());
        var resumen = new ResumenHorasDto
        {
            EmpleadoId = empleadoId,
            Desde = inicio,
            Hasta = hasta.Date
        };

        foreach (var jornada in calendario)
        {
            fichadasPorDia.TryGetValue(jornada.Fecha.Date, out var eventos);
            var (trabajados, incompleta) = CalcularMinutosTrabajados(eventos ?? []);
            var minutosTarde = CalcularMinutosTarde(jornada, eventos ?? []);
            var dia = new ResumenHorasDiaDto
            {
                Fecha = jornada.Fecha,
                MinutosProgramados = jornada.MinutosProgramados,
                MinutosTrabajados = trabajados,
                MinutosTarde = minutosTarde,
                MinutosSaldo = trabajados - jornada.MinutosProgramados,
                Incompleta = incompleta
            };
            resumen.Dias.Add(dia);
            resumen.MinutosProgramados += dia.MinutosProgramados;
            resumen.MinutosTrabajados += dia.MinutosTrabajados;
        }

        resumen.MinutosRepuestos = reposiciones;
        resumen.SaldoMinutos = resumen.MinutosTrabajados - resumen.MinutosProgramados + reposiciones;
        return resumen;
    }

    private async Task<bool> EmpleadoExisteAsync(int empleadoId, int empresaId)
    {
        return await _context.Empleado.AnyAsync(e =>
            e.Id == empleadoId && e.EmpresaId == empresaId && e.Activo);
    }

    private async Task<TurnoAsignacionDto> ObtenerAsignacionPorIdAsync(int id)
    {
        return await _context.EmpleadoTurno.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new TurnoAsignacionDto
            {
                Id = a.Id,
                TurnoId = a.TurnoId,
                NombreTurno = a.Turno!.Nombre,
                FechaInicio = a.FechaInicio,
                FechaFin = a.FechaFin
            })
            .FirstAsync();
    }

    private static void ValidarRango(DateTime desde, DateTime hasta)
    {
        if (desde.Date > hasta.Date || (hasta.Date - desde.Date).TotalDays > 365)
            throw new ArgumentException("El rango de fechas debe ser válido y no superar 366 días.");
    }

    private static JornadaDiaDto MapearDiaTurno(DateTime fecha, TurnoDia dia, int tolerancia)
    {
        return new JornadaDiaDto
        {
            Fecha = fecha,
            TieneTurnoAsignado = true,
            TieneHorarioConfigurado = true,
            EsDiaLibre = false,
            ToleranciaMinutos = tolerancia,
            MinutosAlmuerzo = 60,
            HoraEntrada = FormatearHora(dia.HoraEntrada),
            HoraSalida = FormatearHora(dia.HoraSalida),
            MinutosProgramados = Math.Max(0, (int)(dia.HoraSalida - dia.HoraEntrada).TotalMinutes - 60)
        };
    }

    private static JornadaDiaDto MapearExcepcion(EmpleadoJornadaExcepcion excepcion, int tolerancia, bool tieneTurnoAsignado)
    {
        if (excepcion.EsDiaLibre)
            return new JornadaDiaDto
            {
                Fecha = excepcion.Fecha,
                TieneTurnoAsignado = tieneTurnoAsignado,
                TieneHorarioConfigurado = true,
                EsDiaLibre = true,
                EsExcepcionManual = true
            };

        var entrada = excepcion.HoraEntrada!.Value;
        var salida = excepcion.HoraSalida!.Value;
        return new JornadaDiaDto
        {
            Fecha = excepcion.Fecha,
            TieneTurnoAsignado = tieneTurnoAsignado,
            TieneHorarioConfigurado = true,
            EsDiaLibre = false,
            EsExcepcionManual = true,
            ToleranciaMinutos = tolerancia,
            MinutosAlmuerzo = 60,
            HoraEntrada = FormatearHora(entrada),
            HoraSalida = FormatearHora(salida),
            MinutosProgramados = Math.Max(0, (int)(salida - entrada).TotalMinutes - 60)
        };
    }

    private static EmpleadoJornadaExcepcion CrearExcepcion(
        int empleadoId,
        DateTime fecha,
        JornadaExcepcionEscribirDto dto)
    {
        if (dto.ToleranciaMinutos is < 0 or > 1440)
            throw new ArgumentException("La tolerancia debe estar entre 0 y 1440 minutos.");
        if (dto.EsDiaLibre)
        {
            if (dto.HoraEntrada != null || dto.HoraSalida != null)
                throw new ArgumentException("Un día libre no puede incluir horarios.");
            return new EmpleadoJornadaExcepcion
            {
                EmpleadoId = empleadoId,
                Fecha = fecha,
                EsDiaLibre = true,
                ToleranciaMinutos = dto.ToleranciaMinutos
            };
        }

        if (!TryParseHora(dto.HoraEntrada, out var entrada) ||
            !TryParseHora(dto.HoraSalida, out var salida) ||
            salida - entrada < TimeSpan.FromMinutes(60))
            throw new ArgumentException("El horario debe ser válido, durar al menos una hora y terminar el mismo día.");

        return new EmpleadoJornadaExcepcion
        {
            EmpleadoId = empleadoId,
            Fecha = fecha,
            EsDiaLibre = false,
            ToleranciaMinutos = dto.ToleranciaMinutos,
            HoraEntrada = entrada,
            HoraSalida = salida
        };
    }

    private static (int Minutos, bool Incompleta) CalcularMinutosTrabajados(IReadOnlyList<Fichada> fichadas)
    {
        var minutos = 0;
        Fichada? entradaPendiente = null;
        var incompleta = fichadas.Any(f => f.Estado == "Incompleto");
        foreach (var fichada in fichadas)
        {
            if (fichada.TipoRegistro == "Entrada")
            {
                if (entradaPendiente == null)
                    entradaPendiente = fichada;
                else
                    incompleta = true;
            }
            else if (fichada.TipoRegistro == "Salida" && entradaPendiente != null && fichada.FechaHora > entradaPendiente.FechaHora)
            {
                minutos += (int)(fichada.FechaHora - entradaPendiente.FechaHora).TotalMinutes;
                entradaPendiente = null;
            }
        }

        if (entradaPendiente != null)
        {
            minutos += entradaPendiente.MinutosHastaCorte ?? 0;
            incompleta = true;
        }

        return (minutos, incompleta);
    }

    private static int CalcularMinutosTarde(JornadaDiaDto jornada, IReadOnlyList<Fichada> fichadas)
    {
        if (jornada.EsDiaLibre || string.IsNullOrWhiteSpace(jornada.HoraEntrada))
            return 0;
        var primeraEntrada = fichadas
            .Where(f => f.TipoRegistro == "Entrada")
            .Select(f => (DateTime?)f.FechaHora)
            .Min();
        if (!primeraEntrada.HasValue)
            return 0;
        var horaEsperada = jornada.Fecha.Date.Add(TimeSpan.Parse(jornada.HoraEntrada));
        var tolerancia = TimeSpan.FromMinutes(jornada.ToleranciaMinutos);
        return Math.Max(0, (int)(primeraEntrada.Value - horaEsperada - tolerancia).TotalMinutes);
    }

    private static ReposicionHorasDto MapearReposicion(ReposicionHoras reposicion)
    {
        return new ReposicionHorasDto
        {
            Id = reposicion.Id,
            Fecha = reposicion.Fecha,
            Minutos = reposicion.Minutos,
            Detalle = reposicion.Detalle,
            CreadoPor = reposicion.CreadoPorNombre,
            CreadoEn = reposicion.CreadoEn
        };
    }

    private static bool TryParseHora(string? value, out TimeSpan result)
    {
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out result);
    }

    private static string FormatearHora(TimeSpan hora) => hora.ToString(@"hh\:mm");
}
