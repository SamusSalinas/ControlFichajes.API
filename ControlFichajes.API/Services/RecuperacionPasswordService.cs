using System.Collections.Concurrent;
using System.Data;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlFichajes.API.Services;

public sealed class RecuperacionPasswordService : IRecuperacionPasswordService
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<Usuario> _passwordHasher;
    private readonly ICorreoSaliente _correo;
    private readonly IAuditoriaSeguridad _auditoria;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RecuperacionPasswordService> _logger;

    public RecuperacionPasswordService(
        AppDbContext context,
        IPasswordHasher<Usuario> passwordHasher,
        ICorreoSaliente correo,
        IAuditoriaSeguridad auditoria,
        IConfiguration configuration,
        ILogger<RecuperacionPasswordService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _correo = correo;
        _auditoria = auditoria;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SolicitarAsync(string correoNormalizado, CancellationToken cancellationToken = default)
    {
        try
        {
            var usuario = await _context.Usuario
                .FirstOrDefaultAsync(u => u.Correo.ToLower() == correoNormalizado, cancellationToken);
            if (!CuentaElegible(usuario))
            {
                await AuditarAsync("recuperacion_solicitada", "rechazado", usuario?.Id, usuario?.Correo ?? correoNormalizado, null);
                return;
            }

            var token = TokenOpaco.Crear();
            var hash = TokenOpaco.Hash(token);
            var guardado = await GuardarAsync(usuario!, hash, cancellationToken);
            if (guardado == null)
            {
                await AuditarAsync("recuperacion_solicitada", "rechazado", usuario!.Id, usuario.Correo, null);
                return;
            }

            if (guardado.InvalidoAnterior)
                await AuditarAsync("recuperacion_invalidada", "aceptado", usuario!.Id, usuario.Correo, "recuperacion_anterior");

            await AuditarAsync("recuperacion_solicitada", "aceptado", usuario!.Id, usuario.Correo, null);

            if (!await ConfirmarListaParaEnviarAsync(usuario, hash, cancellationToken))
                return;

            var baseUrl = AuthPolicy.UrlPublica(_configuration);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                await RegistrarFalloEnvioAsync(usuario, new InvalidOperationException("smtp_no_configurado"));
                return;
            }

            try
            {
                var enlace = baseUrl + "/restablecer-contrasena?token=" + Uri.EscapeDataString(token);
                await _correo.EnviarAsync(CrearMensaje(usuario.Correo, enlace), cancellationToken);
            }
            catch (Exception ex)
            {
                await RegistrarFalloEnvioAsync(usuario, ex);
                return;
            }

            await AuditarAsync("recuperacion_envio_aceptado", "aceptado", usuario.Id, usuario.Correo, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "No se pudo solicitar la recuperación. Evento={Evento} Excepcion={Excepcion}",
                "recuperacion_solicitada",
                ex.GetType().Name);
        }
    }

    public async Task<RestablecimientoPassword> RestablecerAsync(
        string token,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || !PasswordPolitica.EsElegible(password))
            return new RestablecimientoPassword { Estado = EstadoRestablecimiento.Rechazada };

        var hash = TokenOpaco.Hash(token.Trim());
        var relacional = _context.Database.IsRelational();
        IDbContextTransaction? transaccion = null;
        IAsyncDisposable? puerta = null;
        try
        {
            var recuperacion = await _context.RecuperacionPassword
                .SingleOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
            if (recuperacion == null)
            {
                await AuditarAsync("recuperacion_restablecimiento_rechazado", "rechazado", null, null, null);
                return new RestablecimientoPassword { Estado = EstadoRestablecimiento.Rechazada };
            }

            if (relacional)
                transaccion = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            else
                puerta = await PuertaRecuperacion.AdquirirAsync(recuperacion.UsuarioId);

            if (relacional)
                await BloquearFilaUsuarioAsync(recuperacion.UsuarioId, cancellationToken);

            var ahora = DateTime.UtcNow;
            await _context.Entry(recuperacion).ReloadAsync(cancellationToken);
            var usuario = await _context.Usuario.SingleOrDefaultAsync(u => u.Id == recuperacion.UsuarioId, cancellationToken);
            if (usuario != null)
                await _context.Entry(usuario).ReloadAsync(cancellationToken);

            if (!RecuperacionVigente(recuperacion, ahora) || !CuentaElegible(usuario))
            {
                await AuditarAsync(
                    "recuperacion_restablecimiento_rechazado",
                    "rechazado",
                    usuario?.Id,
                    usuario?.Correo,
                    null);
                return new RestablecimientoPassword { Estado = EstadoRestablecimiento.Rechazada };
            }

            usuario!.PasswordHash = _passwordHasher.HashPassword(usuario, password);
            usuario.RequiereCambioPassword = false;
            usuario.TokenVersion++;
            recuperacion.UsadaEn = ahora;

            var otras = await _context.RecuperacionPassword
                .Where(r => r.UsuarioId == usuario.Id && r.Id != recuperacion.Id && r.UsadaEn == null && r.ExpiraEn > ahora)
                .ToListAsync(cancellationToken);
            foreach (var otra in otras)
                otra.ExpiraEn = ahora;

            await _context.SaveChangesAsync(cancellationToken);
            if (transaccion != null)
                await transaccion.CommitAsync(cancellationToken);

            await AuditarAsync("recuperacion_restablecida", "aceptado", usuario.Id, usuario.Correo, null);
            return new RestablecimientoPassword { Estado = EstadoRestablecimiento.Restablecida };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "No se pudo restablecer la contraseña. Evento={Evento} Excepcion={Excepcion}",
                "recuperacion_restablecida",
                ex.GetType().Name);
            return new RestablecimientoPassword { Estado = EstadoRestablecimiento.Error };
        }
        finally
        {
            if (transaccion != null)
                await transaccion.DisposeAsync();
            if (puerta != null)
                await puerta.DisposeAsync();
        }
    }

    private async Task<Guardado?> GuardarAsync(Usuario usuario, string hash, CancellationToken cancellationToken)
    {
        var relacional = _context.Database.IsRelational();
        IDbContextTransaction? transaccion = null;
        IAsyncDisposable? puerta = null;
        try
        {
            if (relacional)
                transaccion = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            else
                puerta = await PuertaRecuperacion.AdquirirAsync(usuario.Id);

            if (relacional)
                await BloquearFilaUsuarioAsync(usuario.Id, cancellationToken);

            await _context.Entry(usuario).ReloadAsync(cancellationToken);
            var ahora = DateTime.UtcNow;
            if (!CuentaElegible(usuario))
                return null;

            var anteriores = await _context.RecuperacionPassword
                .Where(r => r.UsuarioId == usuario.Id && r.UsadaEn == null && r.ExpiraEn > ahora)
                .ToListAsync(cancellationToken);
            foreach (var anterior in anteriores)
                anterior.ExpiraEn = ahora;

            _context.RecuperacionPassword.Add(new RecuperacionPassword
            {
                UsuarioId = usuario.Id,
                TokenHash = hash,
                ExpiraEn = ahora.AddMinutes(AuthPolicy.RecuperacionMinutos(_configuration)),
                CreadaEn = ahora
            });
            await _context.SaveChangesAsync(cancellationToken);
            if (transaccion != null)
                await transaccion.CommitAsync(cancellationToken);

            return new Guardado(anteriores.Count > 0);
        }
        finally
        {
            if (transaccion != null)
                await transaccion.DisposeAsync();
            if (puerta != null)
                await puerta.DisposeAsync();
        }
    }

    private async Task BloquearFilaUsuarioAsync(int usuarioId, CancellationToken cancellationToken)
    {
        var transaccion = _context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("transaccion_ausente");
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaccion.GetDbTransaction();
        command.CommandText = "SELECT `Id` FROM `Usuario` WHERE `Id` = @id FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = usuarioId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
        }
    }

    private async Task<bool> ConfirmarListaParaEnviarAsync(Usuario usuario, string hash, CancellationToken cancellationToken)
    {
        await _context.Entry(usuario).ReloadAsync(cancellationToken);
        var ahora = DateTime.UtcNow;
        var vigente = await _context.RecuperacionPassword.AnyAsync(r =>
            r.UsuarioId == usuario.Id &&
            r.TokenHash == hash &&
            r.UsadaEn == null &&
            r.ExpiraEn > ahora,
            cancellationToken);
        return CuentaElegible(usuario) && vigente;
    }

    private static bool CuentaElegible(Usuario? usuario)
    {
        return usuario is { Activo: true, CorreoVerificadoEn: not null };
    }

    private static bool RecuperacionVigente(RecuperacionPassword? recuperacion, DateTime ahora)
    {
        return recuperacion != null && recuperacion.UsadaEn == null && recuperacion.ExpiraEn > ahora;
    }

    private async Task RegistrarFalloEnvioAsync(Usuario usuario, Exception ex)
    {
        _logger.LogWarning(
            "No se pudo enviar la recuperación. Evento={Evento} Excepcion={Excepcion}",
            "recuperacion_envio_fallido",
            ex.GetType().Name);
        await AuditarAsync("recuperacion_envio_fallido", "rechazado", usuario.Id, usuario.Correo, "smtp");
    }

    private static CorreoSaliente CrearMensaje(string destinatario, string enlace)
    {
        var texto = string.Join(Environment.NewLine, new[]
        {
            "Recibimos una solicitud para restablecer tu contraseña.",
            "El enlace vence según la configuración de la recuperación.",
            "Si no lo pediste, podés ignorar este mensaje.",
            enlace
        });
        var html = "<p>Recibimos una solicitud para restablecer tu contraseña.</p><p><a href=\"" +
            System.Net.WebUtility.HtmlEncode(enlace) +
            "\">Restablecer contraseña</a></p>";
        return new CorreoSaliente
        {
            Destinatario = destinatario,
            Asunto = "Recuperación de contraseña",
            Texto = texto,
            Html = html
        };
    }

    private async Task AuditarAsync(string evento, string resultado, int? usuarioId, string? correo, string? detalle)
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

    private sealed record Guardado(bool InvalidoAnterior);

    /// <summary>
    /// El proveedor en memoria no ofrece bloqueo de fila. Esta puerta ordena la sección
    /// crítica en las pruebas. MySQL usa FOR UPDATE y cubre varias instancias.
    /// </summary>
    private static class PuertaRecuperacion
    {
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> Puertas = new();

        public static async Task<IAsyncDisposable> AdquirirAsync(int usuarioId)
        {
            var puerta = Puertas.GetOrAdd(usuarioId, _ => new SemaphoreSlim(1, 1));
            await puerta.WaitAsync();
            return new Liberacion(puerta);
        }

        private sealed class Liberacion(SemaphoreSlim puerta) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                puerta.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
