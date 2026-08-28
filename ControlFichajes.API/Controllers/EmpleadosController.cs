using ControlFichajes.API.Models;
using ControlFichajes.API.Services;
using ControlFichajes.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
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
                if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
                    return Forbid();

                var resultado = await _empleadoService.EnrolarHuellaAsync(huellaDto, empresaId);

                if (!resultado)
                    return NotFound(new { mensaje = "Empleado no encontrado." });

                return Ok(new { mensaje = "Huella enrolada exitosamente." });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
            {
                // Obtiene el mensaje directo que devolvió la Base de Datos (MySQL / PostgreSQL)
                var dbError = dbEx.InnerException != null ? dbEx.InnerException.Message : dbEx.Message;
                return BadRequest(new { mensaje = "Error en la BD al guardar la huella.", detalle = dbError });
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
            if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
                return Forbid();

            var empleados = await _empleadoService.ObtenerActivosPorEmpresaAsync(empresaId);
            return Ok(empleados);
        }

        [HttpGet("empresa/{empresaId:int}")]
        public async Task<ActionResult<IEnumerable<Empleado>>> GetEmpleadosPorEmpresa(int empresaId)
        {
            if (!EmpresaAccess.PerteneceAUsuario(User, empresaId))
                return Forbid();

            var empleados = await _empleadoService.ObtenerActivosPorEmpresaAsync(empresaId);
            return Ok(empleados);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Empleado>> GetEmpleado(int id)
        {
            var empleado = await _empleadoService.ObtenerPorIdAsync(id);
            if (empleado == null) return NotFound("Empleado no encontrado o inactivo.");
            if (!EmpresaAccess.PerteneceAUsuario(User, empleado.EmpresaId))
                return Forbid();
            
            return Ok(empleado);
        }

        [HttpPost]
        public async Task<ActionResult<Empleado>> PostEmpleado(EmpleadoRegistroDto dto)
        {
            if (!EmpresaAccess.PerteneceAUsuario(User, dto.EmpresaId))
                return Forbid();

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
            if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
                return Forbid();

            var resultado = await _empleadoService.BorradoLogicoAsync(id, empresaId);
            if (!resultado) return NotFound("Empleado no encontrado.");
            
            return Ok(new { mensaje = "Empleado dado de baja exitosamente." });
        }
    }
}