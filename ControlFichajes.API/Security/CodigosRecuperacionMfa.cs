using System.Security.Cryptography;
using System.Text;
using OtpNet;

namespace ControlFichajes.API.Security;

public static class CodigosRecuperacionMfa
{
    public const int Cantidad = 10;
    public const int EntropiaBytes = 16;
    public const int Caracteres = 26;

    public static IReadOnlyList<string> Generar()
    {
        var codigos = new string[Cantidad];
        for (var i = 0; i < Cantidad; i++)
            codigos[i] = Presentar(RandomNumberGenerator.GetBytes(EntropiaBytes));

        return codigos;
    }

    public static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var buffer = new StringBuilder(valor.Length);
        foreach (var caracter in valor.Trim())
        {
            if (caracter is '-' or ' ')
                continue;

            buffer.Append(char.ToUpperInvariant(caracter));
        }

        if (buffer.Length != Caracteres)
            return null;

        foreach (var caracter in buffer.ToString())
        {
            if (caracter is not (>= 'A' and <= 'Z' or >= '2' and <= '7'))
                return null;
        }

        return buffer.ToString();
    }

    public static string Hash(string normalizado)
    {
        return TokenOpaco.Hash(normalizado);
    }

    public static string Presentar(byte[] entropia)
    {
        var texto = Base32Encoding.ToString(entropia).TrimEnd('=');
        if (texto.Length != Caracteres)
            throw new InvalidOperationException("codigo_recuperacion");

        return Agrupar(texto);
    }

    public static string Agrupar(string base32)
    {
        var partes = new List<string>();
        for (var i = 0; i < base32.Length; i += 4)
            partes.Add(base32.Substring(i, Math.Min(4, base32.Length - i)));

        return string.Join('-', partes);
    }
}
