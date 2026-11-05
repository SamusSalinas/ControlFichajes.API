using System.Net;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public sealed class AuditoriaSeguridadService : IAuditoriaSeguridad
{
    public const int CorreoMaximo = 100;
    public const int UserAgentMaximo = 256;
    public const int IpMaxima = 64;

    private static readonly HashSet<string> Eventos = new(StringComparer.Ordinal)
    {
        "login_ok",
        "login_fallido",
        "login_bloqueado",
        "password_cambio",
        "password_reset_admin",
        "usuario_bootstrap",
        "bootstrap_deshabilitado",
        "usuario_alta",
        "usuario_estado",
        "usuario_rol",
        "usuario_identidad",
        "usuario_desbloqueo",
        "invitacion_creada",
        "invitacion_invalidada",
        "invitacion_envio_aceptado",
        "invitacion_envio_fallido",
        "invitacion_aceptada",
        "invitacion_aceptacion_rechazada",
        "recuperacion_solicitada",
        "recuperacion_invalidada",
        "recuperacion_envio_aceptado",
        "recuperacion_envio_fallido",
        "recuperacion_restablecida",
        "recuperacion_restablecimiento_rechazado",
        "sesion_emitida",
        "sesion_rotada",
        "sesion_reutilizada",
        "sesion_cerrada",
        "sesion_revocada",
        "mfa_desafio_emitido",
        "mfa_verificado",
        "mfa_rechazado"
    };

    private static readonly HashSet<string> Resultados = new(StringComparer.Ordinal)
    {
        "aceptado",
        "rechazado"
    };

    private static readonly HashSet<string> Detalles = new(StringComparer.Ordinal)
    {
        "clave_temporal_vencida",
        "activo",
        "inactivo",
        "identidad",
        "rol=ADMIN",
        "rol=RRHH",
        "invitacion_anterior",
        "recuperacion_anterior",
        "smtp",
        "reemplazo",
        "reutilizacion",
        "cierre",
        "token_version",
        "tradicional",
        "renovable",
        "codigo",
        "vencido",
        "consumido",
        "bloqueado",
        "cifrado",
        "clave",
        "paso",
        "contexto"
    };

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<AuditoriaSeguridadService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditoriaSeguridadService(
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<AuditoriaSeguridadService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task RegistrarAsync(
        string evento,
        string resultado,
        int? usuarioId,
        string? correo,
        string? detalle,
        CancellationToken cancellationToken = default)
    {
        if (!Eventos.Contains(evento) || !Resultados.Contains(resultado) || !DetallePermitido(detalle))
        {
            _logger.LogWarning(
                "No se pudo registrar la auditoría de seguridad. Evento={Evento} Excepcion={Excepcion}",
                Eventos.Contains(evento) ? evento : "no_controlado",
                "ValorNoControlado");
            return;
        }

        try
        {
            var http = _httpContextAccessor.HttpContext;
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            context.AuditoriaSeguridad.Add(new AuditoriaSeguridad
            {
                FechaUtc = DateTime.UtcNow,
                UsuarioId = usuarioId,
                Correo = NormalizarCorreo(correo),
                Evento = evento,
                Resultado = resultado,
                Ip = NormalizarIp(http?.Connection.RemoteIpAddress),
                UserAgent = NormalizarUserAgent(http?.Request.Headers.UserAgent.ToString()),
                Detalle = detalle
            });
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "No se pudo registrar la auditoría de seguridad. Evento={Evento} Excepcion={Excepcion}",
                evento,
                ex.GetType().Name);
        }
    }

    private static bool DetallePermitido(string? detalle)
    {
        return string.IsNullOrEmpty(detalle) || Detalles.Contains(detalle);
    }

    internal static string? NormalizarCorreo(string? correo)
    {
        return Recortar(correo, CorreoMaximo);
    }

    internal static string? NormalizarUserAgent(string? userAgent)
    {
        return Recortar(userAgent, UserAgentMaximo);
    }

    internal static string? NormalizarIp(IPAddress? ip)
    {
        return Recortar(ip?.ToString(), IpMaxima);
    }

    private static string? Recortar(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var limpio = QuitarControles(valor).Trim();
        if (limpio.Length == 0)
            return null;

        return limpio.Length <= maximo ? limpio : limpio[..maximo];
    }

    private static string QuitarControles(string valor)
    {
        var buffer = new char[valor.Length];
        var cantidad = 0;
        foreach (var caracter in valor)
        {
            if (!char.IsControl(caracter))
                buffer[cantidad++] = caracter;
        }

        return new string(buffer, 0, cantidad);
    }
}
