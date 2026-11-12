namespace ControlFichajes.API.Security;

public static class CorreoNormalizado
{
    public const int Maximo = 100;

    public static string? ParaRecuperacion(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var correo = valor.Trim().ToLowerInvariant();
        if (correo.Length > Maximo || correo.Any(char.IsWhiteSpace))
            return null;

        var partes = correo.Split('@');
        if (partes.Length != 2)
            return null;

        var local = partes[0];
        var dominio = partes[1];
        if (local.Length == 0 || dominio.Length < 3 || dominio[0] == '.' || dominio[^1] == '.')
            return null;

        if (!dominio.Contains('.') || local.Contains("..", StringComparison.Ordinal) || dominio.Contains("..", StringComparison.Ordinal))
            return null;

        return correo;
    }
}
