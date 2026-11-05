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

    public static int RecuperacionMinutos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:RecuperacionMinutos", 30, minimo: 1);
    }

    public static int RecuperacionSolicitudLimite(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:RecuperacionSolicitudLimite", 5, minimo: 1);
    }

    public static int RecuperacionSolicitudVentanaSegundos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:RecuperacionSolicitudVentanaSegundos", 60, minimo: 1);
    }

    public static int RecuperacionRestablecerLimite(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:RecuperacionRestablecerLimite", 5, minimo: 1);
    }

    public static int RecuperacionRestablecerVentanaSegundos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:RecuperacionRestablecerVentanaSegundos", 60, minimo: 1);
    }

    public static int AccessExpireMinutes(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Jwt:AccessExpireMinutes", 15, minimo: 1);
    }

    public static int RefreshExpireDays(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Jwt:RefreshExpireDays", 14, minimo: 1);
    }

    public static int RefreshInactividadHoras(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Jwt:RefreshInactividadHoras", 12, minimo: 1);
    }

    public static int RefreshLimite(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:RefreshLimite", 30, minimo: 1);
    }

    public static int RefreshVentanaSegundos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:RefreshVentanaSegundos", 60, minimo: 1);
    }

    public static int CierreLimite(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:CierreLimite", 30, minimo: 1);
    }

    public static int CierreVentanaSegundos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:CierreVentanaSegundos", 60, minimo: 1);
    }

    public static int MfaDesafioMinutos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:MfaDesafioMinutos", 5, minimo: 1);
    }

    public static int MfaIntentosDesafio(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:MfaIntentosDesafio", 5, minimo: 1);
    }

    public static int MfaIntentosUsuario(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:MfaIntentosUsuario", 5, minimo: 1);
    }

    public static int MfaBloqueoMinutos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:MfaBloqueoMinutos", 5, minimo: 1);
    }

    public static int MfaEmisionLimite(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:MfaEmisionLimite", 10, minimo: 1);
    }

    public static int MfaEmisionVentanaSegundos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:MfaEmisionVentanaSegundos", 60, minimo: 1);
    }

    public static int MfaVerificacionLimite(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:MfaVerificacionLimite", 10, minimo: 1);
    }

    public static int MfaVerificacionVentanaSegundos(IConfiguration? configuration)
    {
        return LeerEntero(configuration, "Auth:MfaVerificacionVentanaSegundos", 60, minimo: 1);
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
