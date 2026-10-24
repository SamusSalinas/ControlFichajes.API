namespace ControlFichajes.API.Services;

/// <summary>
/// Hora laboral de Argentina. Los DateTime sin zona (Unspecified) y los Local
/// de las pruebas se leen por sus componentes de reloj, que es el contrato
/// histórico de FechaHora. Solo un instante UTC (sufijo Z) se convierte una vez.
/// </summary>
public static class LaborTime
{
    public const string ZoneId = "America/Argentina/Buenos_Aires";
    public static readonly TimeSpan MaximaJornada = TimeSpan.FromHours(12);
    public static readonly TimeSpan VentanaDuplicado = TimeSpan.FromSeconds(60);

    private static readonly Lazy<TimeZoneInfo> Zona = new(ResolverZona);

    public static TimeZoneInfo Zone => Zona.Value;

    public static DateTime Ahora(ILaborClock clock)
    {
        return DesdeUtc(clock.UtcNow);
    }

    public static DateTime DesdeUtc(DateTime utc)
    {
        var instante = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(instante, Zone), DateTimeKind.Unspecified);
    }

    public static DateTime Pared(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return DesdeUtc(value);
        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    public static DateOnly Fecha(DateTime value) => DateOnly.FromDateTime(Pared(value));

    public static DateTime InicioDia(DateOnly fecha) => fecha.ToDateTime(TimeOnly.MinValue);

    public static DateTime FinDia(DateOnly fecha) => fecha.AddDays(1).ToDateTime(TimeOnly.MinValue);

    private static TimeZoneInfo ResolverZona()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
        }
    }
}

public interface ILaborClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemLaborClock : ILaborClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class FixedLaborClock(DateTime utcNow) : ILaborClock
{
    public DateTime UtcNow { get; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
}
