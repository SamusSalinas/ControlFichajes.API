using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public sealed class JornadaClasificacionConsulta
{
    private readonly AppDbContext _context;
    private readonly ILaborClock _clock;

    public JornadaClasificacionConsulta(AppDbContext context, ILaborClock? clock = null)
    {
        _context = context;
        _clock = clock ?? new SystemLaborClock();
    }

    public DateTime Ahora() => LaborTime.Ahora(_clock);

    public async Task<IReadOnlyDictionary<int, ClasificacionFichada>> ClasificarEmpresaAsync(
        int empresaId,
        IReadOnlyCollection<int> empleadoIds,
        DateTime desde,
        DateTime hasta,
        CancellationToken cancellationToken = default)
    {
        if (empleadoIds.Count == 0)
            return new Dictionary<int, ClasificacionFichada>();

        var inicio = LaborTime.Pared(desde).Date.AddDays(-1);
        var fin = LaborTime.Pared(hasta).Date.AddDays(2);
        var fichadas = await _context.Fichada
            .AsNoTracking()
            .Where(f => empleadoIds.Contains(f.EmpleadoId) &&
                        f.Empleado != null &&
                        f.Empleado.EmpresaId == empresaId &&
                        f.FechaHora >= inicio &&
                        f.FechaHora < fin)
            .Select(f => new FichadaParaClasificar(f.Id, f.EmpleadoId, empresaId, f.FechaHora, f.EsManual))
            .ToListAsync(cancellationToken);
        var horario = await CargarHorariosAsync(empresaId, empleadoIds, inicio, fin, cancellationToken);
        return JornadaClasificador.Clasificar(fichadas, Ahora(), horario)
            .ToDictionary(c => c.Id);
    }

    public async Task<Func<int, DateOnly, HorarioPrevisto?>> CargarHorariosAsync(
        int empresaId,
        IReadOnlyCollection<int> empleadoIds,
        DateTime desde,
        DateTime hasta,
        CancellationToken cancellationToken = default)
    {
        var asignaciones = await _context.EmpleadoTurno
            .AsNoTracking()
            .Include(a => a.Turno)!.ThenInclude(t => t!.Dias)
            .Where(a => empleadoIds.Contains(a.EmpleadoId) &&
                        a.Empleado!.EmpresaId == empresaId &&
                        a.FechaInicio <= hasta &&
                        (!a.FechaFin.HasValue || a.FechaFin.Value >= desde))
            .ToListAsync(cancellationToken);
        var excepciones = await _context.EmpleadoJornadaExcepcion
            .AsNoTracking()
            .Where(e => empleadoIds.Contains(e.EmpleadoId) && e.Fecha >= desde && e.Fecha <= hasta)
            .ToListAsync(cancellationToken);
        var legados = await _context.Empleado
            .AsNoTracking()
            .Where(e => empleadoIds.Contains(e.Id) && e.EmpresaId == empresaId)
            .Select(e => new { e.Id, e.Horario })
            .ToListAsync(cancellationToken);

        return (empleadoId, fecha) =>
        {
            var dia = fecha.ToDateTime(TimeOnly.MinValue);
            var excepcion = excepciones.FirstOrDefault(e => e.EmpleadoId == empleadoId && e.Fecha.Date == dia.Date);
            if (excepcion != null)
                return HorarioDeExcepcion(excepcion);

            var asignacion = asignaciones
                .Where(a => a.EmpleadoId == empleadoId &&
                            a.FechaInicio.Date <= dia.Date &&
                            (!a.FechaFin.HasValue || a.FechaFin.Value.Date >= dia.Date))
                .OrderByDescending(a => a.FechaInicio)
                .FirstOrDefault();
            if (asignacion?.Turno != null)
            {
                var turnoDia = asignacion.Turno.Dias.FirstOrDefault(d => d.DiaSemana == (int)dia.DayOfWeek);
                if (turnoDia == null)
                    return null;
                return new HorarioPrevisto(
                    turnoDia.HoraEntrada,
                    turnoDia.HoraSalida,
                    asignacion.Turno.ToleranciaMinutos,
                    turnoDia.MinutosAlmuerzo);
            }

            var legado = legados.FirstOrDefault(e => e.Id == empleadoId)?.Horario;
            return HorarioLaboral.TryParseLegado(legado, out var previsto) ? previsto : null;
        };
    }

    private static HorarioPrevisto? HorarioDeExcepcion(EmpleadoJornadaExcepcion excepcion)
    {
        if (excepcion.EsDiaLibre || excepcion.HoraEntrada == null || excepcion.HoraSalida == null)
            return null;
        return new HorarioPrevisto(
            excepcion.HoraEntrada.Value,
            excepcion.HoraSalida.Value,
            excepcion.ToleranciaMinutos ?? 0);
    }
}
