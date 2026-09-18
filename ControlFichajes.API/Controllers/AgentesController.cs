using ControlFichajes.API.DTOs;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AgentesController : ControllerBase
{
    private readonly IAgenteAdminService _agenteAdminService;
    private readonly IAgenteLectorService _agenteLectorService;

    public AgentesController(IAgenteAuthService agenteAuthService,
        IAgenteAdminService agenteAdminService,
        IAgenteLectorService agenteLectorService)
    {
        _agenteAdminService = agenteAdminService;
        _agenteLectorService = agenteLectorService;
    }

    [HttpPost]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> Crear(AgenteCrearDto request)
    {
        var agente = await _agenteAdminService.CrearAsync(request);
        return agente == null
            ? Conflict(new { mensaje = "El clientId ya existe o la sucursal no existe." })
            : CreatedAtAction(nameof(Obtener), new { id = agente.Id }, agente);
    }

    [HttpGet]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> Listar()
    {
        return Ok(await _agenteAdminService.ListarAsync());
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> Obtener(int id)
    {
        var agente = await _agenteAdminService.ObtenerAsync(id);
        return agente == null ? NotFound() : Ok(agente);
    }

    [HttpPost("{id:int}/rotar-secret")]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> RotarSecret(int id)
    {
        var agente = await _agenteAdminService.RotarSecretAsync(id);
        return agente == null ? NotFound() : Ok(agente);
    }

    [HttpPatch("{id:int}/desactivar")]
    [Authorize(Policy = "SoloSuperadmin")]
    public async Task<IActionResult> Desactivar(int id)
    {
        return await _agenteAdminService.DesactivarAsync(id) ? NoContent() : NotFound();
    }

    [HttpGet("sucursal")]
    [Authorize(Policy = "SoloAgente")]
    public async Task<IActionResult> ObtenerSucursalDelAgente()
    {
        if (!int.TryParse(User.FindFirst("agente_id")?.Value, out var agenteId))
            return Forbid();

        if (!int.TryParse(User.FindFirst("sucursal_id")?.Value, out var sucursalId))
            return Forbid();

        var sucursal = await _agenteLectorService.ObtenerSucursalPorAgenteAsync(agenteId, sucursalId);
        return sucursal == null ? NotFound() : Ok(sucursal);
    }

    [HttpPost("{id:int}/heartbeat")]
    [Authorize(Policy = "SoloAgente")]
    public async Task<IActionResult> Heartbeat(int id, AgenteHeartbeatDto request)
    {
        if (!int.TryParse(User.FindFirst("agente_id")?.Value, out var agenteId) || agenteId != id)
            return Forbid();

        var heartbeat = await _agenteLectorService.RegistrarHeartbeatAsync(id, request);
        return heartbeat == null ? NotFound() : Ok(new { agenteId = id, ultimoAcceso = DateTime.UtcNow });
    }
}