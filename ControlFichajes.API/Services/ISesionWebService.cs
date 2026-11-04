using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Services;

public enum EstadoSesionWeb
{
    Emitida,
    Rotada,
    Reutilizada,
    Rechazada,
    Cerrada,
    Error
}

public sealed class ResultadoSesionWeb
{
    public EstadoSesionWeb Estado { get; init; }

    public string? Refresh { get; init; }

    public DateTime? ExpiraEn { get; init; }

    public string? FamiliaId { get; init; }

    public AuthResponseDto? Respuesta { get; init; }
}

public interface ISesionWebService
{
    Task<ResultadoSesionWeb> EmitirAsync(
        int usuarioId,
        string? refreshAnterior,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<ResultadoSesionWeb> RotarAsync(
        string? refresh,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<ResultadoSesionWeb> CerrarAsync(
        string? refresh,
        CancellationToken cancellationToken = default);
}
