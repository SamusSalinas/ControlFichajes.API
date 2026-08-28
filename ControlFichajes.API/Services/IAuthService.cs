using ControlFichajes.API.DTOs;

namespace   ControlFichajes.API.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto?> LoginAsync(LoginRequestDto loginDto);
        Task<AuthResponseDto?> RegistrarUsuarioAsync(UsuarioRegistroDto registroDto, bool bootstrap);
    }
}