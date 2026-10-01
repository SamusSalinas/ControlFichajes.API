using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

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
        ValidarTurno(dto);
        var turno = new Turno
        {
            Nombre = dto.Nombre.Trim(),
            ToleranciaMinutos = dto.ToleranciaMinutos,
            EmpresaId = empresaId,
            Activo = true,
            Dias = MapearDias(dto.Dias)
        };

        _context.Turno.Add(turno);
        await _context.SaveChangesAsync();

        return MapearDto(turno);
    }

    public async Task<bool> ActualizarAsync(int id, int empresaId, TurnoCrearDto dto)
    {
        ValidarTurno(dto);
        var turno = await _context.Turno
            .Include(t => t.Dias)
            .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId && t.Activo);

        if (turno == null)
            return false;

        turno.Nombre = dto.Nombre.Trim();
        turno.ToleranciaMinutos = dto.ToleranciaMinutos;

        _context.TurnoDia.RemoveRange(turno.Dias);
        turno.Dias = MapearDias(dto.Dias, turno.Id);

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
                HoraSalida = d.HoraSalida.ToString(@"hh\:mm"),
                MinutosAlmuerzo = d.MinutosAlmuerzo
            }).ToList()
        };
    }

    private static List<TurnoDia> MapearDias(IEnumerable<TurnoDiaDto> dias, int? turnoId = null)
    {
        return dias.Select(d => new TurnoDia
        {
            TurnoId = turnoId ?? 0,
            DiaSemana = d.DiaSemana,
            HoraEntrada = TimeSpan.Parse(d.HoraEntrada, CultureInfo.InvariantCulture),
            HoraSalida = TimeSpan.Parse(d.HoraSalida, CultureInfo.InvariantCulture),
            MinutosAlmuerzo = 60
        }).ToList();
    }

    private static void ValidarTurno(TurnoCrearDto? dto)
    {
        if (dto == null)
            throw new ArgumentException("El cuerpo de la solicitud es obligatorio.");
        if (string.IsNullOrWhiteSpace(dto.Nombre))
            throw new ArgumentException("El nombre del turno es obligatorio.");
        if (dto.ToleranciaMinutos is < 0 or > 1440)
            throw new ArgumentException("La tolerancia debe estar entre 0 y 1440 minutos.");
        if (dto.Dias == null || dto.Dias.Count == 0)
            throw new ArgumentException("El turno debe contener al menos un día asignado.");
        if (dto.Dias.Any(d => d.DiaSemana is < 0 or > 6))
            throw new ArgumentException("DiaSemana debe estar entre 0 (domingo) y 6 (sábado).");
        if (dto.Dias.Select(d => d.DiaSemana).Distinct().Count() != dto.Dias.Count)
            throw new ArgumentException("No puede haber más de un horario para el mismo día de semana.");

        foreach (var dia in dto.Dias)
        {
            if (!TimeSpan.TryParse(dia.HoraEntrada, CultureInfo.InvariantCulture, out var entrada) ||
                !TimeSpan.TryParse(dia.HoraSalida, CultureInfo.InvariantCulture, out var salida) ||
                entrada < TimeSpan.Zero || salida > TimeSpan.FromDays(1) ||
                salida - entrada < TimeSpan.FromMinutes(60))
                throw new ArgumentException("Cada día requiere horas válidas del mismo día y una duración de al menos 60 minutos.");
        }
    }
}
