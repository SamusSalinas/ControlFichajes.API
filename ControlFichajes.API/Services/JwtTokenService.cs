using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Models;
using Microsoft.IdentityModel.Tokens;

namespace ControlFichajes.API.Services;

public sealed class JwtTokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateToken(Usuario usuario)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(GetRequiredConfiguration("Jwt:Key"));
        var claims = CreateClaims(usuario);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(GetExpirationMinutes()),
            Issuer = GetRequiredConfiguration("Jwt:Issuer"),
            Audience = GetRequiredConfiguration("Jwt:Audience"),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }

    public string CreateAgentToken(AgenteInstalacion agente, int empresaId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, agente.Id.ToString()),
            new(ClaimTypes.Name, agente.ClientId),
            new(ClaimTypes.Role, "AGENTE_SUCURSAL"),
            new("token_use", "agent"),
            new("agente_id", agente.Id.ToString()),
            new("empresa_id", empresaId.ToString()),
            new("sucursal_id", agente.SucursalId.ToString())
        };

        return CreateSignedToken(claims);
    }

    private double GetExpirationMinutes()
    {
        return Convert.ToDouble(GetRequiredConfiguration("Jwt:ExpireMinutes"));
    }

    private string CreateSignedToken(IEnumerable<Claim> claims)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(GetExpirationMinutes()),
            Issuer = GetRequiredConfiguration("Jwt:Issuer"),
            Audience = GetRequiredConfiguration("Jwt:Audience"),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetRequiredConfiguration("Jwt:Key"))),
                SecurityAlgorithms.HmacSha256Signature)
        };

        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }

    private string GetRequiredConfiguration(string key)
    {
        return _configuration[key]
            ?? throw new InvalidOperationException($"Missing configuration: {key}.");
    }

    private static List<Claim> CreateClaims(Usuario usuario)
    {
        var role = AppRoles.IsSuperAdmin(usuario.Rol)
            ? AppRoles.SuperAdmin
            : usuario.Rol;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.NombreUsuario),
            new(ClaimTypes.Email, usuario.Correo),
            new(ClaimTypes.Role, role),
            new("token_use", "web"),
            new("token_version", usuario.TokenVersion.ToString())
        };

        if (usuario.RequiereCambioPassword)
            claims.Add(new Claim("requiere_cambio_password", "true"));

        if (!AppRoles.IsSuperAdmin(role))
            claims.Add(new Claim("empresa_id", usuario.EmpresaId.ToString()));

        return claims;
    }
}