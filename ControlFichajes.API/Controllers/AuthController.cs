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
        private readonly IVerificacionMfaService? _mfa;
        private readonly IEnrolamientoMfaService? _enrolamiento;

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
            IHostEnvironment? entorno = null,
            IVerificacionMfaService? mfa = null,
            IEnrolamientoMfaService? enrolamiento = null)
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
            _mfa = mfa;
            _enrolamiento = enrolamiento;
        }

        [HttpPost("Login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            if (HttpContext == null)
                return await LoginTradicionalAsync(request);

            SinCache();
            var generacion = GeneracionSesion.Leer(Request);
            if (generacion.Estado == EstadoGeneracionSesion.Invalida)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            var clase = ProteccionSolicitudSesion.Evaluar(Request, _configuration);
            if (clase == ClaseSolicitudSesion.Rechazada)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (generacion.Estado == EstadoGeneracionSesion.Valida && clase != ClaseSolicitudSesion.Renovable)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            var modo = clase == ClaseSolicitudSesion.Renovable
                ? ModoSesionMfa.Renovable
                : ModoSesionMfa.Tradicional;
            var ingreso = await _authService.IngresarAsync(request, modo, IdGeneracion(generacion), Ip());
            if (ingreso.Estado == EstadoIngreso.VinculacionPendiente)
            {
                return Unauthorized(new
                {
                    codigo = "vinculacion_mfa_pendiente",
                    desafio = ingreso.Desafio,
                    expiraEn = ingreso.ExpiraEn
                });
            }

            if (ingreso.Estado == EstadoIngreso.VinculacionNoDisponible)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { codigo = "vinculacion_no_disponible" });

            if (ingreso.Estado == EstadoIngreso.MfaRequerido)
            {
                return Unauthorized(new
                {
                    codigo = "mfa_requerido",
                    desafio = ingreso.Desafio,
                    expiraEn = ingreso.ExpiraEn
                });
            }

            if (ingreso.Estado == EstadoIngreso.Limitado)
                return StatusCode(StatusCodes.Status429TooManyRequests, new { mensaje = "Demasiadas solicitudes. Intentá más tarde." });

            if (ingreso.Estado == EstadoIngreso.NoDisponible)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { codigo = "mfa_no_disponible" });

            if (ingreso.Estado != EstadoIngreso.Autenticado || ingreso.Respuesta == null)
                return Unauthorized(new { mensaje = "Credenciales incorrectas" });

            if (clase == ClaseSolicitudSesion.Tradicional)
                return Ok(ingreso.Respuesta);

            return await EmitirSesionAsync(ingreso.Respuesta, generacion);
        }

        [HttpPost("mfa/verificar")]
        [AllowAnonymous]
        public async Task<IActionResult> VerificarMfa([FromBody] VerificarMfaDto? request)
        {
            SinCache();
            var generacion = GeneracionSesion.Leer(Request);
            if (generacion.Estado == EstadoGeneracionSesion.Invalida)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            var clase = ProteccionSolicitudSesion.Evaluar(Request, _configuration);
            if (clase == ClaseSolicitudSesion.Rechazada)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (generacion.Estado == EstadoGeneracionSesion.Valida && clase != ClaseSolicitudSesion.Renovable)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (_mfa == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { codigo = "mfa_no_disponible" });

            var tieneCodigo = !string.IsNullOrWhiteSpace(request?.Codigo);
            var tieneRecuperacion = !string.IsNullOrWhiteSpace(request?.CodigoRecuperacion);
            if (tieneCodigo == tieneRecuperacion)
                return BadRequest(new { codigo = tieneCodigo ? "mfa_codigo_ambiguo" : "mfa_codigo_requerido" });

            var modo = clase == ClaseSolicitudSesion.Renovable
                ? ModoSesionMfa.Renovable
                : ModoSesionMfa.Tradicional;
            var nombre = NombreCookie(generacion);
            var resultado = await _mfa.VerificarAsync(
                request?.Desafio,
                request?.Codigo,
                modo,
                IdGeneracion(generacion),
                Ip(),
                AgenteUsuario(),
                Request.Cookies[nombre],
                request?.CodigoRecuperacion);
            return ResponderMfa(resultado, generacion);
        }

        [HttpGet("mfa/estado")]
        [Authorize]
        public async Task<IActionResult> EstadoMfa()
        {
            SinCache();
            if (ContextoDeLecturaRechazado())
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (!User.HasClaim("token_use", "web") || !TryUsuarioActual(out var usuarioId))
                return Unauthorized(new { mensaje = "Sesión inválida." });

            if (_enrolamiento == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { codigo = "mfa_no_disponible" });

            var estado = await _enrolamiento.ConsultarEstadoAsync(usuarioId);
            var version = User.FindFirstValue("token_version");
            if (estado == null || !int.TryParse(version, out var tokenVersion) || tokenVersion != estado.TokenVersion)
                return Unauthorized(new { mensaje = "Sesión inválida." });

            return Ok(new EstadoMfaDto
            {
                TotpHabilitado = estado.TotpHabilitado,
                MfaObligatorio = estado.MfaObligatorio,
                ConfiguracionDisponible = AuthPolicy.PermitirEnrolamientoMfa(_configuration)
            });
        }

        [HttpPost("mfa/enrolamiento/iniciar")]
        [Authorize]
        public async Task<IActionResult> IniciarEnrolamiento([FromBody] IniciarEnrolamientoMfaDto? request)
        {
            SinCache();
            if (!AuthPolicy.PermitirEnrolamientoMfa(_configuration))
                return NotFound();

            if (ProteccionSolicitudSesion.Evaluar(Request, _configuration) == ClaseSolicitudSesion.Rechazada)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (_enrolamiento == null || !TryUsuarioActual(out var usuarioId))
                return Unauthorized(new { mensaje = "Sesión inválida." });

            var resultado = await _enrolamiento.IniciarAsync(usuarioId, request?.Password, SesionActual(), Ip());
            return ResponderEnrolamiento(resultado, inicio: true);
        }

        [HttpPost("mfa/enrolamiento/confirmar")]
        [Authorize]
        public async Task<IActionResult> ConfirmarEnrolamiento([FromBody] ConfirmarEnrolamientoMfaDto? request)
        {
            SinCache();
            if (!AuthPolicy.PermitirEnrolamientoMfa(_configuration))
                return NotFound();

            if (ProteccionSolicitudSesion.Evaluar(Request, _configuration) == ClaseSolicitudSesion.Rechazada)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (_enrolamiento == null || !TryUsuarioActual(out var usuarioId))
                return Unauthorized(new { mensaje = "Sesión inválida." });

            var resultado = await _enrolamiento.ConfirmarAsync(usuarioId, request?.Operacion, request?.Codigo, SesionActual(), Ip());
            return ResponderEnrolamiento(resultado, inicio: false);
        }

        [HttpPost("mfa/vinculacion/iniciar")]
        [AllowAnonymous]
        public async Task<IActionResult> IniciarVinculacion([FromBody] IniciarVinculacionMfaDto? request)
        {
            SinCache();
            if (!TryContextoVinculacion(out var modo, out var generacion, out var rechazo))
                return rechazo!;

            if (_enrolamiento == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { codigo = "mfa_no_disponible" });

            var resultado = await _enrolamiento.IniciarVinculacionAsync(request?.Desafio, modo, generacion, Ip());
            return ResponderEnrolamiento(resultado, inicio: true);
        }

        [HttpPost("mfa/vinculacion/confirmar")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmarVinculacion([FromBody] ConfirmarVinculacionMfaDto? request)
        {
            SinCache();
            if (!TryContextoVinculacion(out var modo, out var generacion, out var rechazo))
                return rechazo!;

            if (_enrolamiento == null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { codigo = "mfa_no_disponible" });

            var resultado = await _enrolamiento.ConfirmarVinculacionAsync(
                request?.Desafio,
                request?.Operacion,
                request?.Codigo,
                modo,
                generacion,
                Ip());
            return ResponderEnrolamiento(resultado, inicio: false);
        }

        [HttpPost("mfa/recuperacion/regenerar")]
        [Authorize]
        public async Task<IActionResult> RegenerarCodigos([FromBody] RegenerarCodigosMfaDto? request)
        {
            SinCache();
            if (!AuthPolicy.PermitirEnrolamientoMfa(_configuration))
                return NotFound();

            if (ProteccionSolicitudSesion.Evaluar(Request, _configuration) == ClaseSolicitudSesion.Rechazada)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            var tieneCodigo = !string.IsNullOrWhiteSpace(request?.Codigo);
            var tieneRecuperacion = !string.IsNullOrWhiteSpace(request?.CodigoRecuperacion);
            if (tieneCodigo == tieneRecuperacion)
                return BadRequest(new { codigo = tieneCodigo ? "mfa_codigo_ambiguo" : "mfa_codigo_requerido" });

            if (_enrolamiento == null || !TryUsuarioActual(out var usuarioId))
                return Unauthorized(new { mensaje = "Sesión inválida." });

            var resultado = await _enrolamiento.RegenerarAsync(
                usuarioId,
                request?.Password,
                request?.Codigo,
                request?.CodigoRecuperacion,
                Ip());
            return ResponderEnrolamiento(resultado, inicio: false);
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        [EnableRateLimiting(PoliticaLimiteRefresh)]
        public async Task<IActionResult> Refresh()
        {
            SinCache();
            var generacion = GeneracionSesion.Leer(Request);
            if (generacion.Estado == EstadoGeneracionSesion.Invalida)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (ProteccionSolicitudSesion.Evaluar(Request, _configuration) != ClaseSolicitudSesion.Renovable)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (_sesiones == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo renovar la sesión." });

            var nombre = NombreCookie(generacion);
            var resultado = await _sesiones.RotarAsync(
                Request.Cookies[nombre],
                Ip(),
                AgenteUsuario(),
                IdGeneracion(generacion));
            if (resultado.Estado == EstadoSesionWeb.Error)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo renovar la sesión." });

            if (resultado.Estado != EstadoSesionWeb.Rotada || resultado.Respuesta == null || resultado.Refresh == null || resultado.ExpiraEn == null)
            {
                if (resultado.Estado == EstadoSesionWeb.Reutilizada)
                    BorrarCookie(nombre);

                return Unauthorized(new { mensaje = "Sesión inválida." });
            }

            EscribirCookie(nombre, resultado.Refresh, resultado.ExpiraEn.Value);
            return Ok(resultado.Respuesta);
        }

        [HttpPost("cerrar-sesion")]
        [AllowAnonymous]
        [EnableRateLimiting(PoliticaLimiteCierre)]
        public async Task<IActionResult> CerrarSesion()
        {
            SinCache();
            var generacion = GeneracionSesion.Leer(Request);
            if (generacion.Estado == EstadoGeneracionSesion.Invalida)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (ProteccionSolicitudSesion.Evaluar(Request, _configuration) != ClaseSolicitudSesion.Renovable)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (_sesiones == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo cerrar la sesión." });

            var nombre = NombreCookie(generacion);
            var presente = Request.Cookies[nombre];
            var resultado = await _sesiones.CerrarAsync(presente, IdGeneracion(generacion));
            if (resultado.Estado == EstadoSesionWeb.Error)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo cerrar la sesión." });

            if (resultado.Estado == EstadoSesionWeb.Rechazada)
                return Unauthorized(new { mensaje = "Sesión inválida." });

            if (generacion.Estado != EstadoGeneracionSesion.Valida || presente != null)
                BorrarCookie(nombre);

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

        private IActionResult ResponderMfa(ResultadoVerificacionMfa resultado, LecturaGeneracionSesion generacion)
        {
            switch (resultado.Estado)
            {
                case EstadoVerificacionMfa.Limitado:
                    return StatusCode(StatusCodes.Status429TooManyRequests, new { mensaje = "Demasiadas solicitudes. Intentá más tarde." });
                case EstadoVerificacionMfa.NoDisponible:
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new { codigo = "mfa_no_disponible" });
                case EstadoVerificacionMfa.Contexto:
                    return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });
                case EstadoVerificacionMfa.Bloqueado:
                    return Unauthorized(new { codigo = "mfa_bloqueado" });
                case EstadoVerificacionMfa.Ambiguo:
                    return BadRequest(new { codigo = "mfa_codigo_ambiguo" });
                case EstadoVerificacionMfa.Error:
                    return StatusCode(StatusCodes.Status500InternalServerError, new { codigo = "mfa_error" });
                case EstadoVerificacionMfa.Emitido when resultado.Respuesta != null:
                    if (resultado.Renovable && resultado.Refresh != null && resultado.ExpiraRefresh != null)
                        EscribirCookie(NombreCookie(generacion), resultado.Refresh, resultado.ExpiraRefresh.Value);

                    return Ok(resultado.Respuesta);
                default:
                    return Unauthorized(new { codigo = "mfa_invalido" });
            }
        }

        private async Task<IActionResult> LoginTradicionalAsync(LoginRequestDto request)
        {
            var response = await _authService.LoginAsync(request);
            if (response == null)
                return Unauthorized(new { mensaje = "Credenciales incorrectas" });

            return Ok(response);
        }

        private async Task<IActionResult> EmitirSesionAsync(AuthResponseDto response, LecturaGeneracionSesion generacion)
        {
            if (_sesiones == null || !TryLeerUsuarioId(response.Token, out var usuarioId))
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo iniciar la sesión." });

            var identificador = IdGeneracion(generacion);
            var anterior = identificador.HasValue ? null : Request.Cookies[CookieSesion.Nombre];
            var resultado = await _sesiones.EmitirAsync(usuarioId, anterior, Ip(), AgenteUsuario(), identificador);
            if (identificador.HasValue && resultado.Estado == EstadoSesionWeb.Rechazada)
                return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });

            if (resultado.Estado != EstadoSesionWeb.Emitida || resultado.Refresh == null || resultado.ExpiraEn == null || resultado.Respuesta == null)
                return StatusCode(StatusCodes.Status500InternalServerError, new { mensaje = "No se pudo iniciar la sesión." });

            EscribirCookie(NombreCookie(generacion), resultado.Refresh, resultado.ExpiraEn.Value);
            return Ok(resultado.Respuesta);
        }

        private IActionResult ResponderEnrolamiento(ResultadoEnrolamiento resultado, bool inicio)
        {
            switch (resultado.Estado)
            {
                case EstadoEnrolamiento.Deshabilitado:
                    return NotFound();
                case EstadoEnrolamiento.Limitado:
                    return StatusCode(StatusCodes.Status429TooManyRequests, new { mensaje = "Demasiadas solicitudes. Intentá más tarde." });
                case EstadoEnrolamiento.NoDisponible:
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new { codigo = "mfa_no_disponible" });
                case EstadoEnrolamiento.Contexto:
                    return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });
                case EstadoEnrolamiento.Restringido:
                    return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Debés cambiar tu contraseña antes de continuar." });
                case EstadoEnrolamiento.YaConfigurado:
                    return Conflict(new { codigo = "mfa_ya_configurado" });
                case EstadoEnrolamiento.Bloqueado:
                    return Unauthorized(new { codigo = "mfa_bloqueado" });
                case EstadoEnrolamiento.Credencial:
                    return Unauthorized(new { mensaje = "Credenciales incorrectas" });
                case EstadoEnrolamiento.Error:
                    return StatusCode(StatusCodes.Status500InternalServerError, new { codigo = "mfa_error" });
                case EstadoEnrolamiento.Emitido when inicio && resultado.Operacion != null && resultado.Uri != null && resultado.ClaveManual != null:
                    return Ok(new
                    {
                        operacion = resultado.Operacion,
                        uri = resultado.Uri,
                        claveManual = resultado.ClaveManual,
                        expiraEn = resultado.ExpiraEn
                    });
                case EstadoEnrolamiento.Emitido when resultado.Codigos != null && resultado.Mensaje != null:
                    return Ok(new { mensaje = resultado.Mensaje, codigos = resultado.Codigos });
                default:
                    return Unauthorized(new { codigo = "mfa_invalido" });
            }
        }

        private bool TryContextoVinculacion(out string modo, out Guid? generacion, out IActionResult? rechazo)
        {
            modo = ModoSesionMfa.Tradicional;
            generacion = null;
            rechazo = null;
            var lectura = GeneracionSesion.Leer(Request);
            if (lectura.Estado == EstadoGeneracionSesion.Invalida)
            {
                rechazo = StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });
                return false;
            }

            var clase = ProteccionSolicitudSesion.Evaluar(Request, _configuration);
            if (clase == ClaseSolicitudSesion.Rechazada || (lectura.Estado == EstadoGeneracionSesion.Valida && clase != ClaseSolicitudSesion.Renovable))
            {
                rechazo = StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "Solicitud rechazada." });
                return false;
            }

            if (clase == ClaseSolicitudSesion.Renovable)
            {
                modo = ModoSesionMfa.Renovable;
                generacion = IdGeneracion(lectura);
            }

            return true;
        }

        private bool ContextoDeLecturaRechazado()
        {
            if (ProteccionSolicitudSesion.Evaluar(Request, _configuration) != ClaseSolicitudSesion.Rechazada)
                return false;

            if (!Request.Headers.TryGetValue(ProteccionSolicitudSesion.Encabezado, out var valores) ||
                valores.Count != 1 ||
                !string.Equals(valores[0], ProteccionSolicitudSesion.Valor, StringComparison.Ordinal))
                return true;

            return Request.Headers.ContainsKey("Origin");
        }

        private bool TryUsuarioActual(out int usuarioId)
        {
            usuarioId = 0;
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(claim, out usuarioId) && usuarioId > 0;
        }

        private Guid? SesionActual()
        {
            var sid = User.FindFirstValue("sid");
            return Guid.TryParse(sid, out var valor) ? valor : null;
        }

        private static Guid? IdGeneracion(LecturaGeneracionSesion generacion)
        {
            return generacion.Estado == EstadoGeneracionSesion.Valida ? generacion.Valor : null;
        }

        private static string NombreCookie(LecturaGeneracionSesion generacion)
        {
            return generacion.Estado == EstadoGeneracionSesion.Valida
                ? CookieSesion.NombreDe(generacion.Valor)
                : CookieSesion.Nombre;
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

        private void EscribirCookie(string nombre, string refresh, DateTime expiraEn)
        {
            CookieSesion.Escribir(
                Response,
                nombre,
                refresh,
                expiraEn,
                CookieSecure(),
                CookieSesion.Modo(_configuration),
                TimeSpan.FromDays(AuthPolicy.RefreshExpireDays(_configuration)));
        }

        private void BorrarCookie(string nombre)
        {
            CookieSesion.Borrar(Response, nombre, CookieSecure(), CookieSesion.Modo(_configuration));
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