using System.Security.Claims;

namespace ControlFichajes.API.Controllers;

internal static class EmpresaAccess
{
    public static bool PerteneceAUsuario(ClaimsPrincipal user, int empresaId)
    {
        return int.TryParse(user.FindFirstValue("empresa_id"), out var empresaUsuarioId)
            && empresaUsuarioId == empresaId;
    }

    public static bool TryGetEmpresaId(ClaimsPrincipal user, out int empresaId)
    {
        return int.TryParse(user.FindFirstValue("empresa_id"), out empresaId);
    }
}
