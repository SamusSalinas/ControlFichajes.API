using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlFichajes.API.Security;

public interface IProtectorSecretosMfa
{
    string Proteger(int usuarioId, string proposito, ReadOnlySpan<byte> secreto);

    byte[] Abrir(int usuarioId, string proposito, string sobre);
}

public sealed class ProteccionSecretoNoDisponibleException : Exception
{
    public ProteccionSecretoNoDisponibleException()
        : base("proteccion_secreto_no_disponible")
    {
    }
}

public sealed class ProteccionSecretoRechazadaException : Exception
{
    public ProteccionSecretoRechazadaException()
        : base("proteccion_secreto_rechazada")
    {
    }
}

public sealed partial class ProtectorSecretosMfa : IProtectorSecretosMfa
{
    public const string PropositoTotpActivo = "totp-activo";
    public const string PropositoTotpPendiente = "totp-pendiente";
    public const int Version = 1;
    public const int TamanoClave = 32;
    public const int TamanoNonce = 12;
    public const int TamanoEtiqueta = 16;
    public const int LargoMaximoSecreto = 256;
    public const int LargoMaximoSobre = 512;

    private readonly IConfiguration? _configuration;
    private readonly string? _claveActiva;
    private readonly IReadOnlyDictionary<string, byte[]>? _claves;

