using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers;

[Authorize(Policy = "SoloSuperAdmin")]
[Route("api/correo-saliente")]
[ApiController]
public class CorreoSalienteController : ControllerBase
{
    public const string PoliticaLimitePrueba = "correo-prueba";
    public const string PoliticaLimiteGuardado = "correo-guardar";
    public const string MensajeGuardado = "Configuración de correo guardada.";

    private readonly IConfiguration _configuration;
    private readonly ConfiguracionCorreoServicio _configuracion;
    private readonly ICorreoPruebaService _prueba;
    private readonly AppDbContext _context;
    private readonly IAuditoriaSeguridad _auditoria;

    public CorreoSalienteController(
        IConfiguration configuration,
        ConfiguracionCorreoServicio configuracion,
        ICorreoPruebaService prueba,
        AppDbContext context,
        IAuditoriaSeguridad auditoria)
    {
        _configuration = configuration;
        _configuracion = configuracion;
        _prueba = prueba;
        _context = context;
        _auditoria = auditoria;
    }

    [HttpGet("estado")]
    public IActionResult Estado()
    {
        SinCache();
        if (!PuedeOperar())
            return Unauthorized(new { mensaje = "Sesión inválida." });

        return Ok(Publicar(_configuracion.Efectiva()));
    }

    [HttpPut("configuracion")]
    [EnableRateLimiting(PoliticaLimiteGuardado)]
    public async Task<IActionResult> Guardar([FromBody] GuardarConfiguracionCorreoDto? request)
    {
        SinCache();
        if (!PuedeOperar() || !TryUsuario(out var usuarioId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var operador = await OperadorAsync(usuarioId);
        if (operador == null)
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var resultado = _configuracion.Guardar(Pedir(request), request?.CambiarContrasena == true, request?.Contrasena);
        if (resultado.Estado == EstadoGuardadoCorreo.Invalida)
        {
            await AuditarAsync("correo_configuracion_rechazada", "rechazado", operador.Id, operador.Correo, "validacion");
            return BadRequest(new { mensaje = "Revisá los datos del correo.", errores = resultado.Errores });
        }

        if (resultado.Estado == EstadoGuardadoCorreo.SinClave)
        {
            await AuditarAsync("correo_configuracion_rechazada", "rechazado", operador.Id, operador.Correo, "clave");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { mensaje = "No se puede guardar la contraseña en este equipo." });
        }

        if (resultado.Estado != EstadoGuardadoCorreo.Guardada || resultado.Conexion == null)
        {
            await AuditarAsync("correo_configuracion_rechazada", "rechazado", operador.Id, operador.Correo, "guardado");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { mensaje = "No se pudo guardar la configuración de correo." });
        }

        await AuditarAsync("correo_configuracion_guardada", "aceptado", operador.Id, operador.Correo, "guardado");
        return Ok(new GuardarConfiguracionCorreoRespuestaDto
        {
            Mensaje = MensajeGuardado,
            Configuracion = Publicar(resultado.Conexion)
        });
    }

    [HttpPost("prueba")]
    [EnableRateLimiting(PoliticaLimitePrueba)]
    public async Task<IActionResult> Probar([FromBody] ProbarCorreoSalienteDto? request)
    {
        SinCache();
        if (!PuedeOperar() || !TryUsuario(out var usuarioId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var destinatario = request?.Destinatario?.Trim() ?? string.Empty;
        if (!EstadoCorreoSaliente.CorreoValido(destinatario))
            return BadRequest(new { mensaje = "Ingresá un correo destinatario válido.", errores = new { destinatario = "Ingresá un correo destinatario válido." } });

        var resultado = request?.UsarBorrador == true
            ? await _prueba.EnviarBorradorAsync(
                usuarioId,
                destinatario,
                Pedir(request),
                request.CambiarContrasena,
                request.Contrasena,
                HttpContext.RequestAborted)
            : await _prueba.EnviarAsync(usuarioId, destinatario, HttpContext.RequestAborted);

        return resultado.Estado switch
        {
            EstadoCorreoPrueba.Enviado => Ok(new ProbarCorreoSalienteRespuestaDto
            {
                Mensaje = CorreoPruebaService.MensajeEnviado
            }),
            EstadoCorreoPrueba.Invalido => BadRequest(new { mensaje = "Revisá los datos del correo.", errores = resultado.Errores }),
            EstadoCorreoPrueba.NoConfigurado => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                mensaje = "El correo saliente no está configurado."
            }),
            EstadoCorreoPrueba.Fallido => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                mensaje = "No se pudo enviar el correo de prueba."
            }),
            _ => Unauthorized(new { mensaje = "Sesión inválida." })
        };
    }

    private EstadoCorreoSalienteDto Publicar(ConexionSmtp conexion)
    {
        return new EstadoCorreoSalienteDto
        {
            Servidor = conexion.Servidor,
            Puerto = conexion.Puerto,
            Cifrado = conexion.Cifrado,
            Usuario = conexion.Usuario,
            Remitente = conexion.Remitente,
            ContrasenaConfigurada = conexion.ContrasenaConfigurada,
            Guardada = conexion.Guardada,
            Configurado = ConexionSmtpReglas.Completa(conexion, _configuration)
        };
    }

    private static ConexionSmtp Pedir(GuardarConfiguracionCorreoDto? request)
    {
        return new ConexionSmtp
        {
            Servidor = request?.Servidor?.Trim() ?? string.Empty,
            Puerto = request?.Puerto ?? 0,
            Cifrado = request?.Cifrado?.Trim() ?? string.Empty,
            Usuario = request?.Usuario?.Trim() ?? string.Empty,
            Remitente = request?.Remitente?.Trim() ?? string.Empty
        };
    }

    private static ConexionSmtp Pedir(ProbarCorreoSalienteDto request)
    {
        return new ConexionSmtp
        {
            Servidor = request.Servidor?.Trim() ?? string.Empty,
            Puerto = request.Puerto,
            Cifrado = request.Cifrado?.Trim() ?? string.Empty,
            Usuario = request.Usuario?.Trim() ?? string.Empty,
            Remitente = request.Remitente?.Trim() ?? string.Empty
        };
    }

    private async Task<Usuario?> OperadorAsync(int usuarioId)
    {
        return await _context.Usuario.AsNoTracking().SingleOrDefaultAsync(u => u.Id == usuarioId && u.Activo);
    }

    private async Task AuditarAsync(string evento, string resultado, int usuarioId, string correo, string detalle)
    {
        try
        {
            await _auditoria.RegistrarAsync(evento, resultado, usuarioId, correo, detalle);
        }
        catch (Exception ex)
        {
            // La auditoría no debe impedir la respuesta ni incluir secretos.
            HttpContext.RequestServices.GetService<ILogger<CorreoSalienteController>>()?
                .LogWarning("No se pudo registrar la auditoría de seguridad. Evento={Evento} Excepcion={Excepcion}", evento, ex.GetType().Name);
        }
    }

    private bool PuedeOperar()
    {
        return User.Identity?.IsAuthenticated == true
            && User.HasClaim("token_use", "web")
            && User.IsInRole(AppRoles.SuperAdmin);
    }

    private bool TryUsuario(out int usuarioId)
    {
        usuarioId = 0;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out usuarioId) && usuarioId > 0;
    }

    private void SinCache()
    {
        Response.Headers.CacheControl = "no-store";
    }
}
