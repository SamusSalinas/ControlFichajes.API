using System.Security.Claims;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
        private readonly IInvitacionUsuarioService? _invitaciones;
        private readonly IRecuperacionPasswordService? _recuperacion;

        public const string PoliticaLimiteAceptacion = "aceptar-invitacion";
        public const string PoliticaLimiteRecuperacionSolicitud = "recuperacion-solicitar";
        public const string PoliticaLimiteRecuperacionRestablecer = "recuperacion-restablecer";
        public const string MensajeRecuperacionSolicitada = "Si la cuenta puede recuperar su contraseña, recibirás las instrucciones.";

        public AuthController(
            IAuthService authService,
            IAgenteAuthService agenteAuthService,
            IConfiguration? configuration = null,
            IAuditoriaSeguridad? auditoria = null,
            ILogger<AuthController>? logger = null,
            IInvitacionUsuarioService? invitaciones = null,
            IRecuperacionPasswordService? recuperacion = null)
        {
            _authService = authService;
            _agenteAuthService = agenteAuthService;
            _configuration = configuration;
            _auditoria = auditoria;
            _logger = logger;
            _invitaciones = invitaciones;
            _recuperacion = recuperacion;
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

        [HttpPost("invitacion/aceptar")]
        [AllowAnonymous]
        [EnableRateLimiting(PoliticaLimiteAceptacion)]
        public async Task<IActionResult> AceptarInvitacion([FromBody] AceptarInvitacionDto? request)
        {
            if (_invitaciones == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo aceptar la invitación." });

            if (request == null
                || !PasswordPolitica.EsElegible(request.Password)
                || !string.Equals(request.Password, request.ConfirmarPassword, StringComparison.Ordinal))
            {
                return BadRequest(new { mensaje = "La contraseña no cumple las reglas o no coincide." });
            }

            var resultado = await _invitaciones.AceptarAsync(request.Token, request.Password);
            return resultado.Estado switch
            {
                EstadoAceptacion.Aceptada => Ok(new { mensaje = "Invitación aceptada. Ya podés iniciar sesión." }),
                EstadoAceptacion.Error => StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo aceptar la invitación." }),
                _ => BadRequest(new { mensaje = "La invitación no es válida." })
            };
        }

        [HttpPost("recuperacion/solicitar")]
        [AllowAnonymous]
        [EnableRateLimiting(PoliticaLimiteRecuperacionSolicitud)]
        public async Task<IActionResult> SolicitarRecuperacion([FromBody] SolicitarRecuperacionDto? request)
        {
            var correo = CorreoNormalizado.ParaRecuperacion(request?.Correo);
            if (correo == null)
                return BadRequest(new { mensaje = "El correo no es válido." });

            if (_recuperacion == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo procesar la solicitud." });

            await _recuperacion.SolicitarAsync(correo);
            return Ok(new { mensaje = MensajeRecuperacionSolicitada });
        }

        [HttpPost("recuperacion/restablecer")]
        [AllowAnonymous]
        [EnableRateLimiting(PoliticaLimiteRecuperacionRestablecer)]
        public async Task<IActionResult> RestablecerPassword([FromBody] RestablecerPasswordDto? request)
        {
            if (request == null
                || !PasswordPolitica.EsElegible(request.Password)
                || !string.Equals(request.Password, request.ConfirmarPassword, StringComparison.Ordinal))
            {
                return BadRequest(new { mensaje = "La contraseña no cumple las reglas o no coincide." });
            }

            if (_recuperacion == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo restablecer la contraseña." });

            var resultado = await _recuperacion.RestablecerAsync(request.Token, request.Password);
            return resultado.Estado switch
            {
                EstadoRestablecimiento.Restablecida => Ok(new { mensaje = "Contraseña restablecida. Ya podés iniciar sesión." }),
                EstadoRestablecimiento.Error => StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo restablecer la contraseña." }),
                _ => BadRequest(new { mensaje = "La recuperación no es válida." })
            };
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