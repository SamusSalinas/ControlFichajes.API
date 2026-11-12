using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ControlFichajes.API.Security;

public static class ProxyConfiable
{
    public const string Seccion = "ForwardedHeaders";

    public static bool IntentarAplicar(ForwardedHeadersOptions opciones, IConfiguration seccion, ILogger logger)
    {
        if (!seccion.GetValue("Habilitado", false))
            return false;

        opciones.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor;
        opciones.KnownProxies.Clear();
#pragma warning disable ASPDEPR005
        opciones.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
        opciones.KnownIPNetworks.Clear();

        foreach (var proxy in seccion.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            if (IPAddress.TryParse(proxy, out var direccion))
                opciones.KnownProxies.Add(direccion);
            else
                logger.LogWarning("Se ignoró un proxy de ForwardedHeaders porque no es una dirección IP.");
        }

        foreach (var red in seccion.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
            if (System.Net.IPNetwork.TryParse(red, out var redConfiable))
                opciones.KnownIPNetworks.Add(redConfiable);
            else
                logger.LogWarning("Se ignoró una red de ForwardedHeaders porque no es un CIDR válido.");
        }

        if (opciones.KnownProxies.Count == 0 && opciones.KnownIPNetworks.Count == 0)
        {
            logger.LogWarning(
                "ForwardedHeaders está habilitado sin proxies ni redes válidas. Se mantiene la IP de la conexión directa.");
            return false;
        }

        return true;
    }

    internal static (bool Aplicado, string? Proxy, int Redes, bool SoloEncabezadoFor) Evaluar(
        IConfiguration seccion,
        ILogger logger)
    {
        var opciones = new ForwardedHeadersOptions();
        var aplicado = IntentarAplicar(opciones, seccion, logger);
        return (
            aplicado,
            opciones.KnownProxies.Count == 1 ? opciones.KnownProxies.Single().ToString() : null,
            opciones.KnownIPNetworks.Count,
            opciones.ForwardedHeaders == Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor);
    }
}
