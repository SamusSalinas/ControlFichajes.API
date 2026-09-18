using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public class AgenteLectorService : IAgenteLectorService
{
    private readonly AppDbContext _context;

    public AgenteLectorService(
        AppDbContext context)
    {
        _context = context;
        
    }
    public async Task<AgenteHeartbeatDto?> RegistrarHeartbeatAsync(int id, AgenteHeartbeatDto request)
    {
        var agente = await _context.AgenteInstalacion.FirstOrDefaultAsync(a => a.Id == id && a.Activo);
        if (agente == null)
            return null;

        agente.UltimoAcceso = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return new AgenteHeartbeatDto
        {
            VersionApp = request.VersionApp?.Trim(),
            SerialLector = request.SerialLector?.Trim(),
            EstadoLector = request.EstadoLector?.Trim(),
            UltimaSincronizacion = request.UltimaSincronizacion
        };
    }

    public async Task<SucursalDto?> ObtenerSucursalPorAgenteAsync(int agenteId, int sucursalId)
    {
        var agente = await _context.AgenteInstalacion
            .AsNoTracking()
            .Include(a => a.Sucursal)
            .FirstOrDefaultAsync(a => a.Id == agenteId && a.SucursalId == sucursalId && a.Activo);

        if (agente?.Sucursal == null)
            return null;

        return new SucursalDto
        {
            Id = agente.Sucursal.Id,
            Nombre = agente.Sucursal.Nombre,
            EmpresaId = agente.Sucursal.EmpresaId,
            SerialLector = agente.Sucursal.SerialLector
        };
    }
}
