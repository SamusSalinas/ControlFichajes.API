using ControlFichajes.API.DTOs;

namespace ControlFichajes.API.Services;

public enum EstadoIngreso
{
    Invalido,
    Autenticado,
    MfaRequerido,
    Limitado,
    NoDisponible
}

public sealed class ResultadoIngreso
{
    public EstadoIngreso Estado { get; init; }

    public AuthResponseDto? Respuesta { get; init; }

    public string? Desafio { get; init; }

    public DateTime? ExpiraEn { get; init; }
}

public enum EstadoVerificacionMfa
{
    Invalido,
    Bloqueado,
    Contexto,
    NoDisponible,
    Limitado,
    Ambiguo,
    Emitido,
    Error
}

public sealed class ResultadoVerificacionMfa
{
    public EstadoVerificacionMfa Estado { get; init; }

    public AuthResponseDto? Respuesta { get; init; }

    public string? Refresh { get; init; }

    public DateTime? ExpiraRefresh { get; init; }

    public Guid? Generacion { get; init; }

    public bool Renovable { get; init; }
}
