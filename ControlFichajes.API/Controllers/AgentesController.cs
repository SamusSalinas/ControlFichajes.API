using ControlFichajes.API.DTOs;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AgentesController : ControllerBase
{
    private readonly IAgenteService _agenteService;

    public AgentesController(IAgenteService agenteService)
    {
        _agenteService = agenteService;
    }

    [HttpPost]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> Crear(AgenteCrearDto request)
    {
        var agente = await _agenteService.CrearAsync(request);
        return agente == null
            ? Conflict(new { mensaje = "El clientId ya existe o la sucursal no existe." })
            : CreatedAtAction(nameof(Obtener), new { id = agente.Id }, agente);
    }

    [HttpGet]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> Listar()
    {
        return Ok(await _agenteService.ListarAsync());
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> Obtener(int id)
    {
        var agente = await _agenteService.ObtenerAsync(id);
        return agente == null ? NotFound() : Ok(agente);
    }

    [HttpPost("{id:int}/rotar-secret")]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> RotarSecret(int id)
    {
        var agente = await _agenteService.RotarSecretAsync(id);
        return agente == null ? NotFound() : Ok(agente);
    }

    [HttpPatch("{id:int}/desactivar")]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> Desactivar(int id)
    {
        return await _agenteService.DesactivarAsync(id) ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/heartbeat")]
    [Authorize(Policy = "SoloAgente")]
    public async Task<IActionResult> Heartbeat(int id, AgenteHeartbeatDto request)
    {
        if (!int.TryParse(User.FindFirst("agente_id")?.Value, out var agenteId) || agenteId != id)
            return Forbid();

        var heartbeat = await _agenteService.RegistrarHeartbeatAsync(id, request);
        return heartbeat == null ? NotFound() : Ok(new { agenteId = id, ultimoAcceso = DateTime.UtcNow });
    }
}