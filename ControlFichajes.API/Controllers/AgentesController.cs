using ControlFichajes.API.Constants;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ControlFichajes.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "SoloSuperadmin")]
public class AgentesController : ControllerBase
{
    private readonly IAuthService _authService;

    public AgentesController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] AgenteCrearDto request)
    {
        var agente = await _authService.CrearAgenteAsync(request);
        return agente == null
            ? Conflict(new { mensaje = "El clientId o la sucursal ya no están disponibles." })
            : CreatedAtAction(nameof(Crear), new { id = agente.Id }, agente);
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromServices] AppDbContext context)
    {
        return Ok(await context.AgenteInstalacion
            .AsNoTracking()
            .Select(a => new AgenteDto
            {
                Id = a.Id,
                EmpresaId = a.EmpresaId,
                SucursalId = a.SucursalId,
                ClientId = a.ClientId,
                Activo = a.Activo,
                UltimoHeartbeat = a.UltimoHeartbeat
            })
            .ToListAsync());
    }

    [HttpPost("{id:int}/rotar-secret")]
    public async Task<IActionResult> RotarSecret(int id)
    {
        var agente = await _authService.RotarSecretAgenteAsync(id);
        return agente == null ? NotFound() : Ok(agente);
    }

    [HttpPatch("{id:int}/desactivar")]
    public async Task<IActionResult> Desactivar(int id, [FromServices] AppDbContext context)
    {
        var agente = await context.AgenteInstalacion.FindAsync(id);
        if (agente == null)
            return NotFound();

        agente.Activo = false;
        await context.SaveChangesAsync();
        return NoContent();
    }
}