using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public interface IVerificacionMfaService
{
    Task<ResultadoVerificacionMfa> VerificarAsync(
        string? desafio,
        string? codigo,
        string modo,
        Guid? generacion,
        string? ip,
        string? userAgent,
        string? refreshAnterior,
        CancellationToken cancellationToken = default);
}

public sealed partial class VerificacionMfaService : IVerificacionMfaService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IProtectorSecretosMfa _protector;
    private readonly ITokenService _tokenService;
    private readonly SesionWebService _sesiones;
    private readonly ILimiteMfaPorIp _limite;
    private readonly IAuditoriaSeguridad? _auditoria;
    private readonly ILogger<VerificacionMfaService>? _logger;
    private readonly SemaphoreSlim _serie = new(1, 1);

    public VerificacionMfaService(
        AppDbContext context,
        IConfiguration configuration,
        IProtectorSecretosMfa protector,
        ITokenService tokenService,
        SesionWebService sesiones,
        ILimiteMfaPorIp limite,
        IAuditoriaSeguridad? auditoria = null,
        ILogger<VerificacionMfaService>? logger = null)
    {
        _context = context;
        _configuration = configuration;
        _protector = protector;
        _tokenService = tokenService;
        _sesiones = sesiones;
        _limite = limite;
        _auditoria = auditoria;
        _logger = logger;
    }

    internal Func<CancellationToken, Task>? AntesDeConfirmar { get; set; }

    internal Func<CancellationToken, Task>? DespuesDeConfirmar { get; set; }

    internal Func<DateTime>? Reloj { get; set; }

    public async Task<ResultadoVerificacionMfa> VerificarAsync(
        string? desafio,
        string? codigo,
        string modo,
        Guid? generacion,
        string? ip,
        string? userAgent,
        string? refreshAnterior,
        CancellationToken cancellationToken = default)
    {
        if (!_limite.Permitir(
                "verificacion",
                ip,
                AuthPolicy.MfaVerificacionLimite(_configuration),
                TimeSpan.FromSeconds(AuthPolicy.MfaVerificacionVentanaSegundos(_configuration))))
        {
            return new ResultadoVerificacionMfa { Estado = EstadoVerificacionMfa.Limitado };
        }

        if (string.IsNullOrWhiteSpace(desafio))
            return Invalido();

        var codigoNormalizado = codigo?.Trim() ?? string.Empty;
        var formatoValido = Codigo().IsMatch(codigoNormalizado);
        var hash = TokenOpaco.Hash(desafio.Trim());
        await _serie.WaitAsync(cancellationToken);
        try
        {
            var vista = await _context.DesafioMfa
                .AsNoTracking()
                .SingleOrDefaultAsync(d => d.TokenHash == hash, cancellationToken);
            if (vista == null)
                return Invalido();

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, vista.UsuarioId, cancellationToken);
            var fila = await _context.DesafioMfa
                .SingleOrDefaultAsync(d => d.Id == vista.Id, cancellationToken);
            var usuario = await _context.Usuario
                .SingleOrDefaultAsync(u => u.Id == vista.UsuarioId, cancellationToken);
            if (fila == null || usuario == null)
                return Invalido();

            await _context.Entry(usuario).ReloadAsync(cancellationToken);
            await _context.Entry(fila).ReloadAsync(cancellationToken);
            var ahora = Reloj?.Invoke() ?? DateTime.UtcNow;
            if (!ContextoCoincide(fila, modo, generacion))
            {
                await AuditarAsync("mfa_rechazado", "rechazado", usuario.Id, "contexto");
                return new ResultadoVerificacionMfa { Estado = EstadoVerificacionMfa.Contexto };
            }

            if (!usuario.Activo || fila.TokenVersion != usuario.TokenVersion || fila.Proposito != ModoSesionMfa.PropositoLogin)
                return await CerrarAsync(alcance, fila, usuario, ahora, "consumido", cancellationToken);

            if (fila.ConsumidoEn != null)
                return Invalido();

            if (fila.ExpiraEn <= ahora)
                return await CerrarAsync(alcance, fila, usuario, ahora, "vencido", cancellationToken);

            if (fila.Intentos >= AuthPolicy.MfaIntentosDesafio(_configuration))
                return await CerrarAsync(alcance, fila, usuario, ahora, "consumido", cancellationToken);

            if (usuario.MfaBloqueadoHasta.HasValue && usuario.MfaBloqueadoHasta > ahora)
            {
                await AuditarAsync("mfa_rechazado", "rechazado", usuario.Id, "bloqueado");
                return new ResultadoVerificacionMfa { Estado = EstadoVerificacionMfa.Bloqueado };
            }

            if (!usuario.TotpHabilitado || string.IsNullOrWhiteSpace(usuario.TotpSecretoCifrado))
                return await NoDisponibleAsync(usuario, "clave");

            byte[] secreto;
            try
            {
                secreto = _protector.Abrir(usuario.Id, ProtectorSecretosMfa.PropositoTotpActivo, usuario.TotpSecretoCifrado);
            }
            catch (ProteccionSecretoNoDisponibleException)
            {
                return await NoDisponibleAsync(usuario, "clave");
            }
            catch (ProteccionSecretoRechazadaException)
            {
                return await NoDisponibleAsync(usuario, "cifrado");
            }

            ResultadoTotp totp;
            try
            {
                totp = formatoValido
                    ? ValidadorTotp.Verificar(secreto, codigoNormalizado, ahora)
                    : new ResultadoTotp(false, 0);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secreto);
            }

            if (usuario.MfaBloqueadoHasta.HasValue && usuario.MfaBloqueadoHasta <= ahora)
            {
                usuario.MfaBloqueadoHasta = null;
                usuario.MfaIntentosFallidos = 0;
            }

            var pasoRepetido = totp.Coincide
                && usuario.UltimoPasoTotp.HasValue
                && totp.Paso <= usuario.UltimoPasoTotp.Value;
            if (!totp.Coincide || pasoRepetido)
                return await FallarCodigoAsync(alcance, fila, usuario, ahora, pasoRepetido ? "paso" : "codigo", cancellationToken);

            usuario.UltimoPasoTotp = totp.Paso;
            usuario.MfaIntentosFallidos = 0;
            usuario.MfaBloqueadoHasta = null;
            fila.ConsumidoEn = ahora;
            string? refresh = null;
            DateTime? expiraRefresh = null;
            string? familia = null;
            var renovable = fila.ModoSesion == ModoSesionMfa.Renovable;
            if (renovable)
            {
                var preparada = await _sesiones.AgregarFamiliaEnAlcanceAsync(
                    usuario,
                    refreshAnterior,
                    ip,
                    userAgent,
                    fila.Generacion,
                    cancellationToken);
                if (preparada.Estado != EstadoSesionWeb.Emitida)
                {
                    await _context.Entry(usuario).ReloadAsync(cancellationToken);
                    await _context.Entry(fila).ReloadAsync(cancellationToken);
                    return new ResultadoVerificacionMfa
                    {
                        Estado = preparada.Estado == EstadoSesionWeb.Rechazada
                            ? EstadoVerificacionMfa.Contexto
                            : EstadoVerificacionMfa.Error
                    };
                }

                refresh = preparada.Refresh;
                expiraRefresh = preparada.ExpiraEn;
                familia = preparada.FamiliaId;
            }

            await _context.SaveChangesAsync(cancellationToken);
            if (AntesDeConfirmar != null)
                await AntesDeConfirmar(cancellationToken);

            await alcance.ConfirmarAsync(cancellationToken);
            if (DespuesDeConfirmar != null)
                await DespuesDeConfirmar(cancellationToken);

            var respuesta = new AuthResponseDto
            {
                Token = _tokenService.CreateToken(usuario, familia),
                Mensaje = "Autenticación exitosa",
                RequiereCambioPassword = usuario.RequiereCambioPassword
            };
            await AuditarAsync("mfa_verificado", "aceptado", usuario.Id, null);
            await AuditarAsync("login_ok", "aceptado", usuario.Id, null);
            if (renovable)
                await AuditarAsync("sesion_emitida", "aceptado", usuario.Id, null);

            return new ResultadoVerificacionMfa
            {
                Estado = EstadoVerificacionMfa.Emitido,
                Respuesta = respuesta,
                Refresh = refresh,
                ExpiraRefresh = expiraRefresh,
                Generacion = fila.Generacion,
                Renovable = renovable
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                "No se pudo verificar MFA. Evento={Evento} Excepcion={Excepcion}",
                "mfa_verificado",
                ex.GetType().Name);
            return new ResultadoVerificacionMfa { Estado = EstadoVerificacionMfa.Error };
        }
        finally
        {
            _serie.Release();
        }
    }

    private async Task<ResultadoVerificacionMfa> FallarCodigoAsync(
        AlcanceUsuario alcance,
        DesafioMfa fila,
        Usuario usuario,
        DateTime ahora,
        string detalle,
        CancellationToken cancellationToken)
    {
        fila.Intentos++;
        usuario.MfaIntentosFallidos++;
        if (fila.Intentos >= AuthPolicy.MfaIntentosDesafio(_configuration))
            fila.ConsumidoEn = ahora;

        if (usuario.MfaIntentosFallidos >= AuthPolicy.MfaIntentosUsuario(_configuration))
            usuario.MfaBloqueadoHasta = ahora.AddMinutes(AuthPolicy.MfaBloqueoMinutos(_configuration));

        await _context.SaveChangesAsync(cancellationToken);
        await alcance.ConfirmarAsync(cancellationToken);
        await AuditarAsync("mfa_rechazado", "rechazado", usuario.Id, detalle);
        return Invalido();
    }

    private async Task<ResultadoVerificacionMfa> CerrarAsync(
        AlcanceUsuario alcance,
        DesafioMfa fila,
        Usuario usuario,
        DateTime ahora,
        string detalle,
        CancellationToken cancellationToken)
    {
        if (fila.ConsumidoEn == null)
            fila.ConsumidoEn = ahora;

        await _context.SaveChangesAsync(cancellationToken);
        await alcance.ConfirmarAsync(cancellationToken);
        await AuditarAsync("mfa_rechazado", "rechazado", usuario.Id, detalle);
        return Invalido();
    }

    private async Task<ResultadoVerificacionMfa> NoDisponibleAsync(Usuario usuario, string detalle)
    {
        await AuditarAsync("mfa_rechazado", "rechazado", usuario.Id, detalle);
        return new ResultadoVerificacionMfa { Estado = EstadoVerificacionMfa.NoDisponible };
    }

    private static bool ContextoCoincide(DesafioMfa fila, string modo, Guid? generacion)
    {
        var esperado = fila.ModoSesion == ModoSesionMfa.Renovable
            ? ModoSesionMfa.Renovable
            : ModoSesionMfa.Tradicional;
        var recibido = modo == ModoSesionMfa.Renovable
            ? ModoSesionMfa.Renovable
            : ModoSesionMfa.Tradicional;
        if (esperado != recibido)
            return false;

        return fila.Generacion == (recibido == ModoSesionMfa.Renovable ? generacion : null);
    }

    private static ResultadoVerificacionMfa Invalido()
    {
        return new ResultadoVerificacionMfa { Estado = EstadoVerificacionMfa.Invalido };
    }

    private async Task AuditarAsync(string evento, string resultado, int? usuarioId, string? detalle)
    {
        if (_auditoria == null)
            return;

        try
        {
            await _auditoria.RegistrarAsync(evento, resultado, usuarioId, null, detalle);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                "No se pudo registrar la auditoría de MFA. Evento={Evento} Excepcion={Excepcion}",
                evento,
                ex.GetType().Name);
        }
    }

    [GeneratedRegex("^[0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex Codigo();
}
