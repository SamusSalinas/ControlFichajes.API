using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Services;

public interface ITurnoService
{
    Task<IEnumerable<TurnoDto>> ObtenerPorEmpresaAsync(int empresaId);
    Task<TurnoDto?> ObtenerPorIdAsync(int id, int empresaId);
    Task<(TurnoDto? Turno, string? Error)> CrearAsync(int empresaId, TurnoCrearDto dto);
    Task<(bool Exito, string? Error)> ActualizarAsync(int id, int empresaId, TurnoCrearDto dto);
    Task<(bool Exito, string? Error)> EliminarAsync(int id, int empresaId);
}
