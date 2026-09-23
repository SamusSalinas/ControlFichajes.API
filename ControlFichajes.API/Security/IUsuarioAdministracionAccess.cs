using System.Security.Claims;
using ControlFichajes.API.Models;
namespace ControlFichajes.API.Security;

public interface IUsuarioAdministracionAccess
{
    Task<string?> PuedeAdministrarObjetivo(ClaimsPrincipal user, Usuario? target);
}
