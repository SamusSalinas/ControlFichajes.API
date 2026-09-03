using ControlFichajes.API.Constants;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ControlFichajes.API.Data;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "UsuariosEmpresa")]
public class UsuariosController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly AppDbContext _context;

    public UsuariosController(IAuthService authService, AppDbContext context)
    {
        _authService = authService;
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
            return Forbid();

        var usuarios = await _context.Usuario
            .AsNoTracking()
            .Where(u => u.EmpresaId == empresaId)
            .Select(u => new UsuarioDto
            {
                Id = u.Id,
                EmpresaId = u.EmpresaId,
                NombreUsuario = u.NombreUsuario,
                Correo = u.Correo,
                Rol = u.Rol,
                Activo = u.Activo
            })
            .ToListAsync();

        return Ok(usuarios);
    }

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, UsuarioPatchDto request)
    {
        var usuario = await _context.Usuario.FirstOrDefaultAsync(u => u.Id == id);
        if (usuario == null)
            return NotFound();

        if (!usuario.EmpresaId.HasValue || !EmpresaAccess.PerteneceAUsuario(User, usuario.EmpresaId.Value))
            return Forbid();

        if (usuario.Rol == AppRoles.Superadmin && !User.IsInRole(AppRoles.Superadmin))
            return Forbid();
        if (request.Rol is not null && request.Rol is not (AppRoles.Superadmin or AppRoles.Admin or AppRoles.Rrhh))
            return BadRequest(new { mensaje = "El rol no es válido." });
        if (request.Rol == AppRoles.Superadmin && !User.IsInRole(AppRoles.Superadmin))
            return Forbid();

        if (request.NombreUsuario is not null)
            usuario.NombreUsuario = request.NombreUsuario.Trim();
        if (request.Rol is not null)
            usuario.Rol = request.Rol;
        if (request.Activo.HasValue)
            usuario.Activo = request.Activo.Value;

        await _context.SaveChangesAsync();
        return Ok(new UsuarioDto
        {
            Id = usuario.Id,
            EmpresaId = usuario.EmpresaId,
            NombreUsuario = usuario.NombreUsuario,
            Correo = usuario.Correo,
            Rol = usuario.Rol,
            Activo = usuario.Activo
        });
    }

    [HttpPost]
    public async Task<IActionResult> Crear(UsuarioRegistroDto request)
    {
        if (request.Rol == AppRoles.Superadmin)
        {
            if (!User.IsInRole(AppRoles.Superadmin) || request.EmpresaId.HasValue)
                return Forbid();
        }
        else if (!request.EmpresaId.HasValue ||
                 !EmpresaAccess.PerteneceAUsuario(User, request.EmpresaId.Value))
        {
            return Forbid();
        }

        if (request.Rol is not (AppRoles.Superadmin or AppRoles.Admin or AppRoles.Rrhh))
            return BadRequest(new { mensaje = "El rol debe ser SUPERADMIN, ADMIN o RRHH." });

        var response = await _authService.RegistrarUsuarioAsync(request, bootstrap: false);
        if (response == null)
            return Conflict(new { mensaje = "El correo ya está registrado o la empresa no existe." });

        var usuario = await _authService.ObtenerUsuarioPublicoAsync(request.Email.Trim());
        return StatusCode(StatusCodes.Status201Created, usuario);
    }
}