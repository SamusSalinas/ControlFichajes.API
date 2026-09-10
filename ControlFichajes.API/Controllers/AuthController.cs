using System.Security.Claims;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IAgenteService _agenteService;

        public AuthController(IAuthService authService, IAgenteService agenteService)
        {
            _authService = authService;
            _agenteService = agenteService;
        }

        [HttpPost("Login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            var response = await _authService.LoginAsync(request);
            if (response == null)
            {
                return Unauthorized(new { mensaje = "Credenciales incorrectas"});
            }

             return Ok(response);
        }

        [HttpPost("bootstrap")]
        [AllowAnonymous]
        public async Task<IActionResult> Bootstrap(UsuarioRegistroDto request)
        {
            var response = await _authService.RegistrarUsuarioAsync(request, bootstrap: true);
            if (response == null)
                return Conflict(new { mensaje = "El registro inicial ya fue realizado o los datos no son válidos." });

            return Ok(response);
        }

        [HttpPost("agente")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginAgente(AgenteLoginDto request)
        {
            var token = await _agenteService.AutenticarAsync(request);
            return token == null
                ? Unauthorized(new { mensaje = "Credenciales de agente incorrectas." })
                : Ok(new AuthResponseDto { Token = token, Mensaje = "Autenticación exitosa" });
        }

        [HttpPost("cambiar-password")]
        [Authorize]
        public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordRequestDto request)
        {
            var usuarioIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(usuarioIdClaim, out var usuarioId))
                return Unauthorized(new { mensaje = "Sesión inválida." });

            var ok = await _authService.CambiarPasswordAsync(usuarioId, request);
            return ok
                ? Ok(new { mensaje = "Contraseña cambiada correctamente." })
                : BadRequest(new { mensaje = "La contraseña nueva no cumple el contrato." });
        }

    }
}