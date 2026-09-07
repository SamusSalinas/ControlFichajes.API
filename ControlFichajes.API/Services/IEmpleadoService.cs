using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Services
{
    public interface IEmpleadoService
    {
        Task<IEnumerable<EmpleadoDto>> ObtenerTodosActivosAsync();
        Task<IEnumerable<EmpleadoDto>> ObtenerActivosPorEmpresaAsync(int empresaId);
        Task<EmpleadoDto?> ObtenerPorIdAsync(int id);
        Task<EmpleadoDto> CrearAsync(EmpleadoRegistroDto dto);
        Task<EmpleadoDto?> ActualizarAsync(int id, int empresaId, EmpleadoPatchDto dto);
        Task<bool> BorradoLogicoAsync(int id, int empresaId);

        Task<bool> EnrolarHuellaAsync(HuellaEnrolarDto huellaDto, int empresaId);
    }
}