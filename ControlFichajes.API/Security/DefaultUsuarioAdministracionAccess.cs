using System.Security.Claims;
using System.Threading.Tasks;
using ControlFichajes.API.Models;

namespace ControlFichajes.API.Security;

public class DefaultUsuarioAdministracionAccess : IUsuarioAdministracionAccess
{
    public Task<string?> PuedeAdministrarObjetivo(ClaimsPrincipal user, Usuario? target)
    {
        if (!UsuarioAdministracionAccess.TryGetOperadorId(user, out var operadorId))
            return Task.FromResult<string?>("El ID del operador no es válido o no está autenticado.");

        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(user, target, operadorId))
            return Task.FromResult<string?>("No tienes permisos suficientes para administrar este objetivo.");

        return Task.FromResult<string?>(null);
    }
}