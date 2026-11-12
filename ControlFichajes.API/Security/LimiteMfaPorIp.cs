using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace ControlFichajes.API.Security;

public interface ILimiteMfaPorIp
{
    bool Permitir(string categoria, string? ip, int limite, TimeSpan ventana);
}

public sealed class LimiteMfaPorIp : ILimiteMfaPorIp
{
    private readonly ConcurrentDictionary<string, FixedWindowRateLimiter> _ventanas = new(StringComparer.Ordinal);

    public bool Permitir(string categoria, string? ip, int limite, TimeSpan ventana)
    {
        var clave = categoria + "|" + (string.IsNullOrWhiteSpace(ip) ? "desconocida" : ip.Trim());
        var limiter = _ventanas.GetOrAdd(
            clave,
            _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = limite,
                Window = ventana,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
        using var lease = limiter.AttemptAcquire(1);
        return lease.IsAcquired;
    }
}
