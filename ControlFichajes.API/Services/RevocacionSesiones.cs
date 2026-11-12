using ControlFichajes.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

internal static class RevocacionSesiones
{
    public static async Task<int> MarcarTodasAsync(
        AppDbContext context,
        int usuarioId,
        DateTime ahora,
        CancellationToken cancellationToken = default)
    {
        var filas = await context.RefreshToken
            .Where(r => r.UsuarioId == usuarioId && r.RevocadaEn == null)
            .ToListAsync(cancellationToken);
        foreach (var fila in filas)
            fila.RevocadaEn = ahora;

        return filas.Count;
    }

    public static async Task<int> MarcarFamiliaAsync(
        AppDbContext context,
        int usuarioId,
        Guid familiaId,
        DateTime ahora,
        CancellationToken cancellationToken = default)
    {
        var filas = await context.RefreshToken
            .Where(r => r.UsuarioId == usuarioId && r.FamiliaId == familiaId && r.RevocadaEn == null)
            .ToListAsync(cancellationToken);
        foreach (var fila in filas)
            fila.RevocadaEn = ahora;

        return filas.Count;
    }
}
