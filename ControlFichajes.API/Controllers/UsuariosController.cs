using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "PuedeCrearUsuarios")]
public class UsuariosController : ControllerBase
{
    private readonly IAuthService _authService;

    public UsuariosController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsuarios([FromQuery] int? empresaId, [FromQuery] string? rol, [FromQuery] string? nombreUsuario, [FromQuery] string? correo, [FromQuery] bool? activo)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return Unauthorized();

        if (User.IsInRole(AppRoles.SuperAdmin))
        {
            var usuarios = await _authService.ListarUsuariosAsync(empresaId, rol, nombreUsuario, correo, activo);
            return Ok(usuarios);
        }

        if (User.IsInRole(AppRoles.Admin) && EmpresaAccess.TryGetEmpresaId(User, out var empresaUsuarioId))
        {
            var usuarios = await _authService.ListarUsuariosAsync(empresaUsuarioId, rol, nombreUsuario, correo, activo);
            return Ok(usuarios);
        }

        return Forbid();
    }

    [HttpPost]
    public async Task<IActionResult> Crear(UsuarioRegistroDto request)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId) || request.EmpresaId != empresaId)
            return Forbid();

        if (request.Rol is not (AppRoles.Admin or AppRoles.Rrhh))
            return BadRequest(new { mensaje = "El rol debe ser ADMIN o RRHH." });

        var response = await _authService.RegistrarUsuarioAsync(request, bootstrap: false);
        if (response == null)
            return Conflict(new { mensaje = "El correo ya está registrado o la empresa no existe." });

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("{id}/restablecer-password")]
    public async Task<IActionResult> RestablecerPassword(int id)
    {
        var target = await _authService.GetUsuarioByIdAsync(id);
        if (target == null)
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var callingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var operadorNombre = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var operadorEmpresa = EmpresaAccess.TryGetEmpresaId(User, out var empresaIdOp) ? empresaIdOp : 0;

        if (User.IsInRole(AppRoles.SuperAdmin))
        {
            if (target.Rol == AppRoles.SuperAdmin || target.Id == int.Parse(callingUserId))
                return NotFound(new { mensaje = "Usuario no encontrado." });

            if (target.Rol is not (AppRoles.Admin or AppRoles.Rrhh))
                return NotFound(new { mensaje = "Usuario no encontrado." });

            var result = await _authService.RestablecerPasswordAsync(id);
            return result == null ? NotFound(new { mensaje = "Usuario no encontrado." }) : Ok(result);
        }

        if (User.IsInRole(AppRoles.Admin))
        {
            if (target.EmpresaId != operadorEmpresa || target.Rol != AppRoles.Rrhh)
                return NotFound(new { mensaje = "Usuario no encontrado." });

            var result = await _authService.RestablecerPasswordAsync(id);
            return result == null ? NotFound(new { mensaje = "Usuario no encontrado." }) : Ok(result);
        }

        return NotFound(new { mensaje = "Usuario no encontrado." });
    }

    [HttpPost("{id}/desbloquear")]
    public async Task<IActionResult> Desbloquear(int id)
    {
        var target = await _authService.GetUsuarioByIdAsync(id);
        if (target == null)
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var callingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var operadorEmpresa = EmpresaAccess.TryGetEmpresaId(User, out var empresaIdOp) ? empresaIdOp : 0;

        if (User.IsInRole(AppRoles.SuperAdmin))
        {
            if (target.Rol == AppRoles.SuperAdmin || target.Id == int.Parse(callingUserId))
                return NotFound(new { mensaje = "Usuario no encontrado." });

            if (target.Rol is not (AppRoles.Admin or AppRoles.Rrhh))
                return NotFound(new { mensaje = "Usuario no encontrado." });

            var ok = await _authService.DesbloquearAsync(id);
            return ok ? Ok(new { mensaje = "Cuenta desbloqueada." }) : NotFound(new { mensaje = "Usuario no encontrado." });
        }

        if (User.IsInRole(AppRoles.Admin))
        {
            if (target.EmpresaId != operadorEmpresa || target.Rol != AppRoles.Rrhh)
                return NotFound(new { mensaje = "Usuario no encontrado." });

            var ok = await _authService.DesbloquearAsync(id);
            return ok ? Ok(new { mensaje = "Cuenta desbloqueada." }) : NotFound(new { mensaje = "Usuario no encontrado." });
        }

        return NotFound(new { mensaje = "Usuario no encontrado." });
    }

    [HttpPost("{id}/cambiar-password")]
    public async Task<IActionResult> CambiarPassword(int id, [FromBody] CambiarPasswordRequestDto request)
    {
        var ok = await _authService.CambiarPasswordAsync(id, request);
        return ok ? Ok(new { mensaje = "Contraseña actualizada." }) : BadRequest(new { mensaje = "La contraseña nueva no cumple el contrato requerido." });
    }
}