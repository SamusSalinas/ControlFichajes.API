using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
        public const string SuperadminRole = AppRoles.Superadmin;
        public const string AgenteRole = "AGENTE_SUCURSAL";

        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IPasswordHasher<Usuario> _passwordHasher;
        private readonly IPasswordHasher<AgenteInstalacion> _agentePasswordHasher;

        public AuthService(
            AppDbContext context,
            IConfiguration configuration,
            IPasswordHasher<Usuario> passwordHasher,
            IPasswordHasher<AgenteInstalacion>? agentePasswordHasher = null)
        {
            _context = context;
            _configuration = configuration;
            _passwordHasher = passwordHasher;
            _agentePasswordHasher = agentePasswordHasher ?? new PasswordHasher<AgenteInstalacion>();
        }

        public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto loginDto)
        {
            var correo = NormalizarCorreo(loginDto.Email);
            if (string.IsNullOrWhiteSpace(correo))
                return null;

            var usuario = await _context.Usuario
                .FirstOrDefaultAsync(u => u.Correo == correo);

            if (usuario == null || !usuario.Activo)
                return null;

            var passwordResult = _passwordHasher.VerifyHashedPassword(
                usuario,
                usuario.PasswordHash,
                loginDto.Password);

            if (passwordResult == PasswordVerificationResult.Failed)
                return null;

            return CrearRespuesta(usuario);
        }

        public async Task<AuthResponseDto?> LoginAgenteAsync(AgenteLoginDto loginDto)
        {
            var clientId = loginDto.ClientId.Trim();
            var agente = await _context.AgenteInstalacion
                .FirstOrDefaultAsync(a => a.ClientId == clientId && a.Activo);

            if (agente == null || _agentePasswordHasher.VerifyHashedPassword(
                    agente, agente.SecretHash, loginDto.ClientSecret) == PasswordVerificationResult.Failed)
                return null;

            return CrearRespuestaAgente(agente);
        }

        public async Task<AgenteCreadoDto?> CrearAgenteAsync(AgenteCrearDto dto)
        {
            if (!await _context.Sucursal.AnyAsync(s => s.Id == dto.SucursalId && s.EmpresaId == dto.EmpresaId) ||
                await _context.AgenteInstalacion.AnyAsync(a => a.ClientId == dto.ClientId))
                return null;

            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var agente = new AgenteInstalacion
            {
                EmpresaId = dto.EmpresaId,
                SucursalId = dto.SucursalId,
                ClientId = dto.ClientId.Trim()
            };
            agente.SecretHash = _agentePasswordHasher.HashPassword(agente, secret);
            _context.AgenteInstalacion.Add(agente);
            await _context.SaveChangesAsync();

            return new AgenteCreadoDto
            {
                Id = agente.Id,
                EmpresaId = agente.EmpresaId,
                SucursalId = agente.SucursalId,
                ClientId = agente.ClientId,
                ClientSecret = secret
            };
        }

        public async Task<AgenteCreadoDto?> RotarSecretAgenteAsync(int id)
        {
            var agente = await _context.AgenteInstalacion.FindAsync(id);
            if (agente == null)
                return null;

            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            agente.SecretHash = _agentePasswordHasher.HashPassword(agente, secret);
            await _context.SaveChangesAsync();

            return new AgenteCreadoDto
            {
                Id = agente.Id,
                EmpresaId = agente.EmpresaId,
                SucursalId = agente.SucursalId,
                ClientId = agente.ClientId,
                ClientSecret = secret
            };
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

            if (bootstrap && await _context.Usuario.AnyAsync())
                return null;

            var role = bootstrap ? SuperadminRole : NormalizarRol(registroDto.Rol);
            if (role == null)
                return null;

            if (role != SuperadminRole &&
                (!registroDto.EmpresaId.HasValue ||
                 !await _context.Empresa.AnyAsync(e => e.Id == registroDto.EmpresaId.Value)))
                return null;

            var usuario = CrearUsuario(registroDto, correo, nombreUsuario, role);

            _context.Usuario.Add(usuario);
            await _context.SaveChangesAsync();

            return CrearRespuesta(usuario);
        }

        public async Task<UsuarioDto?> ObtenerUsuarioPublicoAsync(string correo)
        {
            return await _context.Usuario
                .AsNoTracking()
                .Where(u => u.Correo == correo)
                .Select(u => new UsuarioDto
                {
                    Id = u.Id,
                    EmpresaId = u.EmpresaId,
                    NombreUsuario = u.NombreUsuario,
                    Correo = u.Correo,
                    Rol = u.Rol,
                    Activo = u.Activo
                })
                .FirstOrDefaultAsync();
        }

        private Usuario CrearUsuario(
            UsuarioRegistroDto registroDto,
            string correo,
            string nombreUsuario,
            string rol)
        {
            var usuario = new Usuario
            {
                EmpresaId = rol == SuperadminRole ? null : registroDto.EmpresaId,
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

        private static string? NormalizarRol(string rol)
        {
            var rolNormalizado = rol?.Trim() ?? string.Empty;
            return rolNormalizado is AdminRole or RrhhRole or SuperadminRole
                ? rolNormalizado
                : null;
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

        private AuthResponseDto CrearRespuestaAgente(AgenteInstalacion agente)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, agente.Id.ToString()),
                new(ClaimTypes.Role, AgenteRole),
                new("token_use", "agent"),
                new("empresa_id", agente.EmpresaId.ToString()),
                new("sucursal_id", agente.SucursalId.ToString()),
                new("agente_id", agente.Id.ToString())
            };
            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(Convert.ToDouble(_configuration["Jwt:AgentExpireMinutes"] ?? "60")),
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            return new AuthResponseDto
            {
                Token = tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor)),
                Mensaje = "Autenticación de agente exitosa"
            };
        }

        private static List<Claim> CrearClaims(Usuario usuario)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new(ClaimTypes.Name, usuario.NombreUsuario),
                new(ClaimTypes.Email, usuario.Correo),
                new(ClaimTypes.Role, usuario.Rol),
                new("token_use", "web")
            };

            if (usuario.EmpresaId.HasValue)
                claims.Add(new Claim("empresa_id", usuario.EmpresaId.Value.ToString()));

            return claims;
        }
    }
}