using System.Text.RegularExpressions;

namespace ControlFichajes.API.Security;

public enum EstadoGeneracionSesion
{
    Ausente,
    Valida,
    Invalida
}

public readonly struct LecturaGeneracionSesion
{
    public EstadoGeneracionSesion Estado { get; init; }

    public Guid Valor { get; init; }
}

public static partial class GeneracionSesion
{
    public const string Encabezado = "X-CF-Generacion";

    public static LecturaGeneracionSesion Leer(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(Encabezado, out var valores))
            return new LecturaGeneracionSesion { Estado = EstadoGeneracionSesion.Ausente };

        if (valores.Count != 1 || valores[0] is not string texto || !Formato().IsMatch(texto))
            return new LecturaGeneracionSesion { Estado = EstadoGeneracionSesion.Invalida };

        if (!Guid.TryParseExact(texto, "D", out var generacion))
            return new LecturaGeneracionSesion { Estado = EstadoGeneracionSesion.Invalida };

        return new LecturaGeneracionSesion { Estado = EstadoGeneracionSesion.Valida, Valor = generacion };
    }

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex Formato();
}
