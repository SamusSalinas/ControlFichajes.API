using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;

namespace ControlFichajes.API.Services
{
    public interface IEmpleadoService
    {
        Task<IEnumerable<EmpleadoDto>> ObtenerTodosActivosAsync();
        Task<IEnumerable<EmpleadoDto>> ObtenerActivosPorEmpresaAsync(int empresaId);
        Task<IEnumerable<EmpleadoAgenteDto>> ObtenerCatalogoAgenteAsync(int empresaId, int sucursalId);
        Task<EmpleadoDto?> ObtenerPorIdAsync(int id);
        Task<EmpleadoDto> CrearAsync(EmpleadoRegistroDto dto);
        Task<EmpleadoDto?> ActualizarAsync(int id, int empresaId, EmpleadoPatchDto dto);
        Task<bool> BorradoLogicoAsync(int id, int empresaId);

        Task<bool> EnrolarHuellaAsync(HuellaEnrolarDto huellaDto, int empresaId, int? sucursalId = null);
    }
}
