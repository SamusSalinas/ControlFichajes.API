using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Services
{
    public enum ReactivarEmpleadoEstado
    {
        NoEncontrado,
        YaActivo,
        Reactivado
    }

    public interface IEmpleadoService
    {
        Task<IEnumerable<EmpleadoDto>> ObtenerTodosActivosAsync();
        Task<IEnumerable<EmpleadoDto>> ObtenerActivosPorEmpresaAsync(int empresaId);
        Task<IEnumerable<EmpleadoDto>> ObtenerPorEmpresaAsync(int empresaId, bool incluirInactivos = false);
        Task<EmpleadoDto?> ObtenerPorIdAsync(int id);
        Task<EmpleadoDto> CrearAsync(EmpleadoRegistroDto dto);
        Task<EmpleadoDto?> ActualizarAsync(int id, int empresaId, EmpleadoPatchDto dto);
        Task<bool> BorradoLogicoAsync(int id, int empresaId);
        Task<ReactivarEmpleadoEstado> ReactivarAsync(int id, int empresaId);

        Task<bool> EnrolarHuellaAsync(HuellaEnrolarDto huellaDto, int empresaId);
    }
}
