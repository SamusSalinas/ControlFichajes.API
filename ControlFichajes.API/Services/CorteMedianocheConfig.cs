using Microsoft.Extensions.Configuration;

namespace ControlFichajes.API.Services;

/// <summary>
/// CorteMedianoche queda apagado si la sección no existe o Habilitado no es true.
/// La consulta ya clasifica la jornada; el worker no es necesario para leer el estado.
/// No activar esta opción en appsettings ni en variables de producción desde este cambio.
/// </summary>
public static class CorteMedianocheConfig
{
    public const string SectionName = "Jornadas:CorteMedianoche";

    public static bool EstaHabilitado(IConfiguration configuration)
    {
        return configuration.GetValue($"{SectionName}:Habilitado", false);
    }
}
