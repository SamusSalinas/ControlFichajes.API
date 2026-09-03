using ControlFichajes.API.DTOs;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IConfiguration _configuration;

        public AuthController(IAuthService authService, IConfiguration configuration)
        {
            _authService = authService;
            _configuration = configuration;
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

        [HttpPost("agente")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginAgente([FromBody] AgenteLoginDto request)
        {
            var response = await _authService.LoginAgenteAsync(request);
            return response == null
                ? Unauthorized(new { mensaje = "Credenciales de agente incorrectas" })
                : Ok(response);
        }

        [HttpPost("bootstrap")]
        [AllowAnonymous]
        public async Task<IActionResult> Bootstrap(UsuarioRegistroDto request, [FromHeader(Name = "X-Bootstrap-Secret")] string? bootstrapSecret)
        {
            var configuredSecret = _configuration["Bootstrap:Secret"];
            if (string.IsNullOrWhiteSpace(configuredSecret) || string.IsNullOrWhiteSpace(bootstrapSecret) ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(configuredSecret), Encoding.UTF8.GetBytes(bootstrapSecret)))
                return Unauthorized(new { mensaje = "Bootstrap no habilitado." });

            var response = await _authService.RegistrarUsuarioAsync(request, bootstrap: true);
            if (response == null)
                return Conflict(new { mensaje = "El registro inicial ya fue realizado o los datos no son válidos." });

            return Ok(response);
        }

    }
}