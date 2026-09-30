using ControlFichajes.API.Constants;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class TurnosController : ControllerBase
{
    private readonly ITurnoService _turnoService;

    public TurnosController(ITurnoService turnoService)
    {
        _turnoService = turnoService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TurnoDto>>> GetTurnos()
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId) || empresaId <= 0)
            return Forbid();

        var turnos = await _turnoService.ObtenerPorEmpresaAsync(empresaId);
        return Ok(turnos);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TurnoDto>> GetTurno(int id)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId) || empresaId <= 0)
            return Forbid();

        var turno = await _turnoService.ObtenerPorIdAsync(id, empresaId);
        if (turno == null)
            return NotFound();

        return Ok(turno);
    }

    [HttpPost]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<ActionResult<TurnoDto>> PostTurno([FromBody] TurnoCrearDto request)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId) || empresaId <= 0)
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Nombre))
            return BadRequest(new { mensaje = "El nombre del turno es obligatorio." });

        if (request.Dias == null || request.Dias.Count == 0)
            return BadRequest(new { mensaje = "El turno debe contener al menos un día asignado." });

        var nuevoTurno = await _turnoService.CrearAsync(empresaId, request);
        return CreatedAtAction(nameof(GetTurno), new { id = nuevoTurno.Id }, nuevoTurno);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<IActionResult> PutTurno(int id, [FromBody] TurnoCrearDto request)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId) || empresaId <= 0)
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Nombre))
            return BadRequest(new { mensaje = "El nombre del turno es obligatorio." });

        var actualizado = await _turnoService.ActualizarAsync(id, empresaId, request);
        if (!actualizado)
            return NotFound();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<IActionResult> DeleteTurno(int id)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId) || empresaId <= 0)
            return Forbid();

        var eliminado = await _turnoService.EliminarAsync(id, empresaId);
        if (!eliminado)
            return NotFound();

        return NoContent();
    }
}
