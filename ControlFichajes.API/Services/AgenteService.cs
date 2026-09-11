using System.Security.Cryptography;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services;

public sealed class AgenteService : IAgenteService
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<AgenteInstalacion> _passwordHasher;
    private readonly ITokenService _tokenService;

    public AgenteService(
        AppDbContext context,
        IPasswordHasher<AgenteInstalacion> passwordHasher,
        ITokenService tokenService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AgenteCreadoDto?> CrearAsync(AgenteCrearDto request)
    {
        var clientId = request.ClientId.Trim();
        var nombre = request.Nombre.Trim();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(nombre))
            return null;

        var sucursal = await _context.Sucursal.FirstOrDefaultAsync(s => s.Id == request.SucursalId);
        if (sucursal == null || await _context.AgenteInstalacion.AnyAsync(a => a.ClientId == clientId))
            return null;

        var agente = new AgenteInstalacion
        {
            ClientId = clientId,
            Nombre = nombre,
            SucursalId = sucursal.Id,
            Activo = true
        };
        var secret = GenerateSecret();
        agente.SecretHash = _passwordHasher.HashPassword(agente, secret);

        _context.AgenteInstalacion.Add(agente);
        await _context.SaveChangesAsync();
        return ToCreatedDto(agente, sucursal.EmpresaId, secret);
    }

    public async Task<AgenteDto?> ObtenerAsync(int id)
    {
        var agente = await Query().FirstOrDefaultAsync(a => a.Id == id);
        return agente == null ? null : ToDto(agente);
    }

    public async Task<IReadOnlyList<AgenteDto>> ListarAsync()
    {
        var agentes = await Query().ToListAsync();
        return agentes.Select(ToDto).ToList();
    }

    public async Task<AgenteCreadoDto?> RotarSecretAsync(int id)
    {
        var agente = await _context.AgenteInstalacion
            .Include(a => a.Sucursal)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (agente?.Sucursal == null)
            return null;

        var secret = GenerateSecret();
        agente.SecretHash = _passwordHasher.HashPassword(agente, secret);
        await _context.SaveChangesAsync();
        return ToCreatedDto(agente, agente.Sucursal.EmpresaId, secret);
    }

    public async Task<bool> DesactivarAsync(int id)
    {
        var agente = await _context.AgenteInstalacion.FindAsync(id);
        if (agente == null)
            return false;

        agente.Activo = false;
        await _context.SaveChangesAsync();
        return true;
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

    public async Task<AgenteHeartbeatDto?> RegistrarHeartbeatAsync(int id, AgenteHeartbeatDto request)
    {
        var agente = await _context.AgenteInstalacion.FirstOrDefaultAsync(a => a.Id == id && a.Activo);
        if (agente == null)
            return null;

        agente.UltimoAcceso = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return new AgenteHeartbeatDto
        {
            VersionApp = request.VersionApp?.Trim(),
            SerialLector = request.SerialLector?.Trim(),
            EstadoLector = request.EstadoLector?.Trim(),
            UltimaSincronizacion = request.UltimaSincronizacion
        };
    }

    public async Task<SucursalDto?> ObtenerSucursalPorAgenteAsync(int agenteId, int sucursalId)
    {
        var agente = await _context.AgenteInstalacion
            .AsNoTracking()
            .Include(a => a.Sucursal)
            .FirstOrDefaultAsync(a => a.Id == agenteId && a.SucursalId == sucursalId && a.Activo);

        if (agente?.Sucursal == null)
            return null;

        return new SucursalDto
        {
            Id = agente.Sucursal.Id,
            Nombre = agente.Sucursal.Nombre,
            EmpresaId = agente.Sucursal.EmpresaId,
            SerialLector = agente.Sucursal.SerialLector
        };
    }

    private IQueryable<AgenteInstalacion> Query()
    {
        return _context.AgenteInstalacion
            .AsNoTracking()
            .Include(a => a.Sucursal);
    }

    private static AgenteDto ToDto(AgenteInstalacion agente)
    {
        return new AgenteDto
        {
            Id = agente.Id,
            EmpresaId = agente.Sucursal?.EmpresaId ?? 0,
            SucursalId = agente.SucursalId,
            ClientId = agente.ClientId,
            Nombre = agente.Nombre,
            Activo = agente.Activo,
            UltimoAcceso = agente.UltimoAcceso
        };
    }

    private static AgenteCreadoDto ToCreatedDto(AgenteInstalacion agente, int empresaId, string secret)
    {
        return new AgenteCreadoDto
        {
            Id = agente.Id,
            EmpresaId = empresaId,
            SucursalId = agente.SucursalId,
            ClientId = agente.ClientId,
            ClientSecret = secret
        };
    }

    private static string GenerateSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}