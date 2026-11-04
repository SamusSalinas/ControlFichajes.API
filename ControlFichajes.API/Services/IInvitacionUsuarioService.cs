using ControlFichajes.API.Models;

namespace ControlFichajes.API.Services;

public interface IInvitacionUsuarioService
{
    Task<InvitacionOperacion> InvitarAsync(int empresaId, string nombreUsuario, string correo, string rol, int creadoPor);

    Task<InvitacionOperacion> ReenviarAsync(int usuarioId, int creadoPor);

    Task<AceptacionInvitacion> AceptarAsync(string token, string password);
}

public enum EstadoAceptacion
{
    Aceptada,
    Rechazada,
    Error
}

public sealed class AceptacionInvitacion
{
    public EstadoAceptacion Estado { get; init; }
}

public enum EstadoInvitacion
{
    Enviada,
    CorreoDuplicado,
    EmpresaInexistente,
    NoEncontrada,
    NoPendiente,
    EnvioFallido
}

public sealed class InvitacionOperacion
{
    public EstadoInvitacion Estado { get; init; }

    public Usuario? Usuario { get; init; }

    public bool Reenvio { get; init; }
}
