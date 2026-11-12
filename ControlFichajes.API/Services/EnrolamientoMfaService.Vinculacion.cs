using System.Security.Cryptography;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace ControlFichajes.API.Services;

public sealed partial class EnrolamientoMfaService
{
    public async Task<ResultadoEnrolamiento> IniciarVinculacionAsync(
        string? desafio,
        string modo,
        Guid? generacion,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!AuthPolicy.PermitirEnrolamientoMfa(_configuration))
            return Deshabilitado();

        if (!_limite.Permitir("emision", ip, AuthPolicy.MfaEmisionLimite(_configuration), TimeSpan.FromSeconds(AuthPolicy.MfaEmisionVentanaSegundos(_configuration))))
            return Limitado();

        if (string.IsNullOrWhiteSpace(desafio))
            return Invalido();

        await _serie.WaitAsync(cancellationToken);
        try
        {
            var vista = await BuscarDesafioAsync(desafio, cancellationToken);
            if (vista == null)
                return Invalido();

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, vista.UsuarioId, cancellationToken);
            var fila = await _context.DesafioMfa.SingleOrDefaultAsync(d => d.Id == vista.Id, cancellationToken);
            var usuario = await _context.Usuario.SingleOrDefaultAsync(u => u.Id == vista.UsuarioId, cancellationToken);
            if (fila == null || usuario == null)
                return Invalido();

