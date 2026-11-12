namespace ControlFichajes.API.Services;

using ControlFichajes.API.Security;

public enum EstadoCorreoPrueba
{
    Enviado,
    NoConfigurado,
    Fallido,
    OperadorAusente,
    Invalido
}

public sealed class ResultadoCorreoPrueba
{
    public EstadoCorreoPrueba Estado { get; init; }
    public IReadOnlyDictionary<string, string> Errores { get; init; } = new Dictionary<string, string>();
}

public interface ICorreoPruebaService
{
    Task<ResultadoCorreoPrueba> EnviarAsync(int usuarioId, string destinatario, CancellationToken cancellationToken = default);

    Task<ResultadoCorreoPrueba> EnviarBorradorAsync(
        int usuarioId,
        string destinatario,
        ConexionSmtp borrador,
        bool cambiarContrasena,
        string? contrasena,
        CancellationToken cancellationToken = default);
}
