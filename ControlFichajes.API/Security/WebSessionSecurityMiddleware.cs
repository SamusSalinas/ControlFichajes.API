using System.Security.Claims;
using ControlFichajes.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Security;

public sealed class WebSessionSecurityMiddleware
{
    private const string CambiarPasswordPath = "/api/Auth/cambiar-password";
    private readonly RequestDelegate _next;

    public WebSessionSecurityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
            context.User.Identity?.IsAuthenticated != true ||
            !context.User.HasClaim("token_use", "web"))
        {
            await _next(context);
            return;
        }

        var usuarioIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tokenVersionClaim = context.User.FindFirstValue("token_version");

        if (!int.TryParse(usuarioIdClaim, out var usuarioId) ||
            usuarioId <= 0 ||
            !int.TryParse(tokenVersionClaim, out var tokenVersion))
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "Sesión inválida.");
            return;
        }

        var usuario = await dbContext.Usuario
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == usuarioId);

        if (usuario == null || !usuario.Activo || usuario.TokenVersion != tokenVersion)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "Sesión inválida.");
            return;
        }

        if (usuario.RequiereCambioPassword && !EsCambioPasswordPermitido(context.Request))
        {
            await RejectAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Debés cambiar tu contraseña antes de continuar.");
            return;
        }

        await _next(context);
    }

    private static bool EsCambioPasswordPermitido(HttpRequest request)
    {
        return HttpMethods.IsPost(request.Method) &&
            string.Equals(request.Path.Value, CambiarPasswordPath, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RejectAsync(HttpContext context, int statusCode, string mensaje)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { mensaje });
    }
}
