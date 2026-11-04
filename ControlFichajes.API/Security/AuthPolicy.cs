namespace ControlFichajes.API.Security;

public static class AuthPolicy
{
    public static bool PermitirBootstrap(IConfiguration? configuration)
    {
        var raw = configuration?["Auth:PermitirBootstrap"];
        return bool.TryParse(raw, out var habilitado) && habilitado;
    }

    public static bool PermitirAltaConPassword(IConfiguration? configuration)
    {
        var raw = configuration?["Auth:PermitirAltaConPassword"];
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        return bool.TryParse(raw, out var habilitado) && habilitado;
    }

    public static bool ExigirMfa(IConfiguration? configuration)
    {
        return bool.TryParse(configuration?["Auth:ExigirMfa"], out var exigir) && exigir;
    }

    public static int MfaGraciaDias(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:MfaGraciaDias", 14, minimo: 0);
    }

    public static int InvitacionHoras(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:InvitacionHoras", 72, minimo: 1);
    }

    public static int AceptacionLimite(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:AceptacionLimite", 5, minimo: 1);
    }

    public static int AceptacionVentanaSegundos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:AceptacionVentanaSegundos", 60, minimo: 1);
    }

    public static int RecuperacionHoras(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:RecuperacionHoras", 1, minimo: 1);
    }

    public static int RefreshExpireDays(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Jwt:RefreshExpireDays", 14, minimo: 1);
    }

    public static string? UrlPublica(IConfiguration? configuration)
    {
        var url = configuration?["App:PublicBaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return url.TrimEnd('/');
    }

    private static int LeerEntero(IConfiguration? configuration, string clave, int predeterminado, int minimo)
    {
        var raw = configuration?[clave];
        if (!int.TryParse(raw, out var valor) || valor < minimo)
            return predeterminado;

        return valor;
    }
}
