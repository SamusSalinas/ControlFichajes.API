using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;

namespace   ControlFichajes.API.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto?> LoginAsync(LoginRequestDto loginDto);
        Task<ResultadoIngreso> IngresarAsync(LoginRequestDto loginDto, string modo, Guid? generacion, string? ip);
        Task<AuthResponseDto?> RegistrarUsuarioAsync(UsuarioRegistroDto registroDto, bool bootstrap);
        Task<IEnumerable<UsuarioListItemDto>> ListarUsuariosAsync(int? empresaId, string? rol, string? nombreUsuario, string? correo, bool? activo);
        Task<Usuario?> GetUsuarioByIdAsync(int usuarioId);
        Task<RestablecerPasswordResponseDto?> RestablecerPasswordAsync(int usuarioId);
        Task<bool> CambiarPasswordAsync(int usuarioId, CambiarPasswordRequestDto request);
        Task<bool> DesbloquearAsync(int usuarioId);
        Task<UsuarioListItemDto?> CambiarEstadoAsync(int usuarioId, bool activo);
        Task<UsuarioListItemDto?> CambiarRolAsync(int usuarioId, string rol);
        Task<IdentidadUpdateResult> CambiarIdentidadAsync(int usuarioId, string? nombreUsuario, string? correo);
        Task<ExigirMfaResult> ExigirMfaAsync(int usuarioId);
        Task<EliminarCuentaResult> EliminarCuentaAsync(int usuarioId);
        Task<EliminarCuentaResult> CancelarInvitacionAsync(int usuarioId);
    }

    public readonly record struct IdentidadUpdateResult(int StatusCode, string Mensaje, UsuarioListItemDto? Usuario);

    public readonly record struct ExigirMfaResult(int StatusCode, string Mensaje, string? Codigo, UsuarioListItemDto? Usuario);

    public readonly record struct EliminarCuentaResult(int StatusCode, string Mensaje, string? Codigo);
}