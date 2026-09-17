using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public sealed class AgenteAuthService : IAgenteAuthService
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<AgenteInstalacion> _passwordHasher;
    private readonly ITokenService _tokenService;

    public AgenteAuthService(
        AppDbContext context,
        IPasswordHasher<AgenteInstalacion> passwordHasher,
        ITokenService tokenService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<string?> AutenticarAsync(AgenteLoginDto request)
    {
        var agente = await _context.AgenteInstalacion
            .Include(a => a.Sucursal)
            .FirstOrDefaultAsync(a => a.ClientId == request.ClientId.Trim() && a.Activo);

        if (agente?.Sucursal == null)
            return null;

        var result = _passwordHasher.VerifyHashedPassword(agente, agente.SecretHash, request.ClientSecret);
        if (result == PasswordVerificationResult.Failed)
            return null;

        agente.UltimoAcceso = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return _tokenService.CreateAgentToken(agente, agente.Sucursal.EmpresaId);
    }
}
