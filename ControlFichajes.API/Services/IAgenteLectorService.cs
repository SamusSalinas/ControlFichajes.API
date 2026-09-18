using ControlFichajes.API.DTOs;
namespace ControlFichajes.API.Services;

public interface IAgenteLectorService
{
    Task<AgenteHeartbeatDto?> RegistrarHeartbeatAsync(int id, AgenteHeartbeatDto request);
    Task<SucursalDto?> ObtenerSucursalPorAgenteAsync(int agenteId, int sucursalId);
}
