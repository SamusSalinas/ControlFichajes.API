using System.Security.Claims;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        private readonly IAgenteAuthService _agenteAuthService;
        private readonly IConfiguration? _configuration;
        private readonly IAuditoriaSeguridad? _auditoria;
        private readonly ILogger<AuthController>? _logger;

        public AuthController(
            IAuthService authService,
            IAgenteAuthService agenteAuthService,
            IConfiguration? configuration = null,
            IAuditoriaSeguridad? auditoria = null,
            ILogger<AuthController>? logger = null)
        {
            _authService = authService;
            _agenteAuthService = agenteAuthService;
            _configuration = configuration;
            _auditoria = auditoria;
            _logger = logger;
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
            if (!AuthPolicy.PermitirBootstrap(_configuration))
            {
                await RegistrarBootstrapDeshabilitadoAsync();
                return NotFound();
            }

            var response = await _authService.RegistrarUsuarioAsync(request, bootstrap: true);
            if (response == null)
                return Conflict(new { mensaje = "El registro inicial ya fue realizado o los datos no son válidos." });

            return Ok(response);
        }

        [HttpPost("agente")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginAgente(AgenteLoginDto request)
        {
            var token = await _agenteAuthService.AutenticarAsync(request);
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
                : BadRequest(new { mensaje = "No se pudo cambiar la contraseña." });
        }

        private async Task RegistrarBootstrapDeshabilitadoAsync()
        {
            if (_auditoria == null)
                return;

            try
            {
                await _auditoria.RegistrarAsync("bootstrap_deshabilitado", "rechazado", null, null, null);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    "No se pudo registrar la auditoría de seguridad. Evento={Evento} Excepcion={Excepcion}",
                    "bootstrap_deshabilitado",
                    ex.GetType().Name);
            }
        }

    }
}