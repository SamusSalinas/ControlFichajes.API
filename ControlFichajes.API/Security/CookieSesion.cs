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

    public static string NombreDe(Guid generacion)
    {
        return "cf_g_" + generacion.ToString("N");
    }

    public static void Escribir(
        HttpResponse response,
        string nombre,
        string token,
        DateTime expiraEn,
        bool secure,
        SameSiteMode sameSite,
        TimeSpan tope)
    {
        response.Cookies.Append(nombre, token, Opciones(secure, sameSite, expiraEn, tope));
    }

    public static void Borrar(HttpResponse response, string nombre, bool secure, SameSiteMode sameSite)
    {
        response.Cookies.Delete(nombre, Opciones(secure, sameSite, null, TimeSpan.Zero));
    }

    private static CookieOptions Opciones(bool secure, SameSiteMode sameSite, DateTime? expiraEn, TimeSpan tope)
    {
        var opciones = new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = sameSite,
            Path = Path,
            IsEssential = true
        };

        if (!expiraEn.HasValue)
            return opciones;

        var ahora = DateTime.UtcNow;
        var vida = DateTime.SpecifyKind(expiraEn.Value, DateTimeKind.Utc) - ahora;
        if (vida < TimeSpan.Zero)
            vida = TimeSpan.Zero;
        if (tope > TimeSpan.Zero && vida > tope)
            vida = tope;

        opciones.MaxAge = vida;
        opciones.Expires = new DateTimeOffset(ahora.Add(vida));
        return opciones;
    }
}
