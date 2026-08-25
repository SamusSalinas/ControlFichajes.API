using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using ControlFichajes.API.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmpleadosController : ControllerBase
    {
        // Inyectamos el servicio, igual que hiciste con tu ITransaccionService anterior
        private readonly IEmpleadoService _empleadoService;

        public EmpleadosController(IEmpleadoService empleadoService)
        {
            _empleadoService = empleadoService;
        }

        // POST: api/Empleados/enrolar
        [HttpPost("enrolar")]
        public async Task<IActionResult> EnrolarEmpleado([FromBody] HuellaEnrolarDto huellaDto)
        {
            try
            {
                var resultado = await _empleadoService.EnrolarHuellaAsync(huellaDto);

                if (!resultado)
                    return NotFound(new { mensaje = "Empleado no encontrado." });

                return Ok(new { mensaje = "Huella enrolada exitosamente." });
            }
            catch (Exception ex)
            {
                // Por si el Base64 viene mal formado u otro error
                return BadRequest(new { mensaje = "Error al procesar la huella.", detalle = ex.Message });
            }
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Empleado>>> GetEmpleados()
        {
            var empleados = await _empleadoService.ObtenerTodosActivosAsync();
            return Ok(empleados);
        }

        [HttpGet("empresa/{empresaId:int}")]
        public async Task<ActionResult<IEnumerable<Empleado>>> GetEmpleadosPorEmpresa(int empresaId)
        {
            var empleados = await _empleadoService.ObtenerActivosPorEmpresaAsync(empresaId);
            return Ok(empleados);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Empleado>> GetEmpleado(int id)
        {
            var empleado = await _empleadoService.ObtenerPorIdAsync(id);
            if (empleado == null) return NotFound("Empleado no encontrado o inactivo.");
            
            return Ok(empleado);
        }

        [HttpPost]
        public async Task<ActionResult<Empleado>> PostEmpleado(EmpleadoRegistroDto dto)
        {
            try
            {
                // El servicio intenta crear el empleado validando que el DNI no exista
                var nuevoEmpleado = await _empleadoService.CrearAsync(dto);
                
                // Devuelve un código 201 Created y la ruta para ver el nuevo recurso
                return CreatedAtAction(nameof(GetEmpleado), new { id = nuevoEmpleado.Id }, nuevoEmpleado);
            }
            catch (Exception ex)
            {
                // Si el DNI ya existe, el servicio lanza un error y el controlador devuelve un 400 Bad Request
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteEmpleado(int id)
        {
            var resultado = await _empleadoService.BorradoLogicoAsync(id);
            if (!resultado) return NotFound("Empleado no encontrado.");
            
            return Ok(new { mensaje = "Empleado dado de baja exitosamente." });
        }
    }
}