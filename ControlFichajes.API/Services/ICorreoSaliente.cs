namespace ControlFichajes.API.Services;

public interface ICorreoSaliente
{
    Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default);
}

public sealed class CorreoSaliente
{
    public required string Destinatario { get; init; }

    public required string Asunto { get; init; }

    public required string Texto { get; init; }

    public required string Html { get; init; }
}
