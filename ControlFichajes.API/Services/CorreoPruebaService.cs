using System.Net;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public sealed class CorreoPruebaService : ICorreoPruebaService
{
    public const string Asunto = "Correo de prueba";
    public const string Texto = "Este es un correo de prueba de Control de fichajes.";
    public const string MensajeEnviado = "El servidor de correo aceptó el mensaje. Revisá la bandeja de entrada y spam.";

    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IConfiguracionCorreoActiva _activa;
    private readonly ICorreoSaliente _correo;
    private readonly ICorreoConConexion _directo;
    private readonly IAuditoriaSeguridad _auditoria;
    private readonly ILogger<CorreoPruebaService> _logger;

    public CorreoPruebaService(
        AppDbContext context,
        IConfiguration configuration,
        IConfiguracionCorreoActiva activa,
        ICorreoSaliente correo,
        ICorreoConConexion directo,
        IAuditoriaSeguridad auditoria,
        ILogger<CorreoPruebaService> logger)
    {
        _context = context;
        _configuration = configuration;
        _activa = activa;
        _correo = correo;
        _directo = directo;
        _auditoria = auditoria;
        _logger = logger;
    }

    public async Task<ResultadoCorreoPrueba> EnviarAsync(
        int usuarioId,
        string destinatario,
        CancellationToken cancellationToken = default)
    {
        var operador = await _context.Usuario
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == usuarioId && u.Activo, cancellationToken);
        if (operador == null)
            return new ResultadoCorreoPrueba { Estado = EstadoCorreoPrueba.OperadorAusente };

        if (!ConexionSmtpReglas.Completa(_activa.Efectiva(), _configuration))
        {
            await AuditarAsync("correo_prueba_rechazado", "rechazado", operador.Id, operador.Correo, "configuracion");
            return new ResultadoCorreoPrueba { Estado = EstadoCorreoPrueba.NoConfigurado };
        }

        return await EnviarMensajeAsync(operador, destinatario, _ => _correo.EnviarAsync(MensajeDe(destinatario), cancellationToken));
    }

    public async Task<ResultadoCorreoPrueba> EnviarBorradorAsync(
        int usuarioId,
        string destinatario,
        ConexionSmtp borrador,
        bool cambiarContrasena,
        string? contrasena,
        CancellationToken cancellationToken = default)
    {
        var operador = await _context.Usuario
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == usuarioId && u.Activo, cancellationToken);
        if (operador == null)
            return new ResultadoCorreoPrueba { Estado = EstadoCorreoPrueba.OperadorAusente };

        var conexion = _activa.Preparar(borrador, cambiarContrasena, contrasena);
        var errores = ConexionSmtpReglas.Validar(conexion, _configuration);
        if (errores.Count > 0)
        {
            await AuditarAsync("correo_prueba_rechazado", "rechazado", operador.Id, operador.Correo, "validacion");
            return new ResultadoCorreoPrueba { Estado = EstadoCorreoPrueba.Invalido, Errores = errores };
        }

        return await EnviarMensajeAsync(
            operador,
            destinatario,
            _ => _directo.EnviarAsync(conexion, MensajeDe(destinatario), cancellationToken));
    }

    private async Task<ResultadoCorreoPrueba> EnviarMensajeAsync(
        Usuario operador,
        string destinatario,
        Func<string, Task> enviar)
    {
        try
        {
            await enviar(destinatario);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "No se pudo enviar el correo de prueba. Evento={Evento} Excepcion={Excepcion}",
                "correo_prueba_rechazado",
                ex.GetType().Name);
            await AuditarAsync("correo_prueba_rechazado", "rechazado", operador.Id, operador.Correo, "smtp");
            return new ResultadoCorreoPrueba { Estado = EstadoCorreoPrueba.Fallido };
        }

        await AuditarAsync("correo_prueba_enviado", "aceptado", operador.Id, operador.Correo, null);
        return new ResultadoCorreoPrueba { Estado = EstadoCorreoPrueba.Enviado };
    }

    private static CorreoSaliente MensajeDe(string destinatario)
    {
        return new CorreoSaliente
        {
            Destinatario = destinatario,
            Asunto = Asunto,
            Texto = Texto,
            Html = "<p>" + WebUtility.HtmlEncode(Texto) + "</p>"
        };
    }

    private async Task AuditarAsync(string evento, string resultado, int usuarioId, string correo, string? detalle)
    {
        try
        {
            await _auditoria.RegistrarAsync(evento, resultado, usuarioId, correo, detalle);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "No se pudo registrar la auditoría de seguridad. Evento={Evento} Excepcion={Excepcion}",
                evento,
                ex.GetType().Name);
        }
    }
}
