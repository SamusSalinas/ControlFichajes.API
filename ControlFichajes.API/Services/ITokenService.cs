using ControlFichajes.API.Models;

namespace ControlFichajes.API.Services;

public interface ITokenService
{
    string CreateToken(Usuario usuario);
    string CreateAgentToken(AgenteInstalacion agente, int empresaId);
}