using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Models;

namespace ControlFichajes.API.Security;

public static class UsuarioAdministracionAccess
{
    public static bool TryGetOperadorId(ClaimsPrincipal user, out int operadorId)
    {
        operadorId = 0;
        return int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out operadorId)
            && operadorId > 0;
    }

    public static bool PuedeAdministrarObjetivo(ClaimsPrincipal user, Usuario? target, int operadorId)
    {
        if (target == null || operadorId <= 0)
            return false;

        if (target.Id == operadorId)
            return false;

        if (AppRoles.IsSuperAdmin(target.Rol))
            return false;

        if (user.IsInRole(AppRoles.SuperAdmin))
            return target.Rol is AppRoles.Admin or AppRoles.Rrhh;

        if (user.IsInRole(AppRoles.Admin) && EmpresaAccess.TryGetEmpresaId(user, out var empresaId))
            return target.Rol == AppRoles.Rrhh && target.EmpresaId == empresaId;

        return false;
    }

    public static bool PuedeCambiarRol(ClaimsPrincipal user, Usuario? target, int operadorId)
    {
        return user.IsInRole(AppRoles.SuperAdmin)
            && PuedeAdministrarObjetivo(user, target, operadorId);
    }
}
