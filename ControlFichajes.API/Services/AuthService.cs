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

            var passwordResult = _passwordHasher.VerifyHashedPassword(
                usuario,
                usuario.PasswordHash,
                loginDto.Password);

            if (passwordResult == PasswordVerificationResult.Failed)
                return null;

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

        private AuthResponseDto CrearRespuesta(Usuario usuario)
        {
            return new AuthResponseDto
            {
                Token = _tokenService.CreateToken(usuario),
                Mensaje = "Autenticación exitosa"
            };
        }
    }
}