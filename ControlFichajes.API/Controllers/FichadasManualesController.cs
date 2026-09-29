using System.Threading.Tasks;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    // 1. Restricción estricta: RRHH queda bloqueado automáticamente.
    [Authorize(Roles = "SuperAdmin,ADMIN")] 
    public class FichadasManualesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public FichadasManualesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> InsertarFichadaManual([FromBody] FichadaManualCrearDto request)
        {
            // 2. Validación obligatoria del motivo/justificación (Regla de negocio)
            if (string.IsNullOrWhiteSpace(request.Motivo) || string.IsNullOrWhiteSpace(request.Detalle))
            {
                return BadRequest(new { mensaje = "El motivo y el detalle son obligatorios para las fichadas manuales." });
            }

            // (Lógica para validar si el empleado pertenece a la empresa del ADMIN omitida por brevedad)

            var nuevaFichada = new Fichada
            {
                EmpleadoId = request.EmpleadoId,
                FechaHora = request.FechaHora,
                TipoRegistro = request.Tipo, // Corregido: la propiedad del modelo es TipoRegistro
                EsManual = true,
                Estado = "Completada"
            };

            var observacion = new FichadaObservacion
            {
                Motivo = request.Motivo,
                Detalle = request.Detalle,
                Fichada = nuevaFichada
            };

            _context.Fichada.Add(nuevaFichada);
            _context.FichadaObservacion.Add(observacion);
            
            await _context.SaveChangesAsync();

            // Devolver DTO plano
            return Ok(new { mensaje = "Fichada manual registrada correctamente.", id = nuevaFichada.Id });
        }
    }
}