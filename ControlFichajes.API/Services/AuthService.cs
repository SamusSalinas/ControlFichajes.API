using System.Security.Cryptography;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ControlFichajes.API.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher<Usuario> _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly IAuditoriaSeguridad? _auditoria;
        private readonly ILogger<AuthService>? _logger;
        private readonly IConfiguration? _configuration;
        private readonly IProtectorSecretosMfa? _protector;
        private readonly ILimiteMfaPorIp? _limite;
        private const int IntentosAntesDelBloqueo = 3;
        private const int MinutosDeBloqueo = 15;

        public AuthService(
            AppDbContext context,
            IPasswordHasher<Usuario> passwordHasher,
            ITokenService tokenService,
            IAuditoriaSeguridad? auditoria = null,
            ILogger<AuthService>? logger = null,
            IConfiguration? configuration = null,
            IProtectorSecretosMfa? protector = null,
            ILimiteMfaPorIp? limite = null)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _auditoria = auditoria;
            _logger = logger;
            _configuration = configuration;
            _protector = protector;
            _limite = limite;
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto loginDto)
        {
            var resultado = await IngresarAsync(loginDto, ModoSesionMfa.Tradicional, null, null);
            return resultado.Estado == EstadoIngreso.Autenticado ? resultado.Respuesta : null;
        }

        public async Task<ResultadoIngreso> IngresarAsync(
            LoginRequestDto loginDto,
            string modo,
            Guid? generacion,
            string? ip)
        {
            var correo = NormalizarCorreo(loginDto.Email);
            if (string.IsNullOrWhiteSpace(correo))
                return Invalido();

            var usuario = await _context.Usuario
                .FirstOrDefaultAsync(u => u.Correo == correo && u.Activo && u.EliminadoEn == null);

            if (usuario == null)
            {
                await _context.SaveChangesAsync();
                await AuditarAsync("login_fallido", "rechazado", null, correo, null);
                return Invalido();
            }

            var exentoDeBloqueo = AppRoles.IsSuperAdmin(usuario.Rol);
            if (!exentoDeBloqueo && usuario.BloqueadoHasta.HasValue && usuario.BloqueadoHasta > DateTime.UtcNow)
            {
                await _context.SaveChangesAsync();
                await AuditarAsync("login_bloqueado", "rechazado", usuario.Id, null, null);
                return Invalido();
            }

            var passwordResult = _passwordHasher.VerifyHashedPassword(
                usuario,
                usuario.PasswordHash,
                loginDto.Password);

            if (passwordResult == PasswordVerificationResult.Failed)
            {
                var bloqueado = false;
                if (!exentoDeBloqueo)
                {
                    usuario.IntentosFallidos++;
                    usuario.UltimoIntentoFallido = DateTime.UtcNow;
                    bloqueado = usuario.IntentosFallidos >= IntentosAntesDelBloqueo;
                    if (bloqueado)
                        usuario.BloqueadoHasta = DateTime.UtcNow.AddMinutes(MinutosDeBloqueo);
                }

                await _context.SaveChangesAsync();
                await AuditarAsync("login_fallido", "rechazado", usuario.Id, null, null);
                if (bloqueado)
                    await AuditarAsync("login_bloqueado", "rechazado", usuario.Id, null, null);

                return Invalido();
            }

            if (usuario.RequiereCambioPassword && !PasswordTemporalVigente(usuario, DateTime.UtcNow))
            {
                await _context.SaveChangesAsync();
                await AuditarAsync("login_fallido", "rechazado", usuario.Id, null, "clave_temporal_vencida");
                return Invalido();
            }

            if (usuario.MfaObligatorio && !usuario.TotpHabilitado)
            {
                if (!AuthPolicy.PermitirEnrolamientoMfa(_configuration))
                    return new ResultadoIngreso { Estado = EstadoIngreso.VinculacionNoDisponible };

                if (_protector == null)
                    return new ResultadoIngreso { Estado = EstadoIngreso.NoDisponible };

                var limiteVinculacion = _limite ?? new LimiteMfaPorIp();
                if (!limiteVinculacion.Permitir(
                        "emision",
                        ip,
                        AuthPolicy.MfaEmisionLimite(_configuration),
                        TimeSpan.FromSeconds(AuthPolicy.MfaEmisionVentanaSegundos(_configuration))))
                {
                    return new ResultadoIngreso { Estado = EstadoIngreso.Limitado };
                }

                var modoVinculacion = modo == ModoSesionMfa.Renovable
                    ? ModoSesionMfa.Renovable
                    : ModoSesionMfa.Tradicional;
                var ahoraVinculacion = DateTime.UtcNow;
                var desafioVinculacion = TokenOpaco.Crear();
                usuario.IntentosFallidos = 0;
                usuario.BloqueadoHasta = null;
                usuario.UltimoIntentoFallido = null;
                var filaVinculacion = new DesafioMfa
                {
                    UsuarioId = usuario.Id,
                    TokenHash = TokenOpaco.Hash(desafioVinculacion),
                    ExpiraEn = ahoraVinculacion.AddMinutes(AuthPolicy.MfaEnrolamientoMinutos(_configuration)),
                    Proposito = ModoSesionMfa.PropositoVinculacion,
                    TokenVersion = usuario.TokenVersion,
                    Generacion = modoVinculacion == ModoSesionMfa.Renovable ? generacion : null,
                    ModoSesion = modoVinculacion,
                    Intentos = 0
                };
                _context.DesafioMfa.Add(filaVinculacion);
                await _context.SaveChangesAsync();
                await AuditarAsync("mfa_desafio_emitido", "aceptado", usuario.Id, null, "vinculacion");
                return new ResultadoIngreso
                {
                    Estado = EstadoIngreso.VinculacionPendiente,
                    Desafio = desafioVinculacion,
                    ExpiraEn = filaVinculacion.ExpiraEn
                };
            }

            if (!usuario.TotpHabilitado)
            {
                usuario.IntentosFallidos = 0;
                usuario.BloqueadoHasta = null;
                usuario.UltimoIntentoFallido = null;
                await _context.SaveChangesAsync();
                await AuditarAsync("login_ok", "aceptado", usuario.Id, null, null);
                return new ResultadoIngreso { Estado = EstadoIngreso.Autenticado, Respuesta = CrearRespuesta(usuario) };
            }

            if (_protector == null)
                return new ResultadoIngreso { Estado = EstadoIngreso.NoDisponible };

            var limite = _limite ?? new LimiteMfaPorIp();
            if (!limite.Permitir(
                    "emision",
                    ip,
                    AuthPolicy.MfaEmisionLimite(_configuration),
                    TimeSpan.FromSeconds(AuthPolicy.MfaEmisionVentanaSegundos(_configuration))))
            {
                return new ResultadoIngreso { Estado = EstadoIngreso.Limitado };
            }

            var modoRegistrado = modo == ModoSesionMfa.Renovable
                ? ModoSesionMfa.Renovable
                : ModoSesionMfa.Tradicional;
            var generacionRegistrada = modoRegistrado == ModoSesionMfa.Renovable ? generacion : null;
            var ahora = DateTime.UtcNow;
            var desafio = TokenOpaco.Crear();
            usuario.IntentosFallidos = 0;
            usuario.BloqueadoHasta = null;
            usuario.UltimoIntentoFallido = null;
            var fila = new DesafioMfa
            {
                UsuarioId = usuario.Id,
                TokenHash = TokenOpaco.Hash(desafio),
                ExpiraEn = ahora.AddMinutes(AuthPolicy.MfaDesafioMinutos(_configuration)),
                Proposito = ModoSesionMfa.PropositoLogin,
                TokenVersion = usuario.TokenVersion,
                Generacion = generacionRegistrada,
                ModoSesion = modoRegistrado,
                Intentos = 0
            };
            _context.DesafioMfa.Add(fila);
            await _context.SaveChangesAsync();
            await AuditarAsync(
                "mfa_desafio_emitido",
                "aceptado",
                usuario.Id,
                null,
                modoRegistrado);
            return new ResultadoIngreso
            {
                Estado = EstadoIngreso.MfaRequerido,
                Desafio = desafio,
                ExpiraEn = fila.ExpiraEn
            };
        }

        private static ResultadoIngreso Invalido()
        {
            return new ResultadoIngreso { Estado = EstadoIngreso.Invalido };
        }

        public async Task<AuthResponseDto?> RegistrarUsuarioAsync(
            UsuarioRegistroDto registroDto,
            bool bootstrap)
        {
            var correo = NormalizarCorreo(registroDto.Email);
            var nombreUsuario = registroDto.NombreUsuario?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(correo) || string.IsNullOrWhiteSpace(nombreUsuario))
                return null;

            var ocupante = await _context.Usuario.SingleOrDefaultAsync(u => u.Correo == correo);
            if (ocupante != null && ocupante.EliminadoEn == null)
                return null;
            if (ocupante != null)
            {
                CorreoCuentaEliminada.Liberar(ocupante);
                await _context.SaveChangesAsync();
            }

            if (!await _context.Empresa.AnyAsync(e => e.Id == registroDto.EmpresaId))
                return null;

            if (bootstrap && await _context.Usuario.AnyAsync())
                return null;

            var role = bootstrap ? AppRoles.Admin : NormalizarRol(registroDto.Rol);
            var usuario = CrearUsuario(registroDto, correo, nombreUsuario, role);

            _context.Usuario.Add(usuario);
            await _context.SaveChangesAsync();
            await AuditarAsync(bootstrap ? "usuario_bootstrap" : "usuario_alta", "aceptado", usuario.Id, null, null);

            return CrearRespuesta(usuario);
        }

        public async Task<IEnumerable<UsuarioListItemDto>> ListarUsuariosAsync(int? empresaId, string? rol, string? nombreUsuario, string? correo, bool? activo)
        {
            var query = _context.Usuario
                .AsNoTracking()
                .Where(u => u.EliminadoEn == null);

            if (empresaId.HasValue)
                query = query.Where(u => u.EmpresaId == empresaId.Value);

            if (!string.IsNullOrWhiteSpace(rol))
                query = query.Where(u => u.Rol.ToUpper() == rol.ToUpper());

            if (!string.IsNullOrWhiteSpace(nombreUsuario))
                query = query.Where(u => u.NombreUsuario.Contains(nombreUsuario));

            if (!string.IsNullOrWhiteSpace(correo))
                query = query.Where(u => u.Correo.Contains(correo));

            if (activo.HasValue)
                query = query.Where(u => u.Activo == activo.Value);

            return await query
                .Select(u => new UsuarioListItemDto
                {
                    Id = u.Id,
                    EmpresaId = u.EmpresaId,
                    NombreUsuario = u.NombreUsuario,
                    Correo = u.Correo,
                    Rol = u.Rol,
                    Activo = u.Activo,
                    InvitacionPendiente = !u.Activo && u.InvitadoEn != null && u.InvitacionAceptadaEn == null,
                    RequiereCambioPassword = u.RequiereCambioPassword,
                    Bloqueado = u.BloqueadoHasta != null && u.BloqueadoHasta > DateTime.UtcNow,
                    BloqueadoHasta = u.BloqueadoHasta,
                    TotpHabilitado = u.TotpHabilitado,
                    MfaObligatorio = u.MfaObligatorio
                })
                .ToListAsync();
        }

        public async Task<Usuario?> GetUsuarioByIdAsync(int usuarioId)
        {
            return await _context.Usuario
                .SingleOrDefaultAsync(u => u.Id == usuarioId);
        }

        public async Task<RestablecerPasswordResponseDto?> RestablecerPasswordAsync(int usuarioId)
        {
            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null || !usuario.Activo || usuario.EliminadoEn != null)
                return null;

            var passwordTemporal = GenerarPasswordTemporal();
            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (!usuario.Activo || usuario.EliminadoEn != null)
                return null;

            usuario.PasswordHash = _passwordHasher.HashPassword(usuario, passwordTemporal);
            usuario.RequiereCambioPassword = true;
            usuario.PasswordTemporalUsada = false;
            usuario.PasswordTemporalVenceEn = DateTime.UtcNow.AddHours(24);
            usuario.IntentosFallidos = 0;
            usuario.BloqueadoHasta = null;
            usuario.UltimoIntentoFallido = null;
            usuario.TokenVersion++;
            await ConfirmarRevocandoAsync(alcance, usuario);
            await AuditarAsync("password_reset_admin", "aceptado", usuario.Id, null, null);

            return new RestablecerPasswordResponseDto
            {
                Mensaje = "Contraseña temporal creada y marcada para cambio obligatorio.",
                PasswordTemporal = passwordTemporal,
                VenceEn = usuario.PasswordTemporalVenceEn!.Value
            };
        }

        public async Task<bool> CambiarPasswordAsync(int usuarioId, CambiarPasswordRequestDto request)
        {
            if (request.NuevaPassword != request.ConfirmarPassword)
                return false;

            if (!PasswordPolitica.EsElegible(request.NuevaPassword))
                return false;

            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null || !usuario.Activo || usuario.EliminadoEn != null)
                return false;

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (!usuario.Activo || usuario.EliminadoEn != null)
                return false;

            var actualOk = _passwordHasher.VerifyHashedPassword(usuario, usuario.PasswordHash, request.PasswordActual);
            if (actualOk == PasswordVerificationResult.Failed)
                return false;

            if (usuario.RequiereCambioPassword && !PasswordTemporalVigente(usuario, DateTime.UtcNow))
                return false;

            if (string.Equals(request.NuevaPassword, request.PasswordActual, StringComparison.Ordinal))
                return false;

            usuario.PasswordHash = _passwordHasher.HashPassword(usuario, request.NuevaPassword);
            usuario.RequiereCambioPassword = false;
            usuario.PasswordTemporalUsada = true;
            usuario.PasswordTemporalVenceEn = null;
            usuario.IntentosFallidos = 0;
            usuario.BloqueadoHasta = null;
            usuario.UltimoIntentoFallido = null;
            usuario.TokenVersion++;
            await ConfirmarRevocandoAsync(alcance, usuario);
            await AuditarAsync("password_cambio", "aceptado", usuario.Id, null, null);
            return true;
        }

        public async Task<bool> DesbloquearAsync(int usuarioId)
        {
            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null || !usuario.Activo || usuario.EliminadoEn != null)
                return false;

            usuario.IntentosFallidos = 0;
            usuario.BloqueadoHasta = null;
            usuario.UltimoIntentoFallido = null;
            await _context.SaveChangesAsync();
            await AuditarAsync("usuario_desbloqueo", "aceptado", usuario.Id, null, null);
            return true;
        }

        public async Task<UsuarioListItemDto?> CambiarEstadoAsync(int usuarioId, bool activo)
        {
            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null || usuario.EliminadoEn != null)
                return null;

            if (usuario.Activo == activo)
                return ToListItem(usuario);

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (usuario.EliminadoEn != null)
                return null;
            if (usuario.Activo == activo)
                return ToListItem(usuario);

            usuario.Activo = activo;
            usuario.TokenVersion++;
            await ConfirmarRevocandoAsync(alcance, usuario);
            await AuditarAsync("usuario_estado", "aceptado", usuario.Id, null, activo ? "activo" : "inactivo");
            return ToListItem(usuario);
        }

        public async Task<UsuarioListItemDto?> CambiarRolAsync(int usuarioId, string rol)
        {
            if (!AppRoles.TryNormalizeAssignableRole(rol, out var rolNormalizado))
                return null;

            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null || usuario.EliminadoEn != null || AppRoles.IsSuperAdmin(usuario.Rol))
                return null;

            if (string.Equals(usuario.Rol, rolNormalizado, StringComparison.Ordinal))
                return ToListItem(usuario);

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (usuario.EliminadoEn != null || AppRoles.IsSuperAdmin(usuario.Rol))
                return null;

            if (string.Equals(usuario.Rol, rolNormalizado, StringComparison.Ordinal))
                return ToListItem(usuario);

            usuario.Rol = rolNormalizado;
            usuario.TokenVersion++;
            await ConfirmarRevocandoAsync(alcance, usuario);
            await AuditarAsync("usuario_rol", "aceptado", usuario.Id, null, "rol=" + rolNormalizado);
            return ToListItem(usuario);
        }

        public async Task<IdentidadUpdateResult> CambiarIdentidadAsync(int usuarioId, string? nombreUsuario, string? correo)
        {
            if (!UsuarioIdentidadRules.TryValidateNombre(nombreUsuario, out var nombreError))
                return new IdentidadUpdateResult(StatusCodes.Status400BadRequest, nombreError, null);

            if (!UsuarioIdentidadRules.TryValidateCorreo(correo, out var correoError))
                return new IdentidadUpdateResult(StatusCodes.Status400BadRequest, correoError, null);

            var correoNormalizado = NormalizarCorreo(correo ?? string.Empty);

            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null || usuario.EliminadoEn != null)
                return new IdentidadUpdateResult(StatusCodes.Status404NotFound, "Usuario no encontrado.", null);

            if (!await CorreoDisponibleAsync(usuarioId, correoNormalizado))
                return new IdentidadUpdateResult(StatusCodes.Status409Conflict, UsuarioIdentidadRules.CorreoDuplicado, null);

            var mismoNombre = string.Equals(usuario.NombreUsuario, nombreUsuario, StringComparison.Ordinal);
            var mismoCorreo = string.Equals(usuario.Correo, correoNormalizado, StringComparison.Ordinal);
            if (mismoNombre && mismoCorreo)
                return new IdentidadUpdateResult(StatusCodes.Status200OK, UsuarioIdentidadRules.Actualizado, ToListItem(usuario));

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (usuario.EliminadoEn != null)
                return new IdentidadUpdateResult(StatusCodes.Status404NotFound, "Usuario no encontrado.", null);
            if (!await CorreoDisponibleAsync(usuarioId, correoNormalizado))
                return new IdentidadUpdateResult(StatusCodes.Status409Conflict, UsuarioIdentidadRules.CorreoDuplicado, null);

            mismoNombre = string.Equals(usuario.NombreUsuario, nombreUsuario, StringComparison.Ordinal);
            mismoCorreo = string.Equals(usuario.Correo, correoNormalizado, StringComparison.Ordinal);
            if (mismoNombre && mismoCorreo)
                return new IdentidadUpdateResult(StatusCodes.Status200OK, UsuarioIdentidadRules.Actualizado, ToListItem(usuario));

            usuario.NombreUsuario = nombreUsuario!;
            usuario.Correo = correoNormalizado;
            usuario.TokenVersion++;
            await ConfirmarRevocandoAsync(alcance, usuario);
            await AuditarAsync("usuario_identidad", "aceptado", usuario.Id, null, "identidad");
            return new IdentidadUpdateResult(StatusCodes.Status200OK, UsuarioIdentidadRules.Actualizado, ToListItem(usuario));
        }

        public async Task<ExigirMfaResult> ExigirMfaAsync(int usuarioId)
        {
            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null || usuario.EliminadoEn != null)
                return new ExigirMfaResult(StatusCodes.Status404NotFound, "Usuario no encontrado.", null, null);

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (usuario.EliminadoEn != null)
                return new ExigirMfaResult(StatusCodes.Status404NotFound, "Usuario no encontrado.", null, null);

            if (!usuario.Activo)
            {
                await AuditarAsync("mfa_obligacion_rechazada", "rechazado", usuario.Id, null, "inactivo");
                return new ExigirMfaResult(StatusCodes.Status409Conflict, "Solo se puede exigir MFA en una cuenta activa.", null, null);
            }

            if (usuario.MfaObligatorio)
            {
                await AuditarAsync("mfa_obligacion_rechazada", "rechazado", usuario.Id, null, "ya_configurado");
                return new ExigirMfaResult(StatusCodes.Status409Conflict, "La cuenta ya tiene MFA o una configuración pendiente.", null, null);
            }

            var yaTieneAutenticador = usuario.TotpHabilitado;
            if (!yaTieneAutenticador && !AuthPolicy.PermitirEnrolamientoMfa(_configuration))
            {
                await AuditarAsync("mfa_obligacion_rechazada", "rechazado", usuario.Id, null, "enrolamiento");
                return new ExigirMfaResult(
                    StatusCodes.Status409Conflict,
                    "No se puede exigir MFA mientras el enrolamiento está deshabilitado.",
                    "mfa_enrolamiento_deshabilitado",
                    null);
            }

            usuario.MfaObligatorio = true;
            usuario.TokenVersion++;
            await ConfirmarRevocandoAsync(alcance, usuario);
            await AuditarAsync("mfa_obligacion_exigida", "aceptado", usuario.Id, null, yaTieneAutenticador ? "autenticador" : "obligacion");
            return new ExigirMfaResult(StatusCodes.Status200OK, "Se exigió la configuración de MFA.", null, ToListItem(usuario));
        }

        public async Task<EliminarCuentaResult> EliminarCuentaAsync(int usuarioId)
        {
            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null || usuario.EliminadoEn != null)
                return new EliminarCuentaResult(StatusCodes.Status404NotFound, "Usuario no encontrado.", null);

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (usuario.EliminadoEn != null)
                return new EliminarCuentaResult(StatusCodes.Status404NotFound, "Usuario no encontrado.", null);

            if (usuario.Activo)
            {
                await AuditarAsync("usuario_eliminacion_rechazada", "rechazado", usuario.Id, null, "activo");
                return new EliminarCuentaResult(StatusCodes.Status409Conflict, "Solo se puede eliminar una cuenta desactivada.", "cuenta_activa");
            }

            if (EsInvitacionPendiente(usuario))
            {
                await AuditarAsync("usuario_eliminacion_rechazada", "rechazado", usuario.Id, null, "invitacion_pendiente");
                return new EliminarCuentaResult(StatusCodes.Status409Conflict, "No se puede eliminar una invitación pendiente.", "invitacion_pendiente");
            }

            usuario.Activo = false;
            usuario.EliminadoEn = DateTime.UtcNow;
            usuario.TokenVersion++;
            await ConfirmarRevocandoAsync(alcance, usuario);
            await AuditarAsync("usuario_eliminado", "aceptado", usuario.Id, null, "baja");
            return new EliminarCuentaResult(StatusCodes.Status200OK, "Cuenta eliminada.", null);
        }

        public async Task<EliminarCuentaResult> CancelarInvitacionAsync(int usuarioId)
        {
            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null || usuario.EliminadoEn != null)
                return new EliminarCuentaResult(StatusCodes.Status404NotFound, "Usuario no encontrado.", null);

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (usuario.EliminadoEn != null)
                return new EliminarCuentaResult(StatusCodes.Status404NotFound, "Usuario no encontrado.", null);

            if (!EsInvitacionPendiente(usuario))
            {
                await AuditarAsync("invitacion_cancelacion_rechazada", "rechazado", usuario.Id, null, usuario.Activo ? "activa" : "no_pendiente");
                return new EliminarCuentaResult(StatusCodes.Status409Conflict, "Solo se puede cancelar una invitación pendiente.", "no_pendiente");
            }

            var ahora = DateTime.UtcNow;
            var abiertas = await _context.InvitacionUsuario
                .Where(i => i.UsuarioId == usuario.Id && i.UsadaEn == null && i.ExpiraEn > ahora)
                .ToListAsync();
            foreach (var invitacion in abiertas)
                invitacion.ExpiraEn = ahora;

            usuario.Activo = false;
            usuario.EliminadoEn = ahora;
            usuario.TokenVersion++;
            await ConfirmarRevocandoAsync(alcance, usuario);
            await AuditarAsync("invitacion_cancelada", "aceptado", usuario.Id, null, "baja");
            return new EliminarCuentaResult(StatusCodes.Status200OK, "Invitación cancelada.", null);
        }

        private static bool EsInvitacionPendiente(Usuario usuario)
        {
            return usuario.EliminadoEn == null
                && !usuario.Activo
                && usuario.InvitadoEn != null
                && usuario.InvitacionAceptadaEn == null;
        }

        private static UsuarioListItemDto ToListItem(Usuario usuario)
        {
            return new UsuarioListItemDto
            {
                Id = usuario.Id,
                EmpresaId = usuario.EmpresaId,
                NombreUsuario = usuario.NombreUsuario,
                Correo = usuario.Correo,
                Rol = usuario.Rol,
                Activo = usuario.Activo,
                InvitacionPendiente = EsInvitacionPendiente(usuario),
                RequiereCambioPassword = usuario.RequiereCambioPassword,
                Bloqueado = usuario.BloqueadoHasta != null && usuario.BloqueadoHasta > DateTime.UtcNow,
                BloqueadoHasta = usuario.BloqueadoHasta,
                TotpHabilitado = usuario.TotpHabilitado,
                MfaObligatorio = usuario.MfaObligatorio
            };
        }

        private Usuario CrearUsuario(
            UsuarioRegistroDto registroDto,
            string correo,
            string nombreUsuario,
            string rol)
        {
            var usuario = new Usuario
            {
                EmpresaId = registroDto.EmpresaId,
                NombreUsuario = nombreUsuario,
                Correo = correo,
                Rol = rol
            };

            usuario.PasswordHash = _passwordHasher.HashPassword(usuario, registroDto.Password);
            return usuario;
        }

        private async Task<bool> CorreoDisponibleAsync(int usuarioId, string correo)
        {
            var ocupante = await _context.Usuario.SingleOrDefaultAsync(item =>
                item.Id != usuarioId && item.Correo == correo);
            if (ocupante == null)
                return true;
            if (ocupante.EliminadoEn == null)
                return false;

            CorreoCuentaEliminada.Liberar(ocupante);
            await _context.SaveChangesAsync();
            return true;
        }

        private static string NormalizarCorreo(string correo)
        {
            return correo.Trim();
        }

        private static string NormalizarRol(string rol)
        {
            var rolNormalizado = rol?.Trim() ?? string.Empty;
            return rolNormalizado is AppRoles.Admin or AppRoles.Rrhh
                ? rolNormalizado
                : AppRoles.Rrhh;
        }

        private static string GenerarPasswordTemporal()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            const string digits = "23456789";
            const string specials = "!@#$%*-_";
            const string all = letters + digits + specials;
            const int length = 16;

            var password = new char[length];
            password[0] = letters[RandomNumberGenerator.GetInt32(letters.Length)];
            password[1] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
            password[2] = specials[RandomNumberGenerator.GetInt32(specials.Length)];

            for (var index = 3; index < password.Length; index++)
                password[index] = all[RandomNumberGenerator.GetInt32(all.Length)];

            for (var index = password.Length - 1; index > 0; index--)
            {
                var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
                (password[index], password[swapIndex]) = (password[swapIndex], password[index]);
            }

            return new string(password);
        }

        private static bool PasswordTemporalVigente(Usuario usuario, DateTime ahora)
        {
            return !usuario.PasswordTemporalUsada &&
                usuario.PasswordTemporalVenceEn.HasValue &&
                usuario.PasswordTemporalVenceEn.Value > ahora;
        }

        private async Task AuditarAsync(
            string evento,
            string resultado,
            int? usuarioId,
            string? correo,
            string? detalle)
        {
            if (_auditoria == null)
                return;

            try
            {
                await _auditoria.RegistrarAsync(evento, resultado, usuarioId, correo, detalle);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    "No se pudo registrar la auditoría de seguridad. Evento={Evento} Excepcion={Excepcion}",
                    evento,
                    ex.GetType().Name);
            }
        }

        private async Task ConfirmarRevocandoAsync(AlcanceUsuario alcance, Usuario usuario)
        {
            var revocadas = await RevocacionSesiones.MarcarTodasAsync(_context, usuario.Id, DateTime.UtcNow);
            await _context.SaveChangesAsync();
            await alcance.ConfirmarAsync();
            if (revocadas > 0)
                await AuditarAsync("sesion_revocada", "aceptado", usuario.Id, null, "token_version");
        }

        private AuthResponseDto CrearRespuesta(Usuario usuario)
        {
            return new AuthResponseDto
            {
                Token = _tokenService.CreateToken(usuario),
                Mensaje = "Autenticación exitosa",
                RequiereCambioPassword = usuario.RequiereCambioPassword
            };
        }
    }
}