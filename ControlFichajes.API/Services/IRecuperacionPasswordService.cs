namespace ControlFichajes.API.Services;

public interface IRecuperacionPasswordService
{
    Task SolicitarAsync(string correoNormalizado, CancellationToken cancellationToken = default);

    Task<RestablecimientoPassword> RestablecerAsync(string token, string password, CancellationToken cancellationToken = default);
}

public enum EstadoRestablecimiento
{
    Restablecida,
    Rechazada,
    Error
}

public sealed class RestablecimientoPassword
{
    public EstadoRestablecimiento Estado { get; init; }
}
