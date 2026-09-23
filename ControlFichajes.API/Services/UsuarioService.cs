using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Data;

namespace ControlFichajes.API.Services;

public class UsuarioService
{
    private readonly AppDbContext _dbContext;
    private readonly IUsuarioAdministracionAccess _adminAccess;
    private readonly IPasswordHasher<Usuario> _passwordHasher;

    // Inyección de dependencias correcta
    public UsuarioService(
        AppDbContext dbContext,
        IUsuarioAdministracionAccess adminAccess,
        IPasswordHasher<Usuario> passwordHasher)
    {
        _dbContext = dbContext;
        _adminAccess = adminAccess;
        _passwordHasher = passwordHasher;
    }

    public async Task<Usuario> RegistrarUsuario(ClaimsPrincipal usuarioActual, UsuarioRegistroDto dto)
    {
        var nuevoUsuario = new Usuario
        {
            EmpresaId = dto.EmpresaId,
            NombreUsuario = dto.NombreUsuario,
            Correo = dto.Email, 
            Rol = dto.Rol,
            Activo = true
        };

        var errorValidacion = await _adminAccess.PuedeAdministrarObjetivo(usuarioActual, nuevoUsuario);
        if (errorValidacion != null)
        {
            throw new UnauthorizedAccessException(errorValidacion);
        }

        nuevoUsuario.PasswordHash = _passwordHasher.HashPassword(nuevoUsuario, dto.Password);

        _dbContext.Usuario.Add(nuevoUsuario);
        await _dbContext.SaveChangesAsync();

        return nuevoUsuario;
    }
}