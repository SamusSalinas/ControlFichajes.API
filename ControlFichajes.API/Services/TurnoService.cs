using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Extensions;
using ControlFichajes.API.Security;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public class TurnoService : ITurnoService
{
    private readonly AppDbContext _context;
    private readonly ITurnoValidacionRules _validador;

    public TurnoService(AppDbContext context, ITurnoValidacionRules validador)
    {
        _context = context;
        _validador = validador;
    }

    public Task<IEnumerable<TurnoDto>> ObtenerPorEmpresaAsync(int empresaId)
    {
        return ObtenerPorEmpresaAsync(empresaId, LaborTime.DesdeUtc(DateTime.UtcNow));
    }

    public async Task<IEnumerable<TurnoDto>> ObtenerPorEmpresaAsync(int empresaId, DateTime fecha)
    {
        var turnos = await _context.Turno
            .AsNoTracking()
            .Include(t => t.Dias)
            .Where(t => t.EmpresaId == empresaId && t.Activo)
            .ToListAsync();

        var conteos = await ContarAsignacionesVigentesAsync(empresaId, fecha);
        return turnos.Select(turno =>
        {
            var dto = turno.ToDto();
            dto.CantidadEmpleadosAsignados = conteos.GetValueOrDefault(turno.Id);
            return dto;
        }).ToList();
    }

    public async Task<TurnoDto?> ObtenerPorIdAsync(int id, int empresaId)
    {
        var turno = await _context.Turno
            .AsNoTracking()
            .Include(t => t.Dias)
            .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId && t.Activo);

        if (turno == null)
            return null;

        var dto = turno.ToDto();
        var conteos = await ContarAsignacionesVigentesAsync(empresaId, LaborTime.DesdeUtc(DateTime.UtcNow));
        dto.CantidadEmpleadosAsignados = conteos.GetValueOrDefault(turno.Id);
        return dto;
    }

    public async Task<IReadOnlyList<TurnoEmpleadoAsignadoDto>?> ObtenerEmpleadosAsync(
        int turnoId,
        int empresaId,
        DateTime fecha)
    {
        var existe = await _context.Turno.AsNoTracking().AnyAsync(t =>
            t.Id == turnoId && t.EmpresaId == empresaId && t.Activo);
        if (!existe)
            return null;

        var hoy = fecha.Date;
        var asignaciones = await _context.EmpleadoTurno
            .AsNoTracking()
            .Where(a => a.TurnoId == turnoId &&
                        a.Empleado!.EmpresaId == empresaId &&
                        a.Empleado.Activo)
            .Select(a => new
            {
                a.EmpleadoId,
                a.Empleado!.Nombre,
                a.Empleado.Apellido,
                a.Empleado.Legajo,
                a.FechaInicio,
                a.FechaFin
            })
            .ToListAsync();

        return asignaciones
            .Where(a => EsVigente(a.FechaInicio, a.FechaFin, hoy))
            .GroupBy(a => a.EmpleadoId)
            .Select(grupo => grupo.OrderByDescending(a => a.FechaInicio).First())
            .OrderBy(a => a.Apellido)
            .ThenBy(a => a.Nombre)
            .Select(a => new TurnoEmpleadoAsignadoDto
            {
                EmpleadoId = a.EmpleadoId,
                NombreCompleto = $"{a.Nombre} {a.Apellido}".Trim(),
                Legajo = a.Legajo,
                FechaInicio = a.FechaInicio.Date,
                FechaFin = a.FechaFin?.Date
            })
            .ToList();
    }

    private async Task<Dictionary<int, int>> ContarAsignacionesVigentesAsync(int empresaId, DateTime fecha)
    {
        var hoy = fecha.Date;
        var asignaciones = await _context.EmpleadoTurno
            .AsNoTracking()
            .Where(a => a.Turno!.EmpresaId == empresaId &&
                        a.Turno.Activo &&
                        a.Empleado!.EmpresaId == empresaId &&
                        a.Empleado.Activo)
            .Select(a => new { a.TurnoId, a.EmpleadoId, a.FechaInicio, a.FechaFin })
            .ToListAsync();

        return asignaciones
            .Where(a => EsVigente(a.FechaInicio, a.FechaFin, hoy))
            .GroupBy(a => a.TurnoId)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Select(a => a.EmpleadoId).Distinct().Count());
    }

    private static bool EsVigente(DateTime inicio, DateTime? fin, DateTime hoy)
    {
        return inicio.Date <= hoy.Date && (!fin.HasValue || fin.Value.Date >= hoy.Date);
    }

    public async Task<(TurnoDto? Turno, string? Error)> CrearAsync(int empresaId, TurnoCrearDto dto)
    {
        var error = _validador.Validar(dto);
        if (error != null) return (null, error);

        var turno = new Turno
        {
            Nombre = dto.Nombre.Trim(),
            ToleranciaMinutos = dto.ToleranciaMinutos,
            EmpresaId = empresaId,
            Activo = true,
            Dias = dto.Dias.ToEntityList()
        };

        _context.Turno.Add(turno);
        await _context.SaveChangesAsync();

        var creado = turno.ToDto();
        creado.CantidadEmpleadosAsignados = 0;
        return (creado, null);
    }

    public async Task<(bool Exito, string? Error)> ActualizarAsync(int id, int empresaId, TurnoCrearDto dto)
    {
        var error = _validador.Validar(dto);
        if (error != null) return (false, error);

        var turno = await _context.Turno
            .Include(t => t.Dias)
            .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId && t.Activo);

        if (turno == null) return (false, "Turno no encontrado.");

        turno.Nombre = dto.Nombre.Trim();
        turno.ToleranciaMinutos = dto.ToleranciaMinutos;

        var reemplazo = dto.Dias.ToEntityList(turno.Id);
        turno.Dias.Clear();
        foreach (var dia in reemplazo)
            turno.Dias.Add(dia);

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Exito, string? Error)> EliminarAsync(int id, int empresaId)
    {
        var turno = await _context.Turno
            .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId && t.Activo);

        if (turno == null) return (false, "Turno no encontrado.");

        var asignado = await _context.EmpleadoTurno.AnyAsync(a => a.TurnoId == id);
        if (asignado)
            return (false, "No se puede eliminar el turno porque tiene asignaciones de empleados.");

        turno.Activo = false;
        await _context.SaveChangesAsync();
        return (true, null);
    }
}