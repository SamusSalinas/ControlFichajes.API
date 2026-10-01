using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Services;

public interface ITurnoService
{
    Task<IEnumerable<TurnoDto>> ObtenerPorEmpresaAsync(int empresaId);
    Task<TurnoDto?> ObtenerPorIdAsync(int id, int empresaId);
    Task<TurnoDto> CrearAsync(int empresaId, TurnoCrearDto dto);
    Task<bool> ActualizarAsync(int id, int empresaId, TurnoCrearDto dto);
    Task<bool> EliminarAsync(int id, int empresaId);
}
