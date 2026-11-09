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
                .FirstOrDefaultAsync(u => u.Correo == correo && u.Activo);

            if (usuario == null)
            {
                await _context.SaveChangesAsync();
                await AuditarAsync("login_fallido", "rechazado", null, correo, null);
                return Invalido();
            }

            if (usuario.BloqueadoHasta.HasValue && usuario.BloqueadoHasta > DateTime.UtcNow)
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
                usuario.IntentosFallidos++;
                usuario.UltimoIntentoFallido = DateTime.UtcNow;
                var bloqueado = usuario.IntentosFallidos >= 5;
                if (bloqueado)
                    usuario.BloqueadoHasta = DateTime.UtcNow.AddMinutes(15);

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

            if (await _context.Usuario.AnyAsync(u => u.Correo == correo))
                return null;

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
                .AsQueryable();

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
                    RequiereCambioPassword = u.RequiereCambioPassword,
                    Bloqueado = u.BloqueadoHasta != null && u.BloqueadoHasta > DateTime.UtcNow,
                    BloqueadoHasta = u.BloqueadoHasta
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
            if (usuario == null || !usuario.Activo)
                return null;

            var passwordTemporal = GenerarPasswordTemporal();
            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (!usuario.Activo)
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
            if (usuario == null || !usuario.Activo)
                return false;

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (!usuario.Activo)
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
            if (usuario == null || !usuario.Activo)
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
            if (usuario == null)
                return null;

            if (usuario.Activo == activo)
                return ToListItem(usuario);

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
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
            if (usuario == null || AppRoles.IsSuperAdmin(usuario.Rol))
                return null;

            if (string.Equals(usuario.Rol, rolNormalizado, StringComparison.Ordinal))
                return ToListItem(usuario);

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            if (AppRoles.IsSuperAdmin(usuario.Rol))
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
            if (usuario == null)
                return new IdentidadUpdateResult(StatusCodes.Status404NotFound, "Usuario no encontrado.", null);

            var correoTomado = await _context.Usuario.AnyAsync(item =>
                item.Id != usuarioId && item.Correo == correoNormalizado);
            if (correoTomado)
                return new IdentidadUpdateResult(StatusCodes.Status409Conflict, UsuarioIdentidadRules.CorreoDuplicado, null);

            var mismoNombre = string.Equals(usuario.NombreUsuario, nombreUsuario, StringComparison.Ordinal);
            var mismoCorreo = string.Equals(usuario.Correo, correoNormalizado, StringComparison.Ordinal);
            if (mismoNombre && mismoCorreo)
                return new IdentidadUpdateResult(StatusCodes.Status200OK, UsuarioIdentidadRules.Actualizado, ToListItem(usuario));

            await using var alcance = await AlcanceUsuario.AbrirAsync(_context, usuario.Id);
            await _context.Entry(usuario).ReloadAsync();
            correoTomado = await _context.Usuario.AnyAsync(item =>
                item.Id != usuarioId && item.Correo == correoNormalizado);
            if (correoTomado)
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
                RequiereCambioPassword = usuario.RequiereCambioPassword,
                Bloqueado = usuario.BloqueadoHasta != null && usuario.BloqueadoHasta > DateTime.UtcNow,
                BloqueadoHasta = usuario.BloqueadoHasta
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