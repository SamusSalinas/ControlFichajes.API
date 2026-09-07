using System.Security.Claims;
using ControlFichajes.API.Constants;
using Microsoft.AspNetCore.Http;

namespace ControlFichajes.API.Controllers;

public static class EmpresaAccess
{
    public static bool IsSuperAdmin(ClaimsPrincipal user)
    {
        return user.Identities.Any(identity =>
            identity.IsAuthenticated &&
            identity.Claims.Any(claim =>
                claim.Type == identity.RoleClaimType &&
                AppRoles.IsSuperAdmin(claim.Value)));
    }

    public static void ApplyEmpresaContext(ClaimsPrincipal user, IHeaderDictionary headers)
    {
        if (!IsSuperAdmin(user))
            return;

        foreach (var identity in user.Identities)
        {
            foreach (var claim in identity.FindAll("empresa_id").ToList())
                identity.TryRemoveClaim(claim);
        }

        if (user.Identity is not ClaimsIdentity claimsIdentity ||
            !claimsIdentity.IsAuthenticated ||
            !headers.TryGetValue("X-Empresa-Id", out var headerValue) ||
            !int.TryParse(headerValue.ToString(), out var empresaId) ||
            empresaId <= 0)
        {
            return;
        }

        claimsIdentity.AddClaim(new Claim("empresa_id", empresaId.ToString()));
    }

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
