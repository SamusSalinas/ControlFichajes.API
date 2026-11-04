namespace ControlFichajes.API.Security;

public static class CookieSesion
{
    public const string Nombre = "cf_refresh";
    public const string Path = "/api/Auth";

    public static SameSiteMode Modo(IConfiguration? configuration)
    {
        var texto = configuration?["Auth:CookieSameSite"]?.Trim();
        if (string.Equals(texto, "Strict", StringComparison.OrdinalIgnoreCase))
            return SameSiteMode.Strict;

        if (string.Equals(texto, "None", StringComparison.OrdinalIgnoreCase))
            return SameSiteMode.None;

        return SameSiteMode.Lax;
    }

    public static bool Secure(bool desarrollo, bool https, IConfiguration? configuration)
    {
        if (Modo(configuration) == SameSiteMode.None)
            return true;

        if (bool.TryParse(configuration?["Auth:CookieSecure"], out var explicito))
            return explicito;

        return !(desarrollo && !https);
    }

    public static void Escribir(HttpResponse response, string token, DateTime expiraEn, bool secure, SameSiteMode sameSite)
    {
        response.Cookies.Append(Nombre, token, Opciones(secure, sameSite, expiraEn));
    }

    public static void Borrar(HttpResponse response, bool secure, SameSiteMode sameSite)
    {
        response.Cookies.Delete(Nombre, Opciones(secure, sameSite, null));
    }

    private static CookieOptions Opciones(bool secure, SameSiteMode sameSite, DateTime? expiraEn)
    {
        var opciones = new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = sameSite,
            Path = Path,
            IsEssential = true
        };

        if (expiraEn.HasValue)
            opciones.Expires = new DateTimeOffset(DateTime.SpecifyKind(expiraEn.Value, DateTimeKind.Utc));

        return opciones;
    }
}
