using ControlFichajes.API.Security;

namespace ControlFichajes.API.Services;

public interface IConfiguracionCorreoActiva
{
    ConexionSmtp Efectiva();

    ConexionSmtp Preparar(ConexionSmtp pedida, bool cambiarContrasena, string? nueva);
}

public interface ICorreoConConexion
{
    Task EnviarAsync(ConexionSmtp conexion, CorreoSaliente mensaje, CancellationToken cancellationToken = default);
}

public enum EstadoGuardadoCorreo
{
    Guardada,
    Invalida,
    SinClave,
    Error
}

public sealed class ResultadoGuardadoCorreo
{
    public EstadoGuardadoCorreo Estado { get; init; }
    public ConexionSmtp? Conexion { get; init; }
    public IReadOnlyDictionary<string, string> Errores { get; init; } = new Dictionary<string, string>();
}
