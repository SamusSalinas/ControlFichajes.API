using ControlFichajes.API.Models;

namespace ControlFichajes.API.Services;

public interface ITokenService
{
    string CreateToken(Usuario usuario);
    string CreateToken(Usuario usuario, string? sesionId) => CreateToken(usuario);
    string CreateAgentToken(AgenteInstalacion agente, int empresaId);
}