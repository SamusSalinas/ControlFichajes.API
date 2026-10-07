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

        var resultado = await _turnoService.CrearAsync(empresaId, request);
        if (resultado.Error is not null)
            return BadRequest(new { mensaje = resultado.Error });

        var nuevoTurno = resultado.Turno
            ?? throw new InvalidOperationException("La creación del turno no devolvió datos ni un error.");
        return CreatedAtAction(nameof(GetTurno), new { id = nuevoTurno.Id }, nuevoTurno);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<IActionResult> PutTurno(int id, [FromBody] TurnoCrearDto request)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId) || empresaId <= 0)
            return Forbid();

        var resultado = await _turnoService.ActualizarAsync(id, empresaId, request);
        if (!resultado.Exito)
        {
            if (resultado.Error?.Contains("encontrado", StringComparison.OrdinalIgnoreCase) == true)
            return NotFound();

            return BadRequest(new { mensaje = resultado.Error });
        }

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.Admin}")]
    public async Task<IActionResult> DeleteTurno(int id)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId) || empresaId <= 0)
            return Forbid();

        var resultado = await _turnoService.EliminarAsync(id, empresaId);
        if (!resultado.Exito)
            return NotFound();

        return NoContent();
    }
}
