using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ControlFichajes.API.Services
{
    public class AuthService : IAuthService
    {
        public const string AdminRole = AppRoles.Admin;
        public const string RrhhRole = AppRoles.Rrhh;

        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IPasswordHasher<Usuario> _passwordHasher;

        public AuthService(
            AppDbContext context,
            IConfiguration configuration,
            IPasswordHasher<Usuario> passwordHasher)
        {
            _context = context;
            _configuration = configuration;
            _passwordHasher = passwordHasher;
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto loginDto)
        {
            var correo = NormalizarCorreo(loginDto.Email);
            if (string.IsNullOrWhiteSpace(correo))
                return null;

            var usuario = await _context.Usuario
                .FirstOrDefaultAsync(u => u.Correo == correo);

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

            var role = bootstrap ? AdminRole : NormalizarRol(registroDto.Rol);
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
            return rolNormalizado is AdminRole or RrhhRole ? rolNormalizado : RrhhRole;
        }

        private AuthResponseDto CrearRespuesta(Usuario usuario)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);
            var claims = CrearClaims(usuario);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(Convert.ToDouble(_configuration["Jwt:ExpireMinutes"])),
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);

            return new AuthResponseDto
            {
                Token = tokenHandler.WriteToken(token),
                Mensaje = "Autenticación exitosa"
            };
        }

        private static List<Claim> CrearClaims(Usuario usuario)
        {
            return new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new(ClaimTypes.Name, usuario.NombreUsuario),
                new(ClaimTypes.Email, usuario.Correo),
                new(ClaimTypes.Role, usuario.Rol),
                new("empresa_id", usuario.EmpresaId.ToString())
            };
        }
    }
}