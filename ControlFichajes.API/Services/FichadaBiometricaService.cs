using System.Globalization;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public static class FichadaBiometricaIdempotencia
{
    public const string Indice = "UX_Fichada_ClaveIdempotencia";

    public static DateTime Normalizar(DateTime fechaHora)
    {
        var pared = LaborTime.Pared(fechaHora);
        var ticks = pared.Ticks - (pared.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTime(ticks, DateTimeKind.Unspecified);
    }

    public static string Clave(int empleadoId, DateTime fechaHora)
    {
        var normalizada = Normalizar(fechaHora);
        return string.Create(CultureInfo.InvariantCulture, $"b:{empleadoId}:{normalizada:yyyyMMddHHmmssffffff}");
    }

    public static bool EsConflictoDeClave(DbUpdateException exception)
    {
        if (!FichadaObservacionPersistencia.EsDuplicadoClaveMySql(exception))
            return false;
        return exception.ToString().Contains(Indice, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class FichadaBiometricaResultado
{
    public int Recibidas { get; init; }
    public string? Error { get; init; }
}

public sealed class FichadaBiometricaService
{
    private const string MetodoBiometrico = "Biometrico";
    private readonly AppDbContext _context;

    public FichadaBiometricaService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<FichadaBiometricaResultado> RegistrarAsync(
        IReadOnlyCollection<FichadaRequestDto> solicitudes,
        int empresaId,
        CancellationToken cancellationToken = default)
    {
        if (solicitudes.Count is < 1 or > 500)
            return new FichadaBiometricaResultado { Error = "El lote debe contener entre 1 y 500 fichadas." };
        if (solicitudes.Any(s => s.EmpleadoId <= 0 || s.FechaHora == default))
            return new FichadaBiometricaResultado { Error = "EmpleadoId y FechaHora son obligatorios." };

        var empleadoIds = solicitudes.Select(s => s.EmpleadoId).Distinct().ToList();
        var empleadosActivos = await _context.Empleado
            .Where(e => empleadoIds.Contains(e.Id) && e.Activo && e.EmpresaId == empresaId)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (empleadoIds.Except(empleadosActivos).Any())
            return new FichadaBiometricaResultado
            {
                Error = "El lote contiene empleados inexistentes o inactivos."
            };

        var normalizadas = solicitudes
            .Select(s => (Solicitud: s, FechaHora: FichadaBiometricaIdempotencia.Normalizar(s.FechaHora)))
            .Select(s => (s.Solicitud, s.FechaHora, Clave: FichadaBiometricaIdempotencia.Clave(s.Solicitud.EmpleadoId, s.FechaHora)))
            .OrderBy(s => s.Solicitud.EmpleadoId)
            .ThenBy(s => s.FechaHora)
            .ToList();
        var fechaDesde = normalizadas.Min(s => s.FechaHora).Date;
        var fechaHasta = normalizadas.Max(s => s.FechaHora).Date.AddDays(1);
        var claves = normalizadas.Select(s => s.Clave).Distinct().ToList();
        var eventosDelRango = await _context.Fichada
            .Where(f => empleadoIds.Contains(f.EmpleadoId) &&
                        ((f.ClaveIdempotencia != null && claves.Contains(f.ClaveIdempotencia)) ||
                         (f.FechaHora >= fechaDesde && f.FechaHora < fechaHasta)))
            .ToListAsync(cancellationToken);
        var nuevosEventos = new List<Fichada>(solicitudes.Count);
        var clavesDelLote = new HashSet<string>(StringComparer.Ordinal);

        foreach (var solicitud in normalizadas)
        {
            if (!clavesDelLote.Add(solicitud.Clave))
                continue;
            if (eventosDelRango.Any(f =>
                    f.EmpleadoId == solicitud.Solicitud.EmpleadoId &&
                    (f.ClaveIdempotencia == solicitud.Clave ||
                     (f.Metodo == MetodoBiometrico &&
                      FichadaBiometricaIdempotencia.Normalizar(f.FechaHora) == solicitud.FechaHora))))
            {
                continue;
            }

            nuevosEventos.Add(new Fichada
            {
                EmpleadoId = solicitud.Solicitud.EmpleadoId,
                FechaHora = solicitud.FechaHora,
                TipoRegistro = RegistroTipos.SinTipo,
                Metodo = MetodoBiometrico,
                ClaveIdempotencia = solicitud.Clave
            });
        }

        if (nuevosEventos.Count > 0)
        {
            _context.Fichada.AddRange(nuevosEventos);
            await GuardarIdempotenteAsync(nuevosEventos, cancellationToken);
        }

        return new FichadaBiometricaResultado { Recibidas = solicitudes.Count };
    }

    private async Task GuardarIdempotenteAsync(List<Fichada> pendientes, CancellationToken cancellationToken)
    {
        for (var intento = 0; intento <= pendientes.Count; intento++)
        {
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (FichadaBiometricaIdempotencia.EsConflictoDeClave(ex))
            {
                foreach (var entry in _context.ChangeTracker.Entries<Fichada>().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;

                var claves = pendientes.Select(f => f.ClaveIdempotencia).Where(c => c != null).ToList();
                var ocupadas = await _context.Fichada.AsNoTracking()
                    .Where(f => f.ClaveIdempotencia != null && claves.Contains(f.ClaveIdempotencia))
                    .Select(f => f.ClaveIdempotencia!)
                    .ToListAsync(cancellationToken);
                var antes = pendientes.Count;
                pendientes.RemoveAll(f => f.ClaveIdempotencia != null && ocupadas.Contains(f.ClaveIdempotencia));
                if (pendientes.Count == antes)
                    throw;
                if (pendientes.Count == 0)
                    return;
                _context.Fichada.AddRange(pendientes);
            }
        }

        throw new InvalidOperationException("No se pudo confirmar la fichada biométrica.");
    }
}
