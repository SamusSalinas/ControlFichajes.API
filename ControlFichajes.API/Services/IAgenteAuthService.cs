using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Services;

public interface IAgenteAuthService
{
    Task<string?> AutenticarAsync(AgenteLoginDto request);
}
