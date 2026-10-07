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

    public async Task<IEnumerable<TurnoDto>> ObtenerPorEmpresaAsync(int empresaId)
    {
        var turnos = await _context.Turno
            .AsNoTracking()
            .Include(t => t.Dias)
            .Where(t => t.EmpresaId == empresaId && t.Activo)
            .ToListAsync();

        return turnos.Select(t => t.ToDto()).ToList();
    }

    public async Task<TurnoDto?> ObtenerPorIdAsync(int id, int empresaId)
    {
        var turno = await _context.Turno
            .AsNoTracking()
            .Include(t => t.Dias)
            .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId && t.Activo);

        return turno?.ToDto();
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

        return (turno.ToDto(), null);
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

        _context.TurnoDia.RemoveRange(turno.Dias);
        turno.Dias = dto.Dias.ToEntityList(turno.Id);

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Exito, string? Error)> EliminarAsync(int id, int empresaId)
    {
        var turno = await _context.Turno
            .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId && t.Activo);

        if (turno == null) return (false, "Turno no encontrado.");

        turno.Activo = false;
        await _context.SaveChangesAsync();
        return (true, null);
    }
}