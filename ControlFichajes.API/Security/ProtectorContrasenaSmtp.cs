using System.Security.Cryptography;
using System.Text;

namespace ControlFichajes.API.Security;

public sealed class ProtectorContrasenaSmtp
{
    private const int Version = 1;
    private readonly IConfiguration _configuration;

    public ProtectorContrasenaSmtp(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool Disponible => TryClave(out _);

    public string Proteger(string plano)
    {
        if (!TryClave(out var clave))
            throw new ProteccionSecretoNoDisponibleException();

        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var claro = Encoding.UTF8.GetBytes(plano);
        var cifrado = new byte[claro.Length];
        var etiqueta = new byte[16];
        using (var aes = new AesGcm(clave, 16))
        {
            aes.Encrypt(nonce, claro, cifrado, etiqueta, Datos());
        }

        return string.Join('.',
            "v" + Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(cifrado),
            Convert.ToBase64String(etiqueta));
    }

    public string Abrir(string sobre)
    {
        if (!TryClave(out var clave))
            throw new ProteccionSecretoNoDisponibleException();

        var partes = sobre.Split('.');
        if (partes.Length != 4 || partes[0] != "v" + Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new ProteccionSecretoRechazadaException();

        try
        {
            var nonce = Convert.FromBase64String(partes[1]);
            var cifrado = Convert.FromBase64String(partes[2]);
            var etiqueta = Convert.FromBase64String(partes[3]);
            var claro = new byte[cifrado.Length];
            using var aes = new AesGcm(clave, 16);
            aes.Decrypt(nonce, cifrado, etiqueta, claro, Datos());
            return Encoding.UTF8.GetString(claro);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            throw new ProteccionSecretoRechazadaException();
        }
    }

    private bool TryClave(out byte[] clave)
    {
        clave = [];
        var texto = _configuration["Smtp:ProtectionKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(texto))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(texto);
            if (bytes.Length != 32)
                return false;
            clave = bytes;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Datos() => Encoding.UTF8.GetBytes("smtp-panel-v1");
}
