using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlFichajes.API.Services;

public sealed class InvitacionUsuarioService : IInvitacionUsuarioService
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<Usuario> _passwordHasher;
    private readonly ICorreoSaliente _correo;
    private readonly IAuditoriaSeguridad _auditoria;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InvitacionUsuarioService> _logger;

    public InvitacionUsuarioService(
        AppDbContext context,
        IPasswordHasher<Usuario> passwordHasher,
        ICorreoSaliente correo,
        IAuditoriaSeguridad auditoria,
        IConfiguration configuration,
        ILogger<InvitacionUsuarioService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _correo = correo;
        _auditoria = auditoria;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<InvitacionOperacion> InvitarAsync(
        int empresaId,
        string nombreUsuario,
        string correo,
        string rol,
        int creadoPor)
    {
        if (!await _context.Empresa.AnyAsync(e => e.Id == empresaId))
            return new InvitacionOperacion { Estado = EstadoInvitacion.EmpresaInexistente };

        if (await _context.Usuario.AnyAsync(u => u.Correo == correo))
            return new InvitacionOperacion { Estado = EstadoInvitacion.CorreoDuplicado };

        var usuario = new Usuario
        {
            EmpresaId = empresaId,
            NombreUsuario = nombreUsuario,
            Correo = correo,
            Rol = rol,
            Activo = false,
            InvitadoEn = DateTime.UtcNow,
            RequiereCambioPassword = false
        };
        usuario.PasswordHash = _passwordHasher.HashPassword(
            usuario,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenOpaco.Bytes)));

        var estado = await EmitirYEnviarAsync(usuario, creadoPor, nuevoUsuario: true);
        return new InvitacionOperacion
        {
            Estado = estado,
            Usuario = usuario
        };
    }

    public async Task<InvitacionOperacion> ReenviarAsync(int usuarioId, int creadoPor)
    {
        var usuario = await _context.Usuario.SingleOrDefaultAsync(u => u.Id == usuarioId);
        if (usuario == null)
            return new InvitacionOperacion { Estado = EstadoInvitacion.NoEncontrada };

        var estado = await EmitirYEnviarAsync(usuario, creadoPor, nuevoUsuario: false);
        return new InvitacionOperacion
        {
            Estado = estado,
            Usuario = usuario,
            Reenvio = true
        };
    }

    private async Task<EstadoInvitacion> EmitirYEnviarAsync(Usuario usuario, int creadoPor, bool nuevoUsuario)
    {
        var horas = AuthPolicy.InvitacionHoras(_configuration);
        var baseUrl = AuthPolicy.UrlPublica(_configuration);
        var ahora = DateTime.UtcNow;
        var token = TokenOpaco.Crear();
        var hash = TokenOpaco.Hash(token);
        var guardado = await GuardarInvitacionAsync(usuario, creadoPor, nuevoUsuario, hash, ahora, horas);
        if (guardado == null)
            return EstadoInvitacion.NoPendiente;

        if (guardado.InvalidoAnterior)
            await AuditarAsync("invitacion_invalidada", "aceptado", usuario.Id, usuario.Correo, "invitacion_anterior");

        await AuditarAsync("invitacion_creada", "aceptado", usuario.Id, usuario.Correo, null);

        if (!await ConfirmarListoParaEnviarAsync(usuario, hash))
            return EstadoInvitacion.EnvioFallido;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            await RegistrarFalloEnvioAsync(usuario, new InvalidOperationException("smtp_no_configurado"));
            return EstadoInvitacion.EnvioFallido;
        }

        try
        {
            var enlace = baseUrl + "/aceptar-invitacion?token=" + Uri.EscapeDataString(token);
            await _correo.EnviarAsync(CrearMensaje(usuario, enlace));
        }
        catch (Exception ex)
        {
            await RegistrarFalloEnvioAsync(usuario, ex);
            return EstadoInvitacion.EnvioFallido;
        }

        await AuditarAsync("invitacion_envio_aceptado", "aceptado", usuario.Id, usuario.Correo, null);
        return EstadoInvitacion.Enviada;
    }

    private async Task<GuardadoInvitacion?> GuardarInvitacionAsync(
        Usuario usuario,
        int creadoPor,
        bool nuevoUsuario,
        string hash,
        DateTime ahora,
        int horas)
    {
        var relacional = _context.Database.IsRelational();
        IDbContextTransaction? transaccion = null;
        IAsyncDisposable? puerta = null;
        try
        {
            if (relacional)
                transaccion = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            else if (!nuevoUsuario)
                puerta = await PuertaInvitacion.AdquirirAsync(usuario.Id);

            var invalidoAnterior = false;
            if (!nuevoUsuario)
            {
                if (relacional)
                    await BloquearFilaUsuarioAsync(usuario.Id);

                await _context.Entry(usuario).ReloadAsync();
                if (usuario.Activo || usuario.InvitadoEn == null || usuario.InvitacionAceptadaEn != null)
                    return null;

                var anteriores = await _context.InvitacionUsuario
                    .Where(i => i.UsuarioId == usuario.Id && i.UsadaEn == null && i.ExpiraEn > ahora)
                    .ToListAsync();
                foreach (var anterior in anteriores)
                    anterior.ExpiraEn = ahora;
                invalidoAnterior = anteriores.Count > 0;
            }

            var invitacion = new InvitacionUsuario
            {
                Usuario = usuario,
                TokenHash = hash,
                ExpiraEn = ahora.AddHours(horas),
                CreadaEn = ahora,
                CreadaPorUsuarioId = creadoPor
            };

            if (nuevoUsuario)
                _context.Usuario.Add(usuario);

            _context.InvitacionUsuario.Add(invitacion);
            await _context.SaveChangesAsync();
            if (transaccion != null)
                await transaccion.CommitAsync();

            return new GuardadoInvitacion(invalidoAnterior);
        }
        finally
        {
            if (transaccion != null)
                await transaccion.DisposeAsync();
            if (puerta != null)
                await puerta.DisposeAsync();
        }
    }

    private async Task BloquearFilaUsuarioAsync(int usuarioId)
    {
        var transaccion = _context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("transaccion_ausente");
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.Transaction = transaccion.GetDbTransaction();
        command.CommandText = "SELECT `Id` FROM `Usuario` WHERE `Id` = @id FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = usuarioId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
        }
    }

    private async Task<bool> ConfirmarListoParaEnviarAsync(Usuario usuario, string hash)
    {
        await _context.Entry(usuario).ReloadAsync();
        var hashPresente = await _context.InvitacionUsuario.AnyAsync(i =>
            i.UsuarioId == usuario.Id && i.TokenHash == hash);
        return !usuario.Activo
            && usuario.InvitadoEn != null
            && usuario.InvitacionAceptadaEn == null
            && hashPresente;
    }

    private async Task RegistrarFalloEnvioAsync(Usuario usuario, Exception ex)
    {
        _logger.LogWarning(
            "No se pudo enviar la invitación. Evento={Evento} Excepcion={Excepcion}",
            "invitacion_envio_fallido",
            ex.GetType().Name);
        await AuditarAsync("invitacion_envio_fallido", "rechazado", usuario.Id, usuario.Correo, "smtp");
    }

    private static CorreoSaliente CrearMensaje(Usuario usuario, string enlace)
    {
        var texto = string.Join(Environment.NewLine, new[]
        {
            "Te invitaron a Control de fichajes.",
            "El enlace vence según la configuración de la invitación.",
            enlace
        });
        var html = "<p>Te invitaron a Control de fichajes.</p><p><a href=\"" +
            System.Net.WebUtility.HtmlEncode(enlace) +
            "\">Aceptar invitación</a></p>";
        return new CorreoSaliente
        {
            Destinatario = usuario.Correo,
            Asunto = "Invitación a Control de fichajes",
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

    private sealed record GuardadoInvitacion(bool InvalidoAnterior);

    /// <summary>
    /// El proveedor en memoria no ofrece bloqueo de fila. Esta puerta cubre solo ese caso
    /// para que la sección crítica quede ordenada en las pruebas. MySQL usa FOR UPDATE.
    /// </summary>
    private static class PuertaInvitacion
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
