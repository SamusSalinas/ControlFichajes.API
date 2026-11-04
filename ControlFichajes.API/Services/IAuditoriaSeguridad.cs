namespace ControlFichajes.API.Services;

public interface IAuditoriaSeguridad
{
    Task RegistrarAsync(
        string evento,
        string resultado,
        int? usuarioId,
        string? correo,
        string? detalle,
        CancellationToken cancellationToken = default);
}
