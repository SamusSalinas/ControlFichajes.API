using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public class TurnoService : ITurnoService
{
    private readonly AppDbContext _context;

    public TurnoService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<TurnoDto>> ObtenerPorEmpresaAsync(int empresaId)
    {
        var turnos = await _context.Turno
            .AsNoTracking()
            .Include(t => t.Dias)
            .Where(t => t.EmpresaId == empresaId && t.Activo)
            .ToListAsync();

        return turnos.Select(MapearDto).ToList();
    }

    public async Task<TurnoDto?> ObtenerPorIdAsync(int id, int empresaId)
    {
        var turno = await _context.Turno
            .AsNoTracking()
            .Include(t => t.Dias)
            .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId && t.Activo);

        return turno == null ? null : MapearDto(turno);
    }

    public async Task<TurnoDto> CrearAsync(int empresaId, TurnoCrearDto dto)
    {
        var turno = new Turno
        {
            Nombre = dto.Nombre.Trim(),
            ToleranciaMinutos = dto.ToleranciaMinutos,
            EmpresaId = empresaId,
            Activo = true,
            Dias = dto.Dias.Select(d => new TurnoDia
            {
                DiaSemana = d.DiaSemana,
                HoraEntrada = TimeSpan.TryParse(d.HoraEntrada, out var he) ? he : TimeSpan.Zero,
                HoraSalida = TimeSpan.TryParse(d.HoraSalida, out var hs) ? hs : TimeSpan.Zero,
                MinutosAlmuerzo = 60
            }).ToList()
        };

        _context.Turno.Add(turno);
        await _context.SaveChangesAsync();

        return MapearDto(turno);
    }

    public async Task<bool> ActualizarAsync(int id, int empresaId, TurnoCrearDto dto)
    {
        var turno = await _context.Turno
            .Include(t => t.Dias)
            .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId && t.Activo);

        if (turno == null)
            return false;

        turno.Nombre = dto.Nombre.Trim();
        turno.ToleranciaMinutos = dto.ToleranciaMinutos;

        _context.TurnoDia.RemoveRange(turno.Dias);
        turno.Dias = dto.Dias.Select(d => new TurnoDia
        {
            TurnoId = turno.Id,
            DiaSemana = d.DiaSemana,
            HoraEntrada = TimeSpan.TryParse(d.HoraEntrada, out var he) ? he : TimeSpan.Zero,
            HoraSalida = TimeSpan.TryParse(d.HoraSalida, out var hs) ? hs : TimeSpan.Zero,
            MinutosAlmuerzo = 60
        }).ToList();

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> EliminarAsync(int id, int empresaId)
    {
        var turno = await _context.Turno
            .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId && t.Activo);

        if (turno == null)
            return false;

        turno.Activo = false;
        await _context.SaveChangesAsync();
        return true;
    }

    private static TurnoDto MapearDto(Turno turno)
    {
        return new TurnoDto
        {
            Id = turno.Id,
            Nombre = turno.Nombre,
            ToleranciaMinutos = turno.ToleranciaMinutos,
            Dias = turno.Dias.Select(d => new TurnoDiaDto
            {
                DiaSemana = d.DiaSemana,
                HoraEntrada = d.HoraEntrada.ToString(@"hh\:mm"),
                HoraSalida = d.HoraSalida.ToString(@"hh\:mm")
            }).ToList()
        };
    }
}
