namespace ControlFichajes.API.Security;

public sealed record ConexionSmtp
{
    public string Servidor { get; init; } = string.Empty;
    public int Puerto { get; init; }
    public string Cifrado { get; init; } = string.Empty;
    public string Usuario { get; init; } = string.Empty;
    public string Remitente { get; init; } = string.Empty;
    public string Contrasena { get; init; } = string.Empty;
    public bool Guardada { get; init; }

    public bool ContrasenaConfigurada => Contrasena.Length > 0;
}

public static class ConexionSmtpReglas
{
    public const string Hostinger = "smtp.hostinger.com";
    public const string TlsConexion = "tls-conexion";
    public const string StartTls = "starttls";
    public const string Ninguno = "ninguno";

    public static bool CapturadorPermitido(IConfiguration? configuration)
    {
        return bool.TryParse(configuration?["Smtp:PermitirCapturadorLocal"], out var valor) && valor;
    }

    public static ConexionSmtp DesdeConfiguracion(IConfiguration configuration)
    {
        var servidor = configuration["Smtp:Host"]?.Trim() ?? string.Empty;
        var puerto = int.TryParse(configuration["Smtp:Port"], out var valor) ? valor : 0;
        var ssl = !bool.TryParse(configuration["Smtp:EnableSsl"], out var habilitar) || habilitar;
        return new ConexionSmtp
        {
            Servidor = servidor,
            Puerto = puerto,
            Cifrado = ssl ? (puerto == 465 ? TlsConexion : StartTls) : Ninguno,
            Usuario = configuration["Smtp:User"]?.Trim() ?? string.Empty,
            Remitente = configuration["Smtp:From"]?.Trim() ?? string.Empty,
            Contrasena = configuration["Smtp:Password"] ?? string.Empty,
            Guardada = false
        };
    }

    public static bool Completa(ConexionSmtp conexion, IConfiguration? configuration)
    {
        return Validar(conexion, configuration).Count == 0;
    }

    public static Dictionary<string, string> Validar(ConexionSmtp conexion, IConfiguration? configuration)
    {
        var errores = new Dictionary<string, string>(StringComparer.Ordinal);
        var servidor = conexion.Servidor.Trim();
        if (servidor.Length == 0 || !ServidorPermitido(servidor, configuration))
            errores["servidor"] = "El servidor SMTP no está habilitado.";

        if (conexion.Puerto is < 1 or > 65535)
            errores["puerto"] = "Ingresá un puerto entre 1 y 65535.";
        else if (EsHostinger(servidor) && conexion.Puerto != 465)
            errores["puerto"] = "Hostinger usa el puerto 465.";

        if (!CifradoPermitido(servidor, conexion.Cifrado, configuration))
            errores["cifrado"] = "El cifrado no está habilitado para este servidor.";

        var remitente = conexion.Remitente.Trim();
        if (!EstadoCorreoSaliente.CorreoValido(remitente))
            errores["remitente"] = remitente.Length == 0
                ? "Ingresá el correo remitente."
                : "Ingresá un correo remitente válido.";

        var usuario = conexion.Usuario.Trim();
        if (usuario.Length > 100 || usuario.Any(char.IsWhiteSpace))
            errores["usuario"] = "El usuario SMTP no es válido.";

        if (usuario.Length == 0 && conexion.Contrasena.Length > 0)
            errores["contrasena"] = "Sin usuario SMTP, no corresponde una contraseña.";
        else if (usuario.Length > 0 && conexion.Contrasena.Length == 0)
            errores["contrasena"] = "Si indicás un usuario SMTP, la contraseña es obligatoria.";
        else if (conexion.Contrasena.Length > 200)
            errores["contrasena"] = "La contraseña SMTP es demasiado larga.";

        return errores;
    }

    public static bool ServidorPermitido(string servidor, IConfiguration? configuration)
    {
        if (EsHostinger(servidor))
            return true;
        if (!CapturadorPermitido(configuration))
            return false;
        return servidor.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || servidor.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CifradoPermitido(string servidor, string? cifrado, IConfiguration? configuration)
    {
        if (EsHostinger(servidor))
            return string.Equals(cifrado, TlsConexion, StringComparison.Ordinal);

        if (!ServidorPermitido(servidor, configuration))
            return false;

        return cifrado is TlsConexion or StartTls or Ninguno;
    }

    private static bool EsHostinger(string servidor)
    {
        return servidor.Equals(Hostinger, StringComparison.OrdinalIgnoreCase);
    }
}
