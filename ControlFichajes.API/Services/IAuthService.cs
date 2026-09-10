using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;

namespace   ControlFichajes.API.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto?> LoginAsync(LoginRequestDto loginDto);
        Task<AuthResponseDto?> RegistrarUsuarioAsync(UsuarioRegistroDto registroDto, bool bootstrap);
        Task<IEnumerable<UsuarioListItemDto>> ListarUsuariosAsync(int? empresaId, string? rol, string? nombreUsuario, string? correo, bool? activo);
        Task<Usuario?> GetUsuarioByIdAsync(int usuarioId);
        Task<RestablecerPasswordResponseDto?> RestablecerPasswordAsync(int usuarioId);
        Task<bool> CambiarPasswordAsync(int usuarioId, CambiarPasswordRequestDto request);
        Task<bool> DesbloquearAsync(int usuarioId);
    }
}