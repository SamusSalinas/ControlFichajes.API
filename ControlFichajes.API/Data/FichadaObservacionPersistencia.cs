using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ControlFichajes.API.Data;

internal static class FichadaObservacionPersistencia
{
    public const int MySqlDuplicateEntry = 1062;

    public static bool EsDuplicadoClaveMySql(DbUpdateException exception)
    {
        for (Exception? actual = exception; actual != null; actual = actual.InnerException)
        {
            if (actual is MySqlException mysql && mysql.Number == MySqlDuplicateEntry)
                return true;
        }

        return false;
    }

    public static void DesacoplarInsertFallido(AppDbContext context, Fichada fichada, FichadaObservacion insertFallido)
    {
        foreach (var entry in context.ChangeTracker.Entries<FichadaObservacion>().ToList())
        {
            if (ReferenceEquals(entry.Entity, insertFallido) ||
                (entry.Entity.FichadaId == insertFallido.FichadaId && entry.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }
        }

        if (ReferenceEquals(fichada.Observacion, insertFallido))
            fichada.Observacion = null;
    }

    public static async Task<FichadaObservacion?> RecuperarTrasInsertDuplicadoAsync(
        AppDbContext context,
        Fichada fichada,
        FichadaObservacion insertFallido,
        string motivo,
        string detalle,
        int? autorId,
        string autorNombre,
        DateTime ahora,
        CancellationToken cancellationToken = default)
    {
        DesacoplarInsertFallido(context, fichada, insertFallido);

        var existente = await context.FichadaObservacion
            .FirstOrDefaultAsync(o => o.FichadaId == fichada.Id, cancellationToken);
        if (existente is null)
            return null;

        existente.Motivo = motivo;
        existente.Detalle = detalle;
        existente.ModificadoPorUsuarioId = autorId;
        existente.ModificadoPorNombre = autorNombre;
        existente.ModificadoEn = ahora;
        fichada.Observacion = existente;
        await context.SaveChangesAsync(cancellationToken);
        return existente;
    }
}
