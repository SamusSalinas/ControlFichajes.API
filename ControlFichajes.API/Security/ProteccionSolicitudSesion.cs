namespace ControlFichajes.API.Security;

public enum ClaseSolicitudSesion
{
    Tradicional,
    Renovable,
    Rechazada
}

public static class ProteccionSolicitudSesion
{
    public const string Encabezado = "X-CF-Solicitud";
    public const string Valor = "1";

    public static ClaseSolicitudSesion Evaluar(HttpRequest request, IConfiguration? configuration)
    {
        if (!request.Headers.TryGetValue(Encabezado, out var valores))
            return ClaseSolicitudSesion.Tradicional;

        if (valores.Count != 1 || !string.Equals(valores[0], Valor, StringComparison.Ordinal))
            return ClaseSolicitudSesion.Rechazada;

        return OrigenPermitido(request, configuration)
            ? ClaseSolicitudSesion.Renovable
            : ClaseSolicitudSesion.Rechazada;
    }

    public static bool OrigenPermitido(HttpRequest request, IConfiguration? configuration)
    {
        if (!request.Headers.TryGetValue("Origin", out var origenes) || origenes.Count != 1)
            return false;

        var origen = origenes[0];
        if (string.IsNullOrWhiteSpace(origen) || string.Equals(origen, "null", StringComparison.Ordinal))
            return false;

        var permitidos = configuration?.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return permitidos.Contains(origen, StringComparer.Ordinal);
    }
}
