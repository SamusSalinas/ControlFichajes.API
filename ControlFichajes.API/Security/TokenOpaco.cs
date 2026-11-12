using System.Security.Cryptography;
using System.Text;

namespace ControlFichajes.API.Security;

public static class TokenOpaco
{
    public const int Bytes = 32;

    public static string Crear()
    {
        return Base64Url(RandomNumberGenerator.GetBytes(Bytes));
    }

    public static string Hash(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
