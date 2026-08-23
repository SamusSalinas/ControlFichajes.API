using ControlFichajes.API.Models;
using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Services
{
    public interface IEmpleadoService
    {
        Task<IEnumerable<Empleado>> ObtenerTodosActivosAsync();
        Task<Empleado?> ObtenerPorIdAsync(int id);
        Task<Empleado> CrearAsync(EmpleadoRegistroDto dto);
        Task<bool> BorradoLogicoAsync(int id);
    }
}