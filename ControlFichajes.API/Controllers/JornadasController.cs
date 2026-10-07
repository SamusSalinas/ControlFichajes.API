using System.Security.Claims;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/jornadas")]
[Authorize]
public class JornadasController : ControllerBase
{
    private readonly JornadaService _jornadaService;

    public JornadasController(JornadaService jornadaService)
    {
        _jornadaService = jornadaService;
    }

    [HttpGet("empleados/{empleadoId:int}/asignaciones")]
    public async Task<IActionResult> GetAsignaciones(int empleadoId)
    {
        if (!TryGetEmpresaId(out var empresaId))
            return Forbid();
        var asignaciones = await _jornadaService.ObtenerAsignacionesAsync(empleadoId, empresaId);
        return asignaciones == null ? NotFound() : Ok(asignaciones);
    }

    [HttpPost("empleados/{empleadoId:int}/asignaciones")]
    [Authorize(Roles = "ADMIN,SuperAdmin")]
    public async Task<IActionResult> PostAsignacion(int empleadoId, [FromBody] TurnoAsignacionCrearDto request)
    {
        if (!TryGetEmpresaId(out var empresaId))
            return Forbid();
        try
        {
            var asignacion = await _jornadaService.AsignarTurnoAsync(empleadoId, empresaId, request);
            return asignacion == null
                ? NotFound(new { mensaje = "Empleado o turno no encontrado en la empresa." })
                : CreatedAtAction(nameof(GetAsignaciones), new { empleadoId }, asignacion);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpGet("empleados/{empleadoId:int}/calendario")]
    public async Task<IActionResult> GetCalendario(
        int empleadoId,
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta)
    {
        if (!TryGetEmpresaId(out var empresaId))
            return Forbid();
        try
        {
            var calendario = await _jornadaService.ObtenerCalendarioAsync(empleadoId, empresaId, desde, hasta);
            return calendario == null ? NotFound() : Ok(calendario);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPut("empleados/{empleadoId:int}/dias/{fecha}")]
    [Authorize(Roles = "ADMIN,SuperAdmin")]
    public async Task<IActionResult> PutExcepcion(
        int empleadoId,
        DateTime fecha,
        [FromBody] JornadaExcepcionEscribirDto request)
    {
        if (!TryGetEmpresaId(out var empresaId))
            return Forbid();
        try
        {
            var guardada = await _jornadaService.GuardarExcepcionAsync(empleadoId, empresaId, fecha, request);
            return guardada == null ? NotFound() : NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpDelete("empleados/{empleadoId:int}/dias/{fecha}")]
    [Authorize(Roles = "ADMIN,SuperAdmin")]
    public async Task<IActionResult> DeleteExcepcion(int empleadoId, DateTime fecha)
    {
        if (!TryGetEmpresaId(out var empresaId))
            return Forbid();
        var eliminada = await _jornadaService.EliminarExcepcionAsync(empleadoId, empresaId, fecha);
        return eliminada switch
        {
            null => NotFound(),
            false => NotFound(new { mensaje = "No existe una excepción para esa fecha." }),
            true => NoContent()
        };
    }

    [HttpPost("empleados/{empleadoId:int}/reposiciones")]
    [Authorize(Roles = "ADMIN,SuperAdmin")]
    public async Task<IActionResult> PostReposicion(int empleadoId, [FromBody] ReposicionHorasCrearDto request)
    {
        if (!TryGetEmpresaId(out var empresaId))
            return Forbid();
        try
        {
            var reposicion = await _jornadaService.RegistrarReposicionAsync(
                empleadoId,
                empresaId,
                request.Fecha,
                request,
                TryGetUsuarioId(),
                ResolverNombreUsuario());
            return reposicion == null ? NotFound() : StatusCode(StatusCodes.Status201Created, reposicion);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpGet("empleados/{empleadoId:int}/reposiciones")]
    public async Task<IActionResult> GetReposiciones(
        int empleadoId,
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta)
    {
        if (!TryGetEmpresaId(out var empresaId))
            return Forbid();
        try
        {
            var reposiciones = await _jornadaService.ObtenerReposicionesAsync(empleadoId, empresaId, desde, hasta);
            return reposiciones == null ? NotFound() : Ok(reposiciones);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpGet("empleados/{empleadoId:int}/resumen")]
    public async Task<IActionResult> GetResumen(
        int empleadoId,
        [FromQuery] DateTime desde,
        [FromQuery] DateTime hasta)
    {
        if (!TryGetEmpresaId(out var empresaId))
            return Forbid();
        try
        {
            var resumen = await _jornadaService.ObtenerResumenAsync(empleadoId, empresaId, desde, hasta);
            return resumen == null ? NotFound() : Ok(resumen);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    private bool TryGetEmpresaId(out int empresaId) => EmpresaAccess.TryGetEmpresaId(User, out empresaId);

    private int? TryGetUsuarioId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private string ResolverNombreUsuario() => User.FindFirstValue(ClaimTypes.Name) ?? "Usuario";
}