    public ProtectorSecretosMfa(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    internal ProtectorSecretosMfa(string claveActiva, IReadOnlyDictionary<string, byte[]> claves)
    {
        _claveActiva = claveActiva;
        _claves = claves;
    }

    public string Proteger(int usuarioId, string proposito, ReadOnlySpan<byte> secreto)
    {
        ValidarPedido(usuarioId, proposito, secreto.Length);
        var material = Cargar();
        try
        {
            if (!material.Claves.TryGetValue(material.Activa, out var clave))
                throw new ProteccionSecretoNoDisponibleException();

            var nonce = new byte[TamanoNonce];
            RandomNumberGenerator.Fill(nonce);
            var cifrado = new byte[secreto.Length];
            var etiqueta = new byte[TamanoEtiqueta];
            using (var aes = new AesGcm(clave, TamanoEtiqueta))
            {
                aes.Encrypt(nonce, secreto, cifrado, etiqueta, DatosAsociados(usuarioId, proposito));
            }

            var sobre = string.Join('.',
                "v" + Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                material.Activa,
                Codificar(nonce),
                Codificar(cifrado),
                Codificar(etiqueta));
            if (sobre.Length > LargoMaximoSobre)
                throw new ProteccionSecretoRechazadaException();

            return sobre;
        }
        finally
        {
            material.Limpiar();
        }
    }

    public byte[] Abrir(int usuarioId, string proposito, string sobre)
    {
        if (string.IsNullOrWhiteSpace(sobre) || sobre.Length > LargoMaximoSobre)
            throw new ProteccionSecretoRechazadaException();

        var partes = sobre.Split('.');
        if (partes.Length != 5 || partes[0] != "v" + Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new ProteccionSecretoRechazadaException();

        ValidarPedido(usuarioId, proposito, 1);
        if (!Identificador().IsMatch(partes[1]))
            throw new ProteccionSecretoRechazadaException();

        var material = Cargar();
        try
        {
            if (!material.Claves.TryGetValue(partes[1], out var clave))
                throw new ProteccionSecretoRechazadaException();

            byte[] nonce;
            byte[] cifrado;
            byte[] etiqueta;
            try
            {
                nonce = Decodificar(partes[2], TamanoNonce);
                etiqueta = Decodificar(partes[4], TamanoEtiqueta);
                cifrado = Decodificar(partes[3], null);
            }
            catch (FormatException)
            {
                throw new ProteccionSecretoRechazadaException();
            }

            if (cifrado.Length == 0 || cifrado.Length > LargoMaximoSecreto)
                throw new ProteccionSecretoRechazadaException();

            var plano = new byte[cifrado.Length];
            try
            {
                using var aes = new AesGcm(clave, TamanoEtiqueta);
                aes.Decrypt(nonce, cifrado, etiqueta, plano, DatosAsociados(usuarioId, proposito));
                return plano;
            }
            catch (CryptographicException)
            {
                CryptographicOperations.ZeroMemory(plano);
                throw new ProteccionSecretoRechazadaException();
            }
        }
        finally
        {
            material.Limpiar();
        }
    }

    private Material Cargar()
    {
        if (_claves != null && _claveActiva != null)
        {
            if (!MaterialValido(_claveActiva, _claves))
                throw new ProteccionSecretoNoDisponibleException();

            return new Material(_claveActiva, _claves, limpiar: false);
        }

        var seccion = _configuration?.GetSection("Auth:TotpClaves");
        var activa = seccion?["Activa"]?.Trim() ?? string.Empty;
        var claves = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var hijo in seccion?.GetSection("Lectura").GetChildren() ?? [])
        {
            if (!Identificador().IsMatch(hijo.Key) || string.IsNullOrWhiteSpace(hijo.Value))
            {
                Limpiar(claves);
                throw new ProteccionSecretoNoDisponibleException();
            }

            if (!TryDecodificarClave(hijo.Value, out var clave))
            {
                Limpiar(claves);
                throw new ProteccionSecretoNoDisponibleException();
            }

            if (!claves.TryAdd(hijo.Key, clave))
            {
                CryptographicOperations.ZeroMemory(clave);
                Limpiar(claves);
                throw new ProteccionSecretoNoDisponibleException();
            }
        }

        if (!MaterialValido(activa, claves))
        {
            Limpiar(claves);
            throw new ProteccionSecretoNoDisponibleException();
        }

        return new Material(activa, claves, limpiar: true);
    }

    private static bool MaterialValido(string activa, IReadOnlyDictionary<string, byte[]> claves)
    {
        if (!Identificador().IsMatch(activa) || claves.Count == 0 || !claves.ContainsKey(activa))
            return false;

        foreach (var par in claves)
        {
            if (!Identificador().IsMatch(par.Key) || par.Value.Length != TamanoClave)
                return false;
        }

        return true;
    }

    private static void ValidarPedido(int usuarioId, string proposito, int largo)
    {
        if (usuarioId <= 0 || largo <= 0 || largo > LargoMaximoSecreto || !Proposito().IsMatch(proposito ?? string.Empty))
            throw new ProteccionSecretoRechazadaException();
    }

    private static byte[] DatosAsociados(int usuarioId, string proposito)
    {
        return Encoding.ASCII.GetBytes(
            usuarioId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + proposito);
    }

    private static bool TryDecodificarClave(string texto, out byte[] clave)
    {
        try
        {
            var normal = texto.Trim().Replace('-', '+').Replace('_', '/');
            var falta = (4 - (normal.Length % 4)) % 4;
            if (falta > 0)
                normal += new string('=', falta);
            clave = Convert.FromBase64String(normal);
            if (clave.Length != TamanoClave)
            {
                CryptographicOperations.ZeroMemory(clave);
                clave = [];
                return false;
            }

            return true;
        }
        catch (FormatException)
        {
            clave = [];
            return false;
        }
    }

    private static string Codificar(ReadOnlySpan<byte> datos)
    {
        return Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Decodificar(string texto, int? largo)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Contains('=') || texto.Contains('+') || texto.Contains('/'))
            throw new FormatException();

        var falta = (4 - (texto.Length % 4)) % 4;
        var normal = texto.Replace('-', '+').Replace('_', '/');
        if (falta > 0)
            normal += new string('=', falta);

        var bytes = Convert.FromBase64String(normal);
        if (largo.HasValue && bytes.Length != largo.Value)
            throw new FormatException();

        return bytes;
    }

    private static void Limpiar(Dictionary<string, byte[]> claves)
    {
        foreach (var clave in claves.Values)
            CryptographicOperations.ZeroMemory(clave);
        claves.Clear();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identificador();

    [GeneratedRegex("^[a-z0-9-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Proposito();

    private readonly struct Material
    {
        private readonly bool _limpiar;

        public Material(string activa, IReadOnlyDictionary<string, byte[]> claves, bool limpiar)
        {
            Activa = activa;
            Claves = claves;
            _limpiar = limpiar;
        }

        public string Activa { get; }

        public IReadOnlyDictionary<string, byte[]> Claves { get; }

        public void Limpiar()
        {
            if (_limpiar && Claves is Dictionary<string, byte[]> propias)
                ProtectorSecretosMfa.Limpiar(propias);
        }
    }
}
