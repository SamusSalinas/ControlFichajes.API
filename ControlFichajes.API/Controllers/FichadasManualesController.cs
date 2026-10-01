using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
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
            if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
                return Forbid();
            if (request.EmpleadoId <= 0 || request.FechaHora == default)
                return BadRequest(new { mensaje = "EmpleadoId y FechaHora son obligatorios." });
            if (request.Tipo is not (RegistroTipos.Entrada or RegistroTipos.Salida))
                return BadRequest(new { mensaje = "Tipo debe ser Entrada o Salida." });

            var motivo = request.Motivo.Trim();
            var detalle = request.Detalle.Trim();
            if (!FichadaObservacionMotivos.EsValido(motivo))
                return BadRequest(new { mensaje = "El motivo de la observación no es válido." });
            if (detalle.Length == 0 || detalle.Length > FichadaObservacionMotivos.DetalleMaxLength)
                return BadRequest(new { mensaje = "El detalle es obligatorio y no puede superar 500 caracteres." });

            var empleadoExiste = await _context.Empleado.AnyAsync(e =>
                e.Id == request.EmpleadoId && e.EmpresaId == empresaId && e.Activo);
            if (!empleadoExiste)
                return NotFound(new { mensaje = "Empleado no encontrado en la empresa." });

            var autorNombre = User.FindFirstValue(ClaimTypes.Name) ?? "Usuario";
            autorNombre = autorNombre.Trim();
            if (autorNombre.Length == 0)
                autorNombre = "Usuario";
            if (autorNombre.Length > FichadaObservacionMotivos.NombreMaxLength)
                autorNombre = autorNombre[..FichadaObservacionMotivos.NombreMaxLength];
            int? autorId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId)
                ? parsedId
                : null;
            if (autorId.HasValue && !await _context.Usuario.AnyAsync(u => u.Id == autorId.Value))
                autorId = null;

            var nuevaFichada = new Fichada
            {
                EmpleadoId = request.EmpleadoId,
                FechaHora = request.FechaHora,
                TipoRegistro = request.Tipo,
                Metodo = "Manual",
                EsManual = true,
                Estado = "Completada"
            };

            var observacion = new FichadaObservacion
            {
                Motivo = motivo,
                Detalle = detalle,
                CreadoPorUsuarioId = autorId,
                CreadoPorNombre = autorNombre,
                CreadoEn = DateTime.UtcNow,
                Fichada = nuevaFichada
            };

            if (request.Tipo == RegistroTipos.Salida)
            {
                var diaInicio = request.FechaHora.Date;
                var entradaAbierta = await _context.Fichada
                    .Where(f => f.EmpleadoId == request.EmpleadoId &&
                                f.TipoRegistro == RegistroTipos.Entrada &&
                                f.FechaHora >= diaInicio &&
                                f.FechaHora < diaInicio.AddDays(1) &&
                                f.FechaHora < request.FechaHora &&
                                f.Estado == "Incompleto")
                    .OrderByDescending(f => f.FechaHora)
                    .FirstOrDefaultAsync();
                if (entradaAbierta != null)
                {
                    entradaAbierta.Estado = "Completada";
                    entradaAbierta.MinutosHastaCorte = null;
                }
            }

            _context.Fichada.Add(nuevaFichada);
            _context.FichadaObservacion.Add(observacion);
            
            await _context.SaveChangesAsync();

            return Created($"/api/fichadas?empleadoId={nuevaFichada.EmpleadoId}", new
            {
                mensaje = "Fichada manual registrada correctamente.",
                id = nuevaFichada.Id,
                nuevaFichada.EmpleadoId,
                nuevaFichada.FechaHora,
                tipo = nuevaFichada.TipoRegistro,
                nuevaFichada.EsManual
            });
        }
    }
}