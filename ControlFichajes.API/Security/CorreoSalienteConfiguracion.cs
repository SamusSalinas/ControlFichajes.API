using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.Security;

public readonly record struct EstadoCorreoSaliente(bool Configurado, string Remitente)
{
    public static EstadoCorreoSaliente Leer(IConfiguration? configuration)
    {
        var host = configuration?["Smtp:Host"]?.Trim() ?? string.Empty;
        var remitente = configuration?["Smtp:From"]?.Trim() ?? string.Empty;
        var usuario = configuration?["Smtp:User"]?.Trim() ?? string.Empty;
        var clave = configuration?["Smtp:Password"] ?? string.Empty;
        var puertoTexto = configuration?["Smtp:Port"];
        var puertoValido = int.TryParse(puertoTexto, out var puerto) && puerto is >= 1 and <= 65535;
        var remitenteValido = CorreoValido(remitente);
        var credencialesValidas = string.IsNullOrEmpty(usuario)
            ? string.IsNullOrEmpty(clave)
            : !string.IsNullOrWhiteSpace(clave);
        var configurado = host.Length > 0 && puertoValido && remitenteValido && credencialesValidas;
        return new EstadoCorreoSaliente(configurado, remitenteValido ? remitente : string.Empty);
    }

    public static bool CorreoValido(string? valor)
    {
        var texto = valor?.Trim() ?? string.Empty;
        if (texto.Length == 0 || texto.Length > 100 || texto.Any(char.IsWhiteSpace))
            return false;

        return new EmailAddressAttribute().IsValid(texto);
    }
}
