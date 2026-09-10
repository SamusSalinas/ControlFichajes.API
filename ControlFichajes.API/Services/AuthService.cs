using System.Security.Cryptography;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher<Usuario> _passwordHasher;
        private readonly ITokenService _tokenService;

        public AuthService(
            AppDbContext context,
            IPasswordHasher<Usuario> passwordHasher,
            ITokenService tokenService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto loginDto)
        {
            var correo = NormalizarCorreo(loginDto.Email);
            if (string.IsNullOrWhiteSpace(correo))
                return null;

            var usuario = await _context.Usuario
                .FirstOrDefaultAsync(u => u.Correo == correo && u.Activo);

            if (usuario == null)
                return null;

            if (usuario.BloqueadoHasta.HasValue && usuario.BloqueadoHasta > DateTime.UtcNow)
                return null;

            var passwordResult = _passwordHasher.VerifyHashedPassword(
                usuario,
                usuario.PasswordHash,
                loginDto.Password);

            if (passwordResult == PasswordVerificationResult.Failed)
            {
                usuario.IntentosFallidos++;
                usuario.UltimoIntentoFallido = DateTime.UtcNow;
                if (usuario.IntentosFallidos >= 5)
                {
                    usuario.BloqueadoHasta = DateTime.UtcNow.AddMinutes(15);
                }

                await _context.SaveChangesAsync();
                return null;
            }

            usuario.IntentosFallidos = 0;
            usuario.BloqueadoHasta = null;
            usuario.UltimoIntentoFallido = null;
            await _context.SaveChangesAsync();

            return CrearRespuesta(usuario);
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
            var passwordHash = _passwordHasher.HashPassword(usuario, passwordTemporal);
            usuario.PasswordHash = passwordHash;
            usuario.RequiereCambioPassword = true;
            usuario.PasswordTemporalUsada = false;
            usuario.PasswordTemporalVenceEn = DateTime.UtcNow.AddHours(24);
            usuario.IntentosFallidos = 0;
            usuario.BloqueadoHasta = null;
            usuario.UltimoIntentoFallido = null;
            usuario.TokenVersion++;

            await _context.SaveChangesAsync();

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

            if (request.NuevaPassword.Length < 8 || request.NuevaPassword.Length > 20)
                return false;

            var hasLetter = request.NuevaPassword.Any(char.IsLetter);
            var hasDigit = request.NuevaPassword.Any(char.IsDigit);
            var hasSpecial = request.NuevaPassword.Any(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch));
            var hasNoSpaces = !request.NuevaPassword.Contains(' ');

            if (!hasLetter || !hasDigit || !hasSpecial || !hasNoSpaces)
                return false;

            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null)
                return false;

            var actualOk = _passwordHasher.VerifyHashedPassword(usuario, usuario.PasswordHash, request.PasswordActual);
            if (actualOk != PasswordVerificationResult.Success)
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

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DesbloquearAsync(int usuarioId)
        {
            var usuario = await _context.Usuario.FindAsync(usuarioId);
            if (usuario == null)
                return false;

            usuario.IntentosFallidos = 0;
            usuario.BloqueadoHasta = null;
            usuario.UltimoIntentoFallido = null;
            await _context.SaveChangesAsync();
            return true;
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
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                .Replace("+", "A").Replace("/", "B")
                .Replace("=", "C").Trim();
            return $"Temp-{token[..12]}!";
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