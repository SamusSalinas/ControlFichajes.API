using ControlFichajes.API.Constants;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
            return Forbid();

        if (User.IsInRole(AppRoles.SuperAdmin))
        {
            if (request.EmpresaId != empresaId)
                return Forbid();

            if (!AppRoles.TryNormalizeAssignableRole(request.Rol, out var rolSuperAdmin))
                return BadRequest(new { mensaje = "El rol debe ser ADMIN o RRHH." });

            request.Rol = rolSuperAdmin;
        }
        else if (User.IsInRole(AppRoles.Admin))
        {
            request.EmpresaId = empresaId;
            if (!string.Equals(request.Rol?.Trim(), AppRoles.Rrhh, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { mensaje = "El rol debe ser RRHH." });

            request.Rol = AppRoles.Rrhh;
        }
        else
        {
            return Forbid();
        }

        if (!UsuarioIdentidadRules.TryValidateNombre(request.NombreUsuario, out var nombreError))
            return BadRequest(new { mensaje = nombreError });

        if (!UsuarioIdentidadRules.TryValidateCorreo(request.Email, out var correoError))
            return BadRequest(new { mensaje = correoError });

        var response = await _authService.RegistrarUsuarioAsync(request, bootstrap: false);
        if (response == null)
            return Conflict(new { mensaje = "El correo ya está registrado o la empresa no existe." });

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("{id}/restablecer-password")]
    public async Task<IActionResult> RestablecerPassword(int id)
    {
        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var target = await _authService.GetUsuarioByIdAsync(id);
        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(User, target, operadorId))
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var result = await _authService.RestablecerPasswordAsync(id);
        return result == null
            ? NotFound(new { mensaje = "Usuario no encontrado." })
            : Ok(result);
    }

    [HttpPost("{id}/desbloquear")]
    public async Task<IActionResult> Desbloquear(int id)
    {
        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var target = await _authService.GetUsuarioByIdAsync(id);
        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(User, target, operadorId))
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var ok = await _authService.DesbloquearAsync(id);
        return ok
            ? Ok(new { mensaje = "Cuenta desbloqueada." })
            : NotFound(new { mensaje = "Usuario no encontrado." });
    }

    [HttpPatch("{id}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioDto request)
    {
        if (request?.Activo is not bool activo)
            return BadRequest(new { mensaje = "El campo activo es obligatorio." });

        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var target = await _authService.GetUsuarioByIdAsync(id);
        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(User, target, operadorId))
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var usuario = await _authService.CambiarEstadoAsync(id, activo);
        if (usuario == null)
            return NotFound(new { mensaje = "Usuario no encontrado." });

        return Ok(new UsuarioActualizadoResponseDto
        {
            Mensaje = "Usuario actualizado correctamente.",
            Usuario = usuario
        });
    }

    [HttpPatch("{id}/rol")]
    public async Task<IActionResult> CambiarRol(int id, [FromBody] CambiarRolUsuarioDto request)
    {
        if (AppRoles.IsSuperAdmin(request?.Rol))
            return BadRequest(new { mensaje = "El rol debe ser ADMIN o RRHH." });

        if (!AppRoles.TryNormalizeAssignableRole(request?.Rol, out var rol))
            return BadRequest(new { mensaje = "El rol debe ser ADMIN o RRHH." });

        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var target = await _authService.GetUsuarioByIdAsync(id);
        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(User, target, operadorId))
            return NotFound(new { mensaje = "Usuario no encontrado." });

        if (!UsuarioAdministracionAccess.PuedeCambiarRol(User, target, operadorId))
            return Forbid();

        var usuario = await _authService.CambiarRolAsync(id, rol);
        if (usuario == null)
            return NotFound(new { mensaje = "Usuario no encontrado." });

        return Ok(new UsuarioActualizadoResponseDto
        {
            Mensaje = "Usuario actualizado correctamente.",
            Usuario = usuario
        });
    }

    [HttpPatch("{id}/identidad")]
    public async Task<IActionResult> CambiarIdentidad(int id, [FromBody] CambiarIdentidadUsuarioDto request)
    {
        if (request is null)
            return BadRequest(new { mensaje = "Los datos enviados no son válidos." });

        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var target = await _authService.GetUsuarioByIdAsync(id);
        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(User, target, operadorId))
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var result = await _authService.CambiarIdentidadAsync(id, request.NombreUsuario, request.Correo);
        if (result.Usuario != null)
        {
            return StatusCode(result.StatusCode, new UsuarioActualizadoResponseDto
            {
                Mensaje = result.Mensaje,
                Usuario = result.Usuario
            });
        }

        return StatusCode(result.StatusCode, new { mensaje = result.Mensaje });
    }
}