namespace ControlFichajes.API.Security;

public static class PasswordPolitica
{
    public const int Minimo = 8;
    public const int Maximo = 20;

    public static bool EsElegible(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < Minimo || password.Length > Maximo)
            return false;

        if (password.Contains(' '))
            return false;

        var tieneLetra = password.Any(char.IsLetter);
        var tieneDigito = password.Any(char.IsDigit);
        var tieneEspecial = password.Any(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch));
        return tieneLetra && tieneDigito && tieneEspecial;
    }
}
