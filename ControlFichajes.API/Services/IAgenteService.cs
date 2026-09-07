using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Services;

public interface IAgenteService
{
    Task<AgenteCreadoDto?> CrearAsync(AgenteCrearDto request);
    Task<AgenteDto?> ObtenerAsync(int id);
    Task<IReadOnlyList<AgenteDto>> ListarAsync();
    Task<AgenteCreadoDto?> RotarSecretAsync(int id);
    Task<bool> DesactivarAsync(int id);
    Task<string?> AutenticarAsync(AgenteLoginDto request);
    Task<AgenteHeartbeatDto?> RegistrarHeartbeatAsync(int id, AgenteHeartbeatDto request);
}