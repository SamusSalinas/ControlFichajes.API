using System.Text.RegularExpressions;

namespace ControlFichajes.API.Security;

public static partial class UsuarioIdentidadRules
{
    public const int NombreMin = 3;
    public const int NombreMax = 50;
    public const int CorreoMax = 100;

    public const string NombreVacio = "Ingresá un nombre de usuario.";
    public const string NombreEspacios = "El nombre de usuario no puede contener espacios.";
    public const string NombreLongitud = "El nombre de usuario debe tener entre 3 y 50 caracteres.";
    public const string NombreLetraInicial = "El nombre de usuario debe comenzar con una letra.";
    public const string NombreCaracteres = "Formato inválido. Solo se permiten letras, números, punto (.), guion (-) y guion bajo (_).";
    public const string NombreSeparadores = "No utilices puntos, guiones o guiones bajos consecutivos.";
    public const string NombreTerminacion = "El nombre de usuario debe terminar con una letra o un número.";
    public const string CorreoVacio = "Ingresá el correo electrónico.";
    public const string CorreoLongitud = "El correo no puede superar 100 caracteres.";
    public const string CorreoFormato = "Ingresá un correo electrónico válido.";
    public const string CorreoDuplicado = "Ese correo ya está registrado.";
    public const string Actualizado = "Datos del usuario actualizados correctamente.";

    public static string ValidateNombre(string? nombre)
    {
        var value = nombre ?? string.Empty;
        if (value.Length == 0) return NombreVacio;
        if (value.Any(char.IsWhiteSpace)) return NombreEspacios;
        if (value.Length < NombreMin || value.Length > NombreMax) return NombreLongitud;
        if (!char.IsAsciiLetter(value[0])) return NombreLetraInicial;
        if (value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '.' and not '_' and not '-'))
            return NombreCaracteres;
        if (SeparadoresConsecutivos().IsMatch(value)) return NombreSeparadores;
        if (value[^1] is '.' or '_' or '-') return NombreTerminacion;
        if (!NombreCompleto().IsMatch(value)) return NombreCaracteres;
        return string.Empty;
    }

    public static string ValidateCorreo(string? correo)
    {
        var email = (correo ?? string.Empty).Trim();
        if (email.Length == 0) return CorreoVacio;
        if (email.Length > CorreoMax) return CorreoLongitud;
        if (!CorreoFormatoRegex().IsMatch(email)) return CorreoFormato;
        return string.Empty;
    }

    public static bool TryValidateNombre(string? nombre, out string error)
    {
        error = ValidateNombre(nombre);
        return error.Length == 0;
    }

    public static bool TryValidateCorreo(string? correo, out string error)
    {
        error = ValidateCorreo(correo);
        return error.Length == 0;
    }

    [GeneratedRegex(@"[._-]{2,}")]
    private static partial Regex SeparadoresConsecutivos();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)*$")]
    private static partial Regex NombreCompleto();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex CorreoFormatoRegex();
}
