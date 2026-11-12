using System.Security.Claims;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
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

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext, IConfiguration? configuration = null)
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

        if (!await SesionVigenteAsync(context, dbContext, usuario, configuration))
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

    private static async Task<bool> SesionVigenteAsync(
        HttpContext context,
        AppDbContext dbContext,
        Usuario usuario,
        IConfiguration? configuration)
    {
        var sid = context.User.FindFirstValue("sid");
        if (string.IsNullOrWhiteSpace(sid))
            return true;

        if (!Guid.TryParse(sid, out var familiaId))
            return false;

        var ahora = DateTime.UtcNow;
        var vigente = await dbContext.RefreshToken
            .AsNoTracking()
            .SingleOrDefaultAsync(r =>
                r.FamiliaId == familiaId &&
                r.UsuarioId == usuario.Id &&
                r.RevocadaEn == null &&
                r.ReemplazadaPorId == null);

        if (vigente == null || vigente.TokenVersion != usuario.TokenVersion || vigente.ExpiraEn <= ahora)
            return false;

        var inicio = await dbContext.RefreshToken
            .AsNoTracking()
            .Where(r => r.FamiliaId == familiaId && r.UsuarioId == usuario.Id)
            .MinAsync(r => (DateTime?)r.CreadaEn);

        return inicio.HasValue && inicio.Value.AddDays(AuthPolicy.RefreshExpireDays(configuration)) > ahora;
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
