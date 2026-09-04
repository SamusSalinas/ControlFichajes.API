using System.Security.Claims;
using ControlFichajes.API.Constants;

namespace ControlFichajes.API.Controllers;

internal static class EmpresaAccess
{
    public static bool PerteneceAUsuario(ClaimsPrincipal user, int empresaId)
    {
        if (user.IsInRole(AppRoles.Superadmin))
            return TryGetContextEmpresaId(user, out var contexto) && contexto == empresaId;

        return int.TryParse(user.FindFirstValue("empresa_id"), out var empresaUsuarioId)
            && empresaUsuarioId == empresaId;
    }

    public static bool TryGetEmpresaId(ClaimsPrincipal user, out int empresaId)
    {
        if (user.IsInRole(AppRoles.Superadmin))
            return TryGetContextEmpresaId(user, out empresaId);

        return int.TryParse(user.FindFirstValue("empresa_id"), out empresaId);
    }

    private static bool TryGetContextEmpresaId(ClaimsPrincipal user, out int empresaId)
    {
        var header = user.Identity?.IsAuthenticated == true
            ? user.FindFirstValue("empresa_id")
            : null;

        if (!int.TryParse(header, out empresaId))
            return false;

        return true;
    }
}
