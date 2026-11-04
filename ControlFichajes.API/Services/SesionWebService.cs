using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public sealed class SesionWebService : ISesionWebService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ITokenService _tokenService;
    private readonly IAuditoriaSeguridad? _auditoria;
    private readonly ILogger<SesionWebService>? _logger;

    public SesionWebService(
        AppDbContext context,
        IConfiguration configuration,
        ITokenService tokenService,
        IAuditoriaSeguridad? auditoria = null,
        ILogger<SesionWebService>? logger = null)
    {
        _context = context;
        _configuration = configuration;
        _tokenService = tokenService;
        _auditoria = auditoria;
        _logger = logger;
    }

    public async Task<ResultadoSesionWeb> EmitirAsync(
        int usuarioId,
        string? refreshAnterior,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var hashAnterior = HashSiHay(refreshAnterior);
        int? otroUsuario = null;
        Guid? otraFamilia = null;
        if (hashAnterior != null)
        {
            var vista = await _context.RefreshToken
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.TokenHash == hashAnterior, cancellationToken);
            if (vista != null && vista.UsuarioId != usuarioId)
            {
                otroUsuario = vista.UsuarioId;
                otraFamilia = vista.FamiliaId;
            }
        }

        try
        {
            if (otroUsuario.HasValue && otraFamilia.HasValue)
            {
                var revocada = await RevocarFamiliaConfirmadaAsync(otroUsuario.Value, otraFamilia.Value, cancellationToken);
                if (!revocada)
                    return Error();
            }

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuarioId, cancellationToken);
            var usuario = await _context.Usuario.SingleOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);
            if (usuario == null)
                return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Rechazada };

            await _context.Entry(usuario).ReloadAsync(cancellationToken);
            if (!usuario.Activo)
                return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Rechazada };

            var reemplazo = false;
            if (hashAnterior != null && !otroUsuario.HasValue)
            {
                var previa = await _context.RefreshToken
                    .SingleOrDefaultAsync(r => r.TokenHash == hashAnterior, cancellationToken);
                if (previa != null && previa.UsuarioId == usuario.Id)
                {
                    await RevocacionSesiones.MarcarFamiliaAsync(_context, usuario.Id, previa.FamiliaId, DateTime.UtcNow, cancellationToken);
                    reemplazo = true;
                }
            }

            var ahora = DateTime.UtcNow;
            var creada = ahora;
            var fila = NuevaFila(usuario, creada, creada, ip, userAgent, out var refresh);
            _context.RefreshToken.Add(fila);
            await _context.SaveChangesAsync(cancellationToken);
            var respuesta = Respuesta(usuario, fila.FamiliaId.ToString("D"));
            await alcance.ConfirmarAsync(cancellationToken);
            await AuditarAsync("sesion_emitida", "aceptado", usuario.Id, null, reemplazo ? "reemplazo" : null);
            return new ResultadoSesionWeb
            {
                Estado = EstadoSesionWeb.Emitida,
                Refresh = refresh,
                ExpiraEn = fila.ExpiraEn,
                FamiliaId = fila.FamiliaId.ToString("D"),
                Respuesta = respuesta
            };
        }
        catch (Exception ex)
        {
            RegistrarFallo("sesion_emitida", ex);
            return Error();
        }
    }

    public async Task<ResultadoSesionWeb> RotarAsync(
        string? refresh,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var hash = HashSiHay(refresh);
        if (hash == null)
            return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Rechazada };

        var vista = await _context.RefreshToken
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
        if (vista == null)
            return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Rechazada };

        try
        {
            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, vista.UsuarioId, cancellationToken);
            var actual = await _context.RefreshToken.SingleOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
            var usuario = await _context.Usuario.SingleOrDefaultAsync(u => u.Id == vista.UsuarioId, cancellationToken);
            if (actual == null || usuario == null)
                return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Rechazada };

            await _context.Entry(actual).ReloadAsync(cancellationToken);
            await _context.Entry(usuario).ReloadAsync(cancellationToken);
            var ahora = DateTime.UtcNow;

            if (actual.RevocadaEn != null || actual.ReemplazadaPorId != null)
            {
                await RevocacionSesiones.MarcarFamiliaAsync(_context, usuario.Id, actual.FamiliaId, ahora, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                await alcance.ConfirmarAsync(cancellationToken);
                await AuditarAsync("sesion_reutilizada", "rechazado", usuario.Id, null, "reutilizacion");
                return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Reutilizada, FamiliaId = actual.FamiliaId.ToString("D") };
            }

            if (!usuario.Activo || usuario.TokenVersion != actual.TokenVersion)
                return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Rechazada };

            var inicio = await InicioFamiliaAsync(actual.FamiliaId, cancellationToken);
            var absoluto = inicio.AddDays(AuthPolicy.RefreshExpireDays(_configuration));
            if (actual.ExpiraEn <= ahora || absoluto <= ahora)
                return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Rechazada };

            var sucesor = NuevaFila(usuario, inicio, ahora, ip, userAgent, out var nuevoRefresh);
            sucesor.FamiliaId = actual.FamiliaId;
            sucesor.TokenVersion = usuario.TokenVersion;
            _context.RefreshToken.Add(sucesor);
            await _context.SaveChangesAsync(cancellationToken);
            actual.RevocadaEn = ahora;
            actual.ReemplazadaPorId = sucesor.Id;
            await _context.SaveChangesAsync(cancellationToken);
            var respuesta = Respuesta(usuario, actual.FamiliaId.ToString("D"));
            await alcance.ConfirmarAsync(cancellationToken);
            await AuditarAsync("sesion_rotada", "aceptado", usuario.Id, null, null);
            return new ResultadoSesionWeb
            {
                Estado = EstadoSesionWeb.Rotada,
                Refresh = nuevoRefresh,
                ExpiraEn = sucesor.ExpiraEn,
                FamiliaId = actual.FamiliaId.ToString("D"),
                Respuesta = respuesta
            };
        }
        catch (Exception ex)
        {
            RegistrarFallo("sesion_rotada", ex);
            return Error();
        }
    }

    public async Task<ResultadoSesionWeb> CerrarAsync(string? refresh, CancellationToken cancellationToken = default)
    {
        var hash = HashSiHay(refresh);
        if (hash == null)
            return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Cerrada };

        var vista = await _context.RefreshToken
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
        if (vista == null)
            return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Cerrada };

        try
        {
            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, vista.UsuarioId, cancellationToken);
            var actual = await _context.RefreshToken.SingleOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
            if (actual == null)
            {
                await alcance.ConfirmarAsync(cancellationToken);
                return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Cerrada };
            }

            await _context.Entry(actual).ReloadAsync(cancellationToken);
            var revocadas = await RevocacionSesiones.MarcarFamiliaAsync(
                _context,
                actual.UsuarioId,
                actual.FamiliaId,
                DateTime.UtcNow,
                cancellationToken);
            if (revocadas > 0)
                await _context.SaveChangesAsync(cancellationToken);

            await alcance.ConfirmarAsync(cancellationToken);
            if (revocadas > 0)
                await AuditarAsync("sesion_cerrada", "aceptado", actual.UsuarioId, null, "cierre");

            return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Cerrada, FamiliaId = actual.FamiliaId.ToString("D") };
        }
        catch (Exception ex)
        {
            RegistrarFallo("sesion_cerrada", ex);
            return Error();
        }
    }

    private async Task<bool> RevocarFamiliaConfirmadaAsync(
        int usuarioId,
        Guid familiaId,
        CancellationToken cancellationToken)
    {
        await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuarioId, cancellationToken);
        await RevocacionSesiones.MarcarFamiliaAsync(_context, usuarioId, familiaId, DateTime.UtcNow, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await alcance.ConfirmarAsync(cancellationToken);
        return true;
    }

    private RefreshToken NuevaFila(
        Usuario usuario,
        DateTime inicioFamilia,
        DateTime ahora,
        string? ip,
        string? userAgent,
        out string refresh)
    {
        refresh = TokenOpaco.Crear();
        var absoluto = inicioFamilia.AddDays(AuthPolicy.RefreshExpireDays(_configuration));
        var inactividad = ahora.AddHours(AuthPolicy.RefreshInactividadHoras(_configuration));
        var expira = inactividad < absoluto ? inactividad : absoluto;
        return new RefreshToken
        {
            UsuarioId = usuario.Id,
            TokenHash = TokenOpaco.Hash(refresh),
            FamiliaId = Guid.NewGuid(),
            ExpiraEn = expira,
            CreadaEn = ahora,
            Ip = Cortar(ip, 64),
            UserAgent = Cortar(userAgent, 256),
            TokenVersion = usuario.TokenVersion
        };
    }

    private AuthResponseDto Respuesta(Usuario usuario, string familiaId)
    {
        return new AuthResponseDto
        {
            Token = _tokenService.CreateToken(usuario, familiaId),
            Mensaje = "Autenticación exitosa",
            RequiereCambioPassword = usuario.RequiereCambioPassword
        };
    }

    private async Task<DateTime> InicioFamiliaAsync(Guid familiaId, CancellationToken cancellationToken)
    {
        return await _context.RefreshToken
            .Where(r => r.FamiliaId == familiaId)
            .MinAsync(r => r.CreadaEn, cancellationToken);
    }

    private static string? HashSiHay(string? refresh)
    {
        if (string.IsNullOrWhiteSpace(refresh))
            return null;

        return TokenOpaco.Hash(refresh.Trim());
    }

    private static string? Cortar(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var texto = valor.Trim();
        return texto.Length <= maximo ? texto : texto[..maximo];
    }

    private static ResultadoSesionWeb Error()
    {
        return new ResultadoSesionWeb { Estado = EstadoSesionWeb.Error };
    }

    private void RegistrarFallo(string evento, Exception ex)
    {
        _logger?.LogWarning(
            "No se pudo completar la sesión web. Evento={Evento} Excepcion={Excepcion}",
            evento,
            ex.GetType().Name);
    }

    private async Task AuditarAsync(string evento, string resultado, int? usuarioId, string? correo, string? detalle)
    {
        if (_auditoria == null)
            return;

        try
        {
            await _auditoria.RegistrarAsync(evento, resultado, usuarioId, correo, detalle);
        }
        catch (Exception ex)
        {
            RegistrarFallo(evento, ex);
        }
    }
}
