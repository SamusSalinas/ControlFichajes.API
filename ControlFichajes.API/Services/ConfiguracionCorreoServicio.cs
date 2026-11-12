using System.Text.Json;
using ControlFichajes.API.Security;

namespace ControlFichajes.API.Services;

public sealed class ConfiguracionCorreoServicio : IConfiguracionCorreoActiva
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IConfiguration _configuration;
    private readonly ProtectorContrasenaSmtp _protector;
    private readonly string? _archivo;
    private readonly ILogger<ConfiguracionCorreoServicio> _logger;
    private readonly object _puerta = new();

    public ConfiguracionCorreoServicio(
        IConfiguration configuration,
        ProtectorContrasenaSmtp protector,
        ILogger<ConfiguracionCorreoServicio> logger,
        string? archivo = null)
    {
        _configuration = configuration;
        _protector = protector;
        _logger = logger;
        _archivo = string.IsNullOrWhiteSpace(archivo) ? null : archivo;
    }

    public ConexionSmtp Efectiva()
    {
        var guardada = Leer();
        return guardada ?? ConexionSmtpReglas.DesdeConfiguracion(_configuration);
    }

    public ConexionSmtp Preparar(ConexionSmtp pedida, bool cambiarContrasena, string? nueva)
    {
        var contrasena = cambiarContrasena && !string.IsNullOrEmpty(nueva)
            ? nueva
            : Efectiva().Contrasena;
        return pedida with { Contrasena = contrasena ?? string.Empty, Guardada = false };
    }

    public ResultadoGuardadoCorreo Guardar(ConexionSmtp pedida, bool cambiarContrasena, string? nueva)
    {
        var conexion = Preparar(pedida, cambiarContrasena, nueva);
        var errores = ConexionSmtpReglas.Validar(conexion, _configuration);
        if (errores.Count > 0)
            return new ResultadoGuardadoCorreo { Estado = EstadoGuardadoCorreo.Invalida, Errores = errores };

        if (conexion.Contrasena.Length > 0 && !_protector.Disponible)
            return new ResultadoGuardadoCorreo { Estado = EstadoGuardadoCorreo.SinClave };

        if (string.IsNullOrWhiteSpace(_archivo))
            return new ResultadoGuardadoCorreo { Estado = EstadoGuardadoCorreo.Error };

        try
        {
            var sobre = conexion.Contrasena.Length == 0 ? string.Empty : _protector.Proteger(conexion.Contrasena);
            var documento = new Documento
            {
                Servidor = conexion.Servidor.Trim(),
                Puerto = conexion.Puerto,
                Cifrado = conexion.Cifrado,
                Usuario = conexion.Usuario.Trim(),
                Remitente = conexion.Remitente.Trim(),
                ContrasenaCifrada = sobre
            };
            var temporal = _archivo + ".tmp";
            lock (_puerta)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_archivo)!);
                File.WriteAllText(temporal, JsonSerializer.Serialize(documento, Json));
                File.Move(temporal, _archivo, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProteccionSecretoNoDisponibleException)
        {
            _logger.LogWarning(
                "No se pudo guardar la configuración de correo. Evento={Evento} Excepcion={Excepcion}",
                "correo_configuracion_rechazada",
                ex.GetType().Name);
            return new ResultadoGuardadoCorreo { Estado = EstadoGuardadoCorreo.Error };
        }

        return new ResultadoGuardadoCorreo
        {
            Estado = EstadoGuardadoCorreo.Guardada,
            Conexion = Efectiva()
        };
    }

    private ConexionSmtp? Leer()
    {
        if (string.IsNullOrWhiteSpace(_archivo) || !File.Exists(_archivo))
            return null;

        try
        {
            var documento = JsonSerializer.Deserialize<Documento>(File.ReadAllText(_archivo), Json);
            if (documento == null)
                return null;
            var contrasena = string.Empty;
            if (!string.IsNullOrWhiteSpace(documento.ContrasenaCifrada))
                contrasena = _protector.Abrir(documento.ContrasenaCifrada);
            return new ConexionSmtp
            {
                Servidor = documento.Servidor?.Trim() ?? string.Empty,
                Puerto = documento.Puerto,
                Cifrado = documento.Cifrado ?? string.Empty,
                Usuario = documento.Usuario?.Trim() ?? string.Empty,
                Remitente = documento.Remitente?.Trim() ?? string.Empty,
                Contrasena = contrasena,
                Guardada = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "No se pudo leer la configuración de correo guardada. Evento={Evento} Excepcion={Excepcion}",
                "correo_configuracion_rechazada",
                ex.GetType().Name);
            return null;
        }
    }

    private sealed class Documento
    {
        public string? Servidor { get; set; }
        public int Puerto { get; set; }
        public string? Cifrado { get; set; }
        public string? Usuario { get; set; }
        public string? Remitente { get; set; }
        public string? ContrasenaCifrada { get; set; }
    }
}
