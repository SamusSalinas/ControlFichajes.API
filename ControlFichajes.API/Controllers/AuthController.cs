using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
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
        private readonly ISesionWebService? _sesiones;
        private readonly IHostEnvironment? _entorno;

        public const string PoliticaLimiteAceptacion = "aceptar-invitacion";
        public const string PoliticaLimiteRecuperacionSolicitud = "recuperacion-solicitar";
        public const string PoliticaLimiteRecuperacionRestablecer = "recuperacion-restablecer";
        public const string PoliticaLimiteRefresh = "refresh-sesion";
        public const string PoliticaLimiteCierre = "cerrar-sesion";
        public const string MensajeRecuperacionSolicitada = "Si la cuenta puede recuperar su contraseña, recibirás las instrucciones.";

        public AuthController(
            IAuthService authService,
            IAgenteAuthService agenteAuthService,
            IConfiguration? configuration = null,
            IAuditoriaSeguridad? auditoria = null,
            ILogger<AuthController>? logger = null,
            IInvitacionUsuarioService? invitaciones = null,
            IRecuperacionPasswordService? recuperacion = null,
            ISesionWebService? sesiones = null,
            IHostEnvironment? entorno = null)
        {
            _authService = authService;
            _agenteAuthService = agenteAuthService;
            _configuration = configuration;
            _auditoria = auditoria;
            _logger = logger;
            _invitaciones = invitaciones;
            _recuperacion = recuperacion;
            _sesiones = sesiones;
            _entorno = entorno;
        }

        [HttpPost("Login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            if (HttpContext == null)
                return await LoginTradicionalAsync(request);

            SinCache();
            var clase = ProteccionSolicitudSesion.Evaluar(Request, _configuration);
            if (clase == ClaseSolicitudSesion.Rechazada)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            var response = await LoginTradicionalAsync(request);
            if (clase == ClaseSolicitudSesion.Tradicional || response is not OkObjectResult ok)
                return response;

            return await EmitirSesionAsync((AuthResponseDto)ok.Value!);
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        [EnableRateLimiting(PoliticaLimiteRefresh)]
        public async Task<IActionResult> Refresh()
        {
            SinCache();
            if (ProteccionSolicitudSesion.Evaluar(Request, _configuration) != ClaseSolicitudSesion.Renovable)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (_sesiones == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo renovar la sesión." });

            var resultado = await _sesiones.RotarAsync(Request.Cookies[CookieSesion.Nombre], Ip(), AgenteUsuario());
            if (resultado.Estado == EstadoSesionWeb.Error)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo renovar la sesión." });

            if (resultado.Estado != EstadoSesionWeb.Rotada || resultado.Respuesta == null || resultado.Refresh == null || resultado.ExpiraEn == null)
            {
                if (resultado.Estado == EstadoSesionWeb.Reutilizada)
                    BorrarCookie();

                return Unauthorized(new { mensaje = "Sesión inválida." });
            }

            EscribirCookie(resultado.Refresh, resultado.ExpiraEn.Value);
            return Ok(resultado.Respuesta);
        }

        [HttpPost("cerrar-sesion")]
        [AllowAnonymous]
        [EnableRateLimiting(PoliticaLimiteCierre)]
        public async Task<IActionResult> CerrarSesion()
        {
            SinCache();
            if (ProteccionSolicitudSesion.Evaluar(Request, _configuration) != ClaseSolicitudSesion.Renovable)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (_sesiones == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo cerrar la sesión." });

            var resultado = await _sesiones.CerrarAsync(Request.Cookies[CookieSesion.Nombre]);
            if (resultado.Estado == EstadoSesionWeb.Error)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo cerrar la sesión." });

            BorrarCookie();
            return NoContent();
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

        private async Task<IActionResult> LoginTradicionalAsync(LoginRequestDto request)
        {
            var response = await _authService.LoginAsync(request);
            if (response == null)
                return Unauthorized(new { mensaje = "Credenciales incorrectas" });

            return Ok(response);
        }

        private async Task<IActionResult> EmitirSesionAsync(AuthResponseDto response)
        {
            if (_sesiones == null || !TryLeerUsuarioId(response.Token, out var usuarioId))
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo iniciar la sesión." });

            var resultado = await _sesiones.EmitirAsync(usuarioId, Request.Cookies[CookieSesion.Nombre], Ip(), AgenteUsuario());
            if (resultado.Estado != EstadoSesionWeb.Emitida || resultado.Refresh == null || resultado.ExpiraEn == null || resultado.Respuesta == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo iniciar la sesión." });

            EscribirCookie(resultado.Refresh, resultado.ExpiraEn.Value);
            return Ok(resultado.Respuesta);
        }

        private static bool TryLeerUsuarioId(string token, out int usuarioId)
        {
            usuarioId = 0;
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var claim = jwt.Claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.NameIdentifier ||
                c.Type == "nameid" ||
                c.Type == "sub" ||
                c.Type.EndsWith("/nameidentifier", StringComparison.Ordinal));
            return claim != null && int.TryParse(claim.Value, out usuarioId) && usuarioId > 0;
        }

        private void EscribirCookie(string refresh, DateTime expiraEn)
        {
            CookieSesion.Escribir(Response, refresh, expiraEn, CookieSecure(), CookieSesion.Modo(_configuration));
        }

        private void BorrarCookie()
        {
            CookieSesion.Borrar(Response, CookieSecure(), CookieSesion.Modo(_configuration));
        }

        private bool CookieSecure()
        {
            return CookieSesion.Secure(_entorno?.IsDevelopment() == true, Request.IsHttps, _configuration);
        }

        private string? Ip()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }

        private string? AgenteUsuario()
        {
            return Request.Headers.UserAgent.ToString();
        }

        private void SinCache()
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
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