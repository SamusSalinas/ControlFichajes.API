using ControlFichajes.API.Models;

namespace ControlFichajes.API.Services;

public interface ITokenService
{
    string CreateToken(Usuario usuario);
}