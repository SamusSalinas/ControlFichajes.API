using System.Security.Cryptography;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace ControlFichajes.API.Services;

public interface IEnrolamientoMfaService
{
    Task<ResultadoEnrolamiento> IniciarAsync(
        int usuarioId,
        string? password,
        Guid? sesionId,
        string? ip,
        CancellationToken cancellationToken = default);

    Task<ResultadoEnrolamiento> ConfirmarAsync(
        int usuarioId,
        string? operacion,
        string? codigo,
        Guid? sesionId,
        string? ip,
        CancellationToken cancellationToken = default);

    Task<ResultadoEnrolamiento> IniciarVinculacionAsync(
        string? desafio,
        string modo,
        Guid? generacion,
        string? ip,
        CancellationToken cancellationToken = default);

    Task<ResultadoEnrolamiento> ConfirmarVinculacionAsync(
        string? desafio,
        string? operacion,
        string? codigo,
        string modo,
        Guid? generacion,
        string? ip,
        CancellationToken cancellationToken = default);

    Task<EstadoMfaCuenta?> ConsultarEstadoAsync(int usuarioId, CancellationToken cancellationToken = default);

    Task<ResultadoEnrolamiento> RegenerarAsync(
        int usuarioId,
        string? password,
        string? codigo,
        string? codigoRecuperacion,
        string? ip,
        CancellationToken cancellationToken = default);
}

public sealed class EstadoMfaCuenta
{
    public bool TotpHabilitado { get; init; }

    public bool MfaObligatorio { get; init; }

    public int TokenVersion { get; init; }
}

public enum EstadoEnrolamiento
{
    Deshabilitado,
    Invalido,
    Credencial,
    Restringido,
    YaConfigurado,
    Contexto,
    Bloqueado,
    NoDisponible,
    Limitado,
    Emitido,
    Error
}

public sealed class ResultadoEnrolamiento
{
    public EstadoEnrolamiento Estado { get; init; }

    public string? Operacion { get; init; }

    public string? Uri { get; init; }

    public string? ClaveManual { get; init; }

    public DateTime? ExpiraEn { get; init; }

    public string? Mensaje { get; init; }

    public IReadOnlyList<string>? Codigos { get; init; }
}

public sealed partial class EnrolamientoMfaService : IEnrolamientoMfaService
{
    public const string MensajeConfirmado = "MFA configurado. Volvé a iniciar sesión.";
    public const string MensajeRegenerado = "Códigos regenerados. Volvé a iniciar sesión.";

    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IProtectorSecretosMfa _protector;
    private readonly IPasswordHasher<Usuario> _hasher;
    private readonly ILimiteMfaPorIp _limite;
    private readonly IAuditoriaSeguridad? _auditoria;
    private readonly ILogger<EnrolamientoMfaService>? _logger;
    private readonly SemaphoreSlim _serie = new(1, 1);

    public EnrolamientoMfaService(
        AppDbContext context,
        IConfiguration configuration,
        IProtectorSecretosMfa protector,
        IPasswordHasher<Usuario> hasher,
        ILimiteMfaPorIp limite,
        IAuditoriaSeguridad? auditoria = null,
        ILogger<EnrolamientoMfaService>? logger = null)
    {
        _context = context;
        _configuration = configuration;
        _protector = protector;
        _hasher = hasher;
        _limite = limite;
        _auditoria = auditoria;
        _logger = logger;
    }

    internal Func<CancellationToken, Task>? AntesDeConfirmar { get; set; }

    internal Func<CancellationToken, Task>? DespuesDeConfirmar { get; set; }

    internal Func<DateTime>? Reloj { get; set; }

    public async Task<EstadoMfaCuenta?> ConsultarEstadoAsync(int usuarioId, CancellationToken cancellationToken = default)
    {
        return await _context.Usuario.AsNoTracking()
            .Where(u => u.Id == usuarioId && u.Activo)
            .Select(u => new EstadoMfaCuenta
            {
                TotpHabilitado = u.TotpHabilitado,
                MfaObligatorio = u.MfaObligatorio,
                TokenVersion = u.TokenVersion
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ResultadoEnrolamiento> IniciarAsync(
        int usuarioId,
        string? password,
        Guid? sesionId,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!AuthPolicy.PermitirEnrolamientoMfa(_configuration))
            return Deshabilitado();

        if (!_limite.Permitir("emision", ip, AuthPolicy.MfaEmisionLimite(_configuration), TimeSpan.FromSeconds(AuthPolicy.MfaEmisionVentanaSegundos(_configuration))))
            return Limitado();

        await _serie.WaitAsync(cancellationToken);
        try
        {
            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuarioId, cancellationToken);
            var usuario = await _context.Usuario.SingleOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);
            if (usuario == null || !usuario.Activo)
                return Invalido();

            await _context.Entry(usuario).ReloadAsync(cancellationToken);
            if (usuario.RequiereCambioPassword)
                return Restringido();

            if (usuario.TotpHabilitado)
            {
                await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, "ya_configurado");
                return new ResultadoEnrolamiento { Estado = EstadoEnrolamiento.YaConfigurado };
            }

            if (!PasswordValida(usuario, password))
            {
                await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, "credencial");
                return Credencial();
            }

            var ahora = Reloj?.Invoke() ?? DateTime.UtcNow;
            var secreto = RandomNumberGenerator.GetBytes(20);
            string pendiente;
            try
            {
                pendiente = _protector.Proteger(usuario.Id, ProtectorSecretosMfa.PropositoTotpPendiente, secreto);
            }
            catch (ProteccionSecretoNoDisponibleException)
            {
                CryptographicOperations.ZeroMemory(secreto);
                await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, "clave");
                return NoDisponible();
            }

            var operacion = TokenOpaco.Crear();
            var expira = ahora.AddMinutes(AuthPolicy.MfaEnrolamientoMinutos(_configuration));
            var anteriores = await _context.OperacionEnrolamientoMfa
                .Where(o => o.UsuarioId == usuario.Id && o.ConsumidaEn == null && o.InvalidadaEn == null)
                .ToListAsync(cancellationToken);
            foreach (var anterior in anteriores)
                anterior.InvalidadaEn = ahora;

            usuario.TotpPendienteCifrado = pendiente;
            usuario.TotpPendienteExpiraEn = expira;
            _context.OperacionEnrolamientoMfa.Add(new OperacionEnrolamientoMfa
            {
                UsuarioId = usuario.Id,
                TokenHash = TokenOpaco.Hash(operacion),
                TokenVersion = usuario.TokenVersion,
                SesionId = sesionId,
                CreadaEn = ahora,
                ExpiraEn = expira
            });
            await _context.SaveChangesAsync(cancellationToken);
            await alcance.ConfirmarAsync(cancellationToken);
            var uri = CrearUri(usuario.Correo, secreto);
            var manual = CodigosRecuperacionMfa.Agrupar(Base32Encoding.ToString(secreto).TrimEnd('='));
            CryptographicOperations.ZeroMemory(secreto);
            await AuditarAsync("mfa_enrolamiento_iniciado", "aceptado", usuario.Id, sesionId.HasValue ? "renovable" : "tradicional");
            return new ResultadoEnrolamiento
            {
                Estado = EstadoEnrolamiento.Emitido,
                Operacion = operacion,
                Uri = uri,
                ClaveManual = manual,
                ExpiraEn = expira
            };
        }
        catch (Exception ex)
        {
            Registrar("mfa_enrolamiento_iniciado", ex);
            return Error();
        }
        finally
        {
            _serie.Release();
        }
    }

    public async Task<ResultadoEnrolamiento> ConfirmarAsync(
        int usuarioId,
        string? operacion,
        string? codigo,
        Guid? sesionId,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!AuthPolicy.PermitirEnrolamientoMfa(_configuration))
            return Deshabilitado();

        if (!_limite.Permitir("verificacion", ip, AuthPolicy.MfaVerificacionLimite(_configuration), TimeSpan.FromSeconds(AuthPolicy.MfaVerificacionVentanaSegundos(_configuration))))
            return Limitado();

        if (string.IsNullOrWhiteSpace(operacion))
            return Invalido();

        await _serie.WaitAsync(cancellationToken);
        try
        {
            var hash = TokenOpaco.Hash(operacion.Trim());
            var vista = await _context.OperacionEnrolamientoMfa.AsNoTracking()
                .SingleOrDefaultAsync(o => o.TokenHash == hash, cancellationToken);
            if (vista == null || vista.UsuarioId != usuarioId)
            {
                await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuarioId, vista == null ? "operacion" : "contexto");
                return vista == null ? Invalido() : Contexto();
            }

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuarioId, cancellationToken);
            var fila = await _context.OperacionEnrolamientoMfa.SingleOrDefaultAsync(o => o.Id == vista.Id, cancellationToken);
            var usuario = await _context.Usuario.SingleOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);
            if (fila == null || usuario == null)
                return Invalido();

            await _context.Entry(fila).ReloadAsync(cancellationToken);
            await _context.Entry(usuario).ReloadAsync(cancellationToken);
            var ahora = Reloj?.Invoke() ?? DateTime.UtcNow;
            if (fila.SesionId != sesionId)
            {
                await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, "contexto");
                return Contexto();
            }

            if (fila.ConsumidaEn != null || fila.InvalidadaEn != null || usuario.TotpHabilitado)
                return Invalido();

            if (!usuario.Activo || usuario.RequiereCambioPassword || fila.TokenVersion != usuario.TokenVersion)
                return await CerrarOperacionAsync(alcance, fila, usuario, ahora, "consumido", cancellationToken);

            if (fila.ExpiraEn <= ahora)
                return await CerrarOperacionAsync(alcance, fila, usuario, ahora, "vencido", cancellationToken);

            if (fila.Intentos >= AuthPolicy.MfaIntentosDesafio(_configuration))
                return await CerrarOperacionAsync(alcance, fila, usuario, ahora, "consumido", cancellationToken);

            if (usuario.MfaBloqueadoHasta.HasValue && usuario.MfaBloqueadoHasta > ahora)
            {
                await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, "bloqueado");
                return Bloqueado();
            }

            if (string.IsNullOrWhiteSpace(usuario.TotpPendienteCifrado))
                return await NoDisponibleSinGuardarAsync(usuario, "clave");

            byte[] secreto;
            try
            {
                secreto = _protector.Abrir(usuario.Id, ProtectorSecretosMfa.PropositoTotpPendiente, usuario.TotpPendienteCifrado);
            }
            catch (ProteccionSecretoNoDisponibleException)
            {
                return await NoDisponibleSinGuardarAsync(usuario, "clave");
            }
            catch (ProteccionSecretoRechazadaException)
            {
                return await NoDisponibleSinGuardarAsync(usuario, "cifrado");
            }

            ResultadoTotp totp;
            try
            {
                totp = CodigoTotp().IsMatch(codigo?.Trim() ?? string.Empty)
                    ? ValidadorTotp.Verificar(secreto, codigo!.Trim(), ahora)
                    : new ResultadoTotp(false, 0);
                if (!totp.Coincide || (usuario.UltimoPasoTotp.HasValue && totp.Paso <= usuario.UltimoPasoTotp.Value))
                {
                    if (usuario.MfaBloqueadoHasta.HasValue && usuario.MfaBloqueadoHasta <= ahora)
                    {
                        usuario.MfaIntentosFallidos = 0;
                        usuario.MfaBloqueadoHasta = null;
                    }

                    return await FallarAsync(alcance, fila, usuario, ahora, totp.Coincide ? "paso" : "codigo", cancellationToken);
                }

                string activo;
                try
                {
                    activo = _protector.Proteger(usuario.Id, ProtectorSecretosMfa.PropositoTotpActivo, secreto);
                }
                catch (ProteccionSecretoNoDisponibleException)
                {
                    return await NoDisponibleSinGuardarAsync(usuario, "clave");
                }

                if (string.Equals(activo, usuario.TotpPendienteCifrado, StringComparison.Ordinal))
                    return await NoDisponibleSinGuardarAsync(usuario, "cifrado");

                var codigos = PrepararCodigos(usuario, ahora);
                usuario.TotpSecretoCifrado = activo;
                usuario.TotpPendienteCifrado = null;
                usuario.TotpPendienteExpiraEn = null;
                usuario.TotpHabilitado = true;
                usuario.UltimoPasoTotp = totp.Paso;
                usuario.MfaIntentosFallidos = 0;
                usuario.MfaBloqueadoHasta = null;
                fila.ConsumidaEn = ahora;
                usuario.TokenVersion++;
                await InvalidarDesafiosAsync(usuario.Id, ahora, cancellationToken);
                await RevocacionSesiones.MarcarTodasAsync(_context, usuario.Id, ahora, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                if (AntesDeConfirmar != null)
                    await AntesDeConfirmar(cancellationToken);

                await alcance.ConfirmarAsync(cancellationToken);
                if (DespuesDeConfirmar != null)
                    await DespuesDeConfirmar(cancellationToken);

                await AuditarAsync("mfa_enrolamiento_confirmado", "aceptado", usuario.Id, null);
                return new ResultadoEnrolamiento
                {
                    Estado = EstadoEnrolamiento.Emitido,
                    Mensaje = MensajeConfirmado,
                    Codigos = codigos
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secreto);
            }
        }
        catch (Exception ex)
        {
            Registrar("mfa_enrolamiento_confirmado", ex);
            return Error();
        }
        finally
        {
            _serie.Release();
        }
    }

    public async Task<ResultadoEnrolamiento> RegenerarAsync(
        int usuarioId,
        string? password,
        string? codigo,
        string? codigoRecuperacion,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!AuthPolicy.PermitirEnrolamientoMfa(_configuration))
            return Deshabilitado();

        var pideTotp = !string.IsNullOrWhiteSpace(codigo);
        var pideRecuperacion = !string.IsNullOrWhiteSpace(codigoRecuperacion);
        if (pideTotp == pideRecuperacion)
            return Invalido();

        if (!_limite.Permitir("verificacion", ip, AuthPolicy.MfaVerificacionLimite(_configuration), TimeSpan.FromSeconds(AuthPolicy.MfaVerificacionVentanaSegundos(_configuration))))
            return Limitado();

        await _serie.WaitAsync(cancellationToken);
        try
        {
            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuarioId, cancellationToken);
            var usuario = await _context.Usuario.SingleOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);
            if (usuario == null || !usuario.Activo || !usuario.TotpHabilitado)
                return Invalido();

            await _context.Entry(usuario).ReloadAsync(cancellationToken);
            var ahora = Reloj?.Invoke() ?? DateTime.UtcNow;
            if (usuario.RequiereCambioPassword)
                return Restringido();

            if (!PasswordValida(usuario, password))
            {
                await AuditarAsync("mfa_codigos_rechazados", "rechazado", usuario.Id, "credencial");
                return Credencial();
            }

            if (usuario.MfaBloqueadoHasta.HasValue && usuario.MfaBloqueadoHasta > ahora)
            {
                await AuditarAsync("mfa_codigos_rechazados", "rechazado", usuario.Id, "bloqueado");
                return Bloqueado();
            }

            if (usuario.MfaBloqueadoHasta.HasValue && usuario.MfaBloqueadoHasta <= ahora)
            {
                usuario.MfaIntentosFallidos = 0;
                usuario.MfaBloqueadoHasta = null;
            }

            var recuperacion = pideRecuperacion
                ? await BuscarCodigoAsync(usuario.Id, codigoRecuperacion, cancellationToken)
                : null;
            var totpOk = false;
            long paso = 0;
            if (pideTotp)
            {
                if (string.IsNullOrWhiteSpace(usuario.TotpSecretoCifrado))
                    return await NoDisponibleSinGuardarAsync(usuario, "clave");

                byte[] secreto;
                try
                {
                    secreto = _protector.Abrir(usuario.Id, ProtectorSecretosMfa.PropositoTotpActivo, usuario.TotpSecretoCifrado);
                }
                catch (ProteccionSecretoNoDisponibleException)
                {
                    return await NoDisponibleSinGuardarAsync(usuario, "clave");
                }
                catch (ProteccionSecretoRechazadaException)
                {
                    return await NoDisponibleSinGuardarAsync(usuario, "cifrado");
                }

                try
                {
                    var totp = CodigoTotp().IsMatch(codigo!.Trim())
                        ? ValidadorTotp.Verificar(secreto, codigo.Trim(), ahora)
                        : new ResultadoTotp(false, 0);
                    totpOk = totp.Coincide && (!usuario.UltimoPasoTotp.HasValue || totp.Paso > usuario.UltimoPasoTotp.Value);
                    paso = totp.Paso;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secreto);
                }
            }

            if ((pideTotp && !totpOk) || (pideRecuperacion && recuperacion == null))
                return await FallarUsuarioAsync(alcance, usuario, ahora, pideRecuperacion ? "recuperacion" : "codigo", cancellationToken);

            var anteriores = await _context.CodigoRecuperacionMfa
                .Where(c => c.UsuarioId == usuario.Id && c.UsadoEn == null)
                .ToListAsync(cancellationToken);
            foreach (var anterior in anteriores)
                anterior.UsadoEn = ahora;

            if (recuperacion != null && recuperacion.UsadoEn == null)
                recuperacion.UsadoEn = ahora;

            var codigos = PrepararCodigos(usuario, ahora);
            if (totpOk)
                usuario.UltimoPasoTotp = paso;

            usuario.MfaIntentosFallidos = 0;
            usuario.MfaBloqueadoHasta = null;
            usuario.TokenVersion++;
            await InvalidarDesafiosAsync(usuario.Id, ahora, cancellationToken);
            await RevocacionSesiones.MarcarTodasAsync(_context, usuario.Id, ahora, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            if (AntesDeConfirmar != null)
                await AntesDeConfirmar(cancellationToken);

            await alcance.ConfirmarAsync(cancellationToken);
            if (DespuesDeConfirmar != null)
                await DespuesDeConfirmar(cancellationToken);

            await AuditarAsync("mfa_codigos_regenerados", "aceptado", usuario.Id, pideRecuperacion ? "recuperacion" : "codigo");
            return new ResultadoEnrolamiento
            {
                Estado = EstadoEnrolamiento.Emitido,
                Mensaje = MensajeRegenerado,
                Codigos = codigos
            };
        }
        catch (Exception ex)
        {
            Registrar("mfa_codigos_regenerados", ex);
            return Error();
        }
        finally
        {
            _serie.Release();
        }
    }

    private IReadOnlyList<string> PrepararCodigos(Usuario usuario, DateTime ahora)
    {
        var codigos = CodigosRecuperacionMfa.Generar();
        var conjunto = Guid.NewGuid();
        foreach (var codigo in codigos)
        {
            var normalizado = CodigosRecuperacionMfa.Normalizar(codigo)
                ?? throw new InvalidOperationException("codigo_recuperacion");
            _context.CodigoRecuperacionMfa.Add(new CodigoRecuperacionMfa
            {
                UsuarioId = usuario.Id,
                CodigoHash = CodigosRecuperacionMfa.Hash(normalizado),
                ConjuntoId = conjunto,
                CreadoEn = ahora
            });
        }

        return codigos;
    }

    private async Task<CodigoRecuperacionMfa?> BuscarCodigoAsync(int usuarioId, string? codigo, CancellationToken cancellationToken)
    {
        var normalizado = CodigosRecuperacionMfa.Normalizar(codigo);
        if (normalizado == null)
            return null;

        var hash = CodigosRecuperacionMfa.Hash(normalizado);
        var fila = await _context.CodigoRecuperacionMfa.SingleOrDefaultAsync(c => c.CodigoHash == hash, cancellationToken);
        if (fila == null || fila.UsuarioId != usuarioId || fila.UsadoEn != null)
            return null;

        return fila;
    }

    private async Task InvalidarDesafiosAsync(int usuarioId, DateTime ahora, CancellationToken cancellationToken)
    {
        var abiertos = await _context.DesafioMfa
            .Where(d => d.UsuarioId == usuarioId && d.ConsumidoEn == null)
            .ToListAsync(cancellationToken);
        foreach (var desafio in abiertos)
            desafio.ConsumidoEn = ahora;
    }

    private async Task<ResultadoEnrolamiento> FallarAsync(
        AlcanceUsuario alcance,
        OperacionEnrolamientoMfa fila,
        Usuario usuario,
        DateTime ahora,
        string detalle,
        CancellationToken cancellationToken)
    {
        fila.Intentos++;
        usuario.MfaIntentosFallidos++;
        if (fila.Intentos >= AuthPolicy.MfaIntentosDesafio(_configuration))
        {
            fila.InvalidadaEn = ahora;
            usuario.TotpPendienteCifrado = null;
            usuario.TotpPendienteExpiraEn = null;
        }

        if (usuario.MfaIntentosFallidos >= AuthPolicy.MfaIntentosUsuario(_configuration))
            usuario.MfaBloqueadoHasta = ahora.AddMinutes(AuthPolicy.MfaBloqueoMinutos(_configuration));

        await _context.SaveChangesAsync(cancellationToken);
        await alcance.ConfirmarAsync(cancellationToken);
        await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, detalle);
        return Invalido();
    }

    private async Task<ResultadoEnrolamiento> FallarUsuarioAsync(
        AlcanceUsuario alcance,
        Usuario usuario,
        DateTime ahora,
        string detalle,
        CancellationToken cancellationToken)
    {
        usuario.MfaIntentosFallidos++;
        if (usuario.MfaIntentosFallidos >= AuthPolicy.MfaIntentosUsuario(_configuration))
            usuario.MfaBloqueadoHasta = ahora.AddMinutes(AuthPolicy.MfaBloqueoMinutos(_configuration));

        await _context.SaveChangesAsync(cancellationToken);
        await alcance.ConfirmarAsync(cancellationToken);
        await AuditarAsync("mfa_codigos_rechazados", "rechazado", usuario.Id, detalle);
        return Invalido();
    }

    private async Task<ResultadoEnrolamiento> CerrarOperacionAsync(
        AlcanceUsuario alcance,
        OperacionEnrolamientoMfa fila,
        Usuario usuario,
        DateTime ahora,
        string detalle,
        CancellationToken cancellationToken)
    {
        if (fila.InvalidadaEn == null && fila.ConsumidaEn == null)
            fila.InvalidadaEn = ahora;

        usuario.TotpPendienteCifrado = null;
        usuario.TotpPendienteExpiraEn = null;
        await _context.SaveChangesAsync(cancellationToken);
        await alcance.ConfirmarAsync(cancellationToken);
        await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, detalle);
        return Invalido();
    }

    private async Task<ResultadoEnrolamiento> NoDisponibleSinGuardarAsync(Usuario usuario, string detalle)
    {
        await AuditarAsync(detalle == "clave" || detalle == "cifrado" ? "mfa_enrolamiento_rechazado" : "mfa_codigos_rechazados", "rechazado", usuario.Id, detalle);
        return NoDisponible();
    }

    private bool PasswordValida(Usuario usuario, string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        var resultado = _hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, password);
        return resultado is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    private string CrearUri(string correo, byte[] secreto)
    {
        var emisor = Uri.EscapeDataString(AuthPolicy.MfaEmisor(_configuration));
        var cuenta = Uri.EscapeDataString(correo);
        var material = Base32Encoding.ToString(secreto).TrimEnd('=');
        return $"otpauth://totp/{emisor}:{cuenta}?secret={material}&issuer={emisor}&algorithm=SHA1&digits={ValidadorTotp.Digitos}&period={ValidadorTotp.PasoSegundos}";
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
            Registrar(evento, ex);
        }
    }

    private void Registrar(string evento, Exception ex)
    {
        _logger?.LogWarning(
            "No se pudo completar el enrolamiento MFA. Evento={Evento} Excepcion={Excepcion}",
            evento,
            ex.GetType().Name);
    }

    private static ResultadoEnrolamiento Deshabilitado() => new() { Estado = EstadoEnrolamiento.Deshabilitado };

    private static ResultadoEnrolamiento Invalido() => new() { Estado = EstadoEnrolamiento.Invalido };

    private static ResultadoEnrolamiento Credencial() => new() { Estado = EstadoEnrolamiento.Credencial };

    private static ResultadoEnrolamiento Restringido() => new() { Estado = EstadoEnrolamiento.Restringido };

    private static ResultadoEnrolamiento Contexto() => new() { Estado = EstadoEnrolamiento.Contexto };

    private static ResultadoEnrolamiento Bloqueado() => new() { Estado = EstadoEnrolamiento.Bloqueado };

    private static ResultadoEnrolamiento NoDisponible() => new() { Estado = EstadoEnrolamiento.NoDisponible };

    private static ResultadoEnrolamiento Limitado() => new() { Estado = EstadoEnrolamiento.Limitado };

    private static ResultadoEnrolamiento Error() => new() { Estado = EstadoEnrolamiento.Error };

    [System.Text.RegularExpressions.GeneratedRegex("^[0-9]{6}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex CodigoTotp();
}