            await _context.Entry(fila).ReloadAsync(cancellationToken);
            await _context.Entry(usuario).ReloadAsync(cancellationToken);
            var ahora = Reloj?.Invoke() ?? DateTime.UtcNow;
            if (!ContextoVinculacion(fila, modo, generacion))
            {
                await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, "contexto");
                return Contexto();
            }

            if (fila.Proposito != ModoSesionMfa.PropositoVinculacion || fila.ConsumidoEn != null)
                return Invalido();

            if (!usuario.Activo || !usuario.MfaObligatorio || usuario.TotpHabilitado || fila.TokenVersion != usuario.TokenVersion)
                return await CerrarDesafioAsync(alcance, fila, usuario, ahora, cancellationToken);

            if (fila.ExpiraEn <= ahora)
                return await CerrarDesafioAsync(alcance, fila, usuario, ahora, cancellationToken);

            if (usuario.MfaBloqueadoHasta.HasValue && usuario.MfaBloqueadoHasta > ahora)
            {
                await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, "bloqueado");
                return Bloqueado();
            }

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
                SesionId = null,
                CreadaEn = ahora,
                ExpiraEn = expira
            });
            await _context.SaveChangesAsync(cancellationToken);
            await alcance.ConfirmarAsync(cancellationToken);
            var uri = CrearUri(usuario.Correo, secreto);
            var manual = CodigosRecuperacionMfa.Agrupar(Base32Encoding.ToString(secreto).TrimEnd('='));
            CryptographicOperations.ZeroMemory(secreto);
            await AuditarAsync("mfa_enrolamiento_iniciado", "aceptado", usuario.Id, "vinculacion");
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

    public async Task<ResultadoEnrolamiento> ConfirmarVinculacionAsync(
        string? desafio,
        string? operacion,
        string? codigo,
        string modo,
        Guid? generacion,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!AuthPolicy.PermitirEnrolamientoMfa(_configuration))
            return Deshabilitado();

        if (!_limite.Permitir("verificacion", ip, AuthPolicy.MfaVerificacionLimite(_configuration), TimeSpan.FromSeconds(AuthPolicy.MfaVerificacionVentanaSegundos(_configuration))))
            return Limitado();

        if (string.IsNullOrWhiteSpace(desafio) || string.IsNullOrWhiteSpace(operacion))
            return Invalido();

        await _serie.WaitAsync(cancellationToken);
        try
        {
            var vistaDesafio = await BuscarDesafioAsync(desafio, cancellationToken);
            var vistaOperacion = await _context.OperacionEnrolamientoMfa.AsNoTracking()
                .SingleOrDefaultAsync(o => o.TokenHash == TokenOpaco.Hash(operacion.Trim()), cancellationToken);
            if (vistaDesafio == null || vistaOperacion == null || vistaDesafio.UsuarioId != vistaOperacion.UsuarioId)
                return Invalido();

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, vistaDesafio.UsuarioId, cancellationToken);
            var fila = await _context.DesafioMfa.SingleOrDefaultAsync(d => d.Id == vistaDesafio.Id, cancellationToken);
            var pendiente = await _context.OperacionEnrolamientoMfa.SingleOrDefaultAsync(o => o.Id == vistaOperacion.Id, cancellationToken);
            var usuario = await _context.Usuario.SingleOrDefaultAsync(u => u.Id == vistaDesafio.UsuarioId, cancellationToken);
            if (fila == null || pendiente == null || usuario == null)
                return Invalido();

            await _context.Entry(fila).ReloadAsync(cancellationToken);
            await _context.Entry(pendiente).ReloadAsync(cancellationToken);
            await _context.Entry(usuario).ReloadAsync(cancellationToken);
            var ahora = Reloj?.Invoke() ?? DateTime.UtcNow;
            if (!ContextoVinculacion(fila, modo, generacion) || pendiente.SesionId != null)
            {
                await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, "contexto");
                return Contexto();
            }

            if (fila.Proposito != ModoSesionMfa.PropositoVinculacion || fila.ConsumidoEn != null || pendiente.ConsumidaEn != null || pendiente.InvalidadaEn != null || usuario.TotpHabilitado)
                return Invalido();

            if (!usuario.Activo || !usuario.MfaObligatorio || fila.TokenVersion != usuario.TokenVersion || pendiente.TokenVersion != usuario.TokenVersion)
                return await CerrarDesafioAsync(alcance, fila, usuario, ahora, cancellationToken);

            if (fila.ExpiraEn <= ahora || pendiente.ExpiraEn <= ahora)
                return await CerrarDesafioAsync(alcance, fila, usuario, ahora, cancellationToken);

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

            try
            {
                var totp = CodigoTotp().IsMatch(codigo?.Trim() ?? string.Empty)
                    ? ValidadorTotp.Verificar(secreto, codigo!.Trim(), ahora)
                    : new ResultadoTotp(false, 0);
                if (!totp.Coincide || (usuario.UltimoPasoTotp.HasValue && totp.Paso <= usuario.UltimoPasoTotp.Value))
                {
                    if (usuario.MfaBloqueadoHasta.HasValue && usuario.MfaBloqueadoHasta <= ahora)
                    {
                        usuario.MfaIntentosFallidos = 0;
                        usuario.MfaBloqueadoHasta = null;
                    }

                    return await FallarAsync(alcance, pendiente, usuario, ahora, totp.Coincide ? "paso" : "codigo", cancellationToken);
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
                usuario.MfaObligatorio = true;
                usuario.UltimoPasoTotp = totp.Paso;
                usuario.MfaIntentosFallidos = 0;
                usuario.MfaBloqueadoHasta = null;
                pendiente.ConsumidaEn = ahora;
                fila.ConsumidoEn = ahora;
                usuario.TokenVersion++;
                await InvalidarDesafiosAsync(usuario.Id, ahora, cancellationToken);
                await RevocacionSesiones.MarcarTodasAsync(_context, usuario.Id, ahora, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                if (AntesDeConfirmar != null)
                    await AntesDeConfirmar(cancellationToken);

                await alcance.ConfirmarAsync(cancellationToken);
                if (DespuesDeConfirmar != null)
                    await DespuesDeConfirmar(cancellationToken);

                await AuditarAsync("mfa_enrolamiento_confirmado", "aceptado", usuario.Id, "vinculacion");
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

    private async Task<DesafioMfa?> BuscarDesafioAsync(string desafio, CancellationToken cancellationToken)
    {
        var hash = TokenOpaco.Hash(desafio.Trim());
        return await _context.DesafioMfa.AsNoTracking().SingleOrDefaultAsync(d => d.TokenHash == hash, cancellationToken);
    }

    private static bool ContextoVinculacion(DesafioMfa fila, string modo, Guid? generacion)
    {
        var esperado = modo == ModoSesionMfa.Renovable ? ModoSesionMfa.Renovable : ModoSesionMfa.Tradicional;
        if (!string.Equals(fila.ModoSesion, esperado, StringComparison.Ordinal))
            return false;

        return esperado == ModoSesionMfa.Renovable
            ? fila.Generacion.HasValue && fila.Generacion == generacion
            : fila.Generacion == null && generacion == null;
    }

    private async Task<ResultadoEnrolamiento> CerrarDesafioAsync(
        AlcanceUsuario alcance,
        DesafioMfa fila,
        Usuario usuario,
        DateTime ahora,
        CancellationToken cancellationToken)
    {
        fila.ConsumidoEn ??= ahora;
        await _context.SaveChangesAsync(cancellationToken);
        await alcance.ConfirmarAsync(cancellationToken);
        await AuditarAsync("mfa_enrolamiento_rechazado", "rechazado", usuario.Id, "consumido");
        return Invalido();
    }
}
