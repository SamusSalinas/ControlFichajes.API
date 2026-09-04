using ControlFichajes.API.DTOs;

namespace   ControlFichajes.API.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto?> LoginAsync(LoginRequestDto loginDto);
        Task<AuthResponseDto?> LoginAgenteAsync(AgenteLoginDto loginDto);
        Task<AuthResponseDto?> RegistrarUsuarioAsync(UsuarioRegistroDto registroDto, bool bootstrap);
        Task<UsuarioDto?> ObtenerUsuarioPublicoAsync(string correo);
        Task<AgenteCreadoDto?> CrearAgenteAsync(AgenteCrearDto dto);
        Task<AgenteCreadoDto?> RotarSecretAgenteAsync(int id);
    }
}