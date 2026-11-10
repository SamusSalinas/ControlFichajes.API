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
    private readonly IInvitacionUsuarioService? _invitaciones;
    private readonly IConfiguration? _configuration;

    public UsuariosController(
        IAuthService authService,
        IInvitacionUsuarioService? invitaciones = null,
        IConfiguration? configuration = null)
    {
        _authService = authService;
        _invitaciones = invitaciones;
        _configuration = configuration;
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
        if (!AuthPolicy.PermitirAltaConPassword(_configuration))
            return NotFound();

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

    [HttpPost("invitar")]
    public async Task<IActionResult> Invitar(InvitarUsuarioDto request)
    {
        if (_invitaciones == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { mensaje = "No se pudo enviar la invitación." });

        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
            return Forbid();

        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        if (!TryPrepararAlta(request.EmpresaId, request.Rol, empresaId, out var empresaDestino, out var rol, out var error))
            return error!;

        if (!UsuarioIdentidadRules.TryValidateNombre(request.NombreUsuario, out var nombreError))
            return BadRequest(new { mensaje = nombreError });

        var correo = NormalizarCorreo(request.Email);
        if (!UsuarioIdentidadRules.TryValidateCorreo(correo, out var correoError))
            return BadRequest(new { mensaje = correoError });

        var operacion = await _invitaciones.InvitarAsync(
            empresaDestino,
            request.NombreUsuario,
            correo,
            rol,
            operadorId,
            request.ExigirMfa);
        return ResponderInvitacion(operacion);
    }

    [HttpPost("{id}/reenviar-invitacion")]
    public async Task<IActionResult> ReenviarInvitacion(int id)
    {
        if (_invitaciones == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { mensaje = "No se pudo enviar la invitación." });

        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var target = await _authService.GetUsuarioByIdAsync(id);
        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(User, target, operadorId))
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var operacion = await _invitaciones.ReenviarAsync(id, operadorId);
        return ResponderInvitacion(operacion);
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

    [HttpPost("{id}/exigir-mfa")]
    public async Task<IActionResult> ExigirMfa(int id)
    {
        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var target = await _authService.GetUsuarioByIdAsync(id);
        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(User, target, operadorId))
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var resultado = await _authService.ExigirMfaAsync(id);
        if (resultado.StatusCode == StatusCodes.Status200OK && resultado.Usuario != null)
        {
            return Ok(new UsuarioActualizadoResponseDto
            {
                Mensaje = resultado.Mensaje,
                Usuario = resultado.Usuario
            });
        }

        if (resultado.StatusCode == StatusCodes.Status409Conflict)
        {
            return resultado.Codigo == null
                ? Conflict(new { mensaje = resultado.Mensaje })
                : Conflict(new { codigo = resultado.Codigo, mensaje = resultado.Mensaje });
        }

        return StatusCode(resultado.StatusCode, new { mensaje = resultado.Mensaje });
    }

    [HttpPost("{id}/eliminar")]
    public async Task<IActionResult> Eliminar(int id)
    {
        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var target = await _authService.GetUsuarioByIdAsync(id);
        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(User, target, operadorId))
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var resultado = await _authService.EliminarCuentaAsync(id);
        if (resultado.StatusCode == StatusCodes.Status200OK)
            return Ok(new { mensaje = resultado.Mensaje });

        if (resultado.StatusCode == StatusCodes.Status409Conflict)
        {
            return resultado.Codigo == null
                ? Conflict(new { mensaje = resultado.Mensaje })
                : Conflict(new { codigo = resultado.Codigo, mensaje = resultado.Mensaje });
        }

        if (resultado.StatusCode == StatusCodes.Status404NotFound)
            return NotFound(new { mensaje = resultado.Mensaje });

        return StatusCode(resultado.StatusCode, new { mensaje = resultado.Mensaje });
    }

    [HttpPost("{id}/cancelar-invitacion")]
    public async Task<IActionResult> CancelarInvitacion(int id)
    {
        if (!UsuarioAdministracionAccess.TryGetOperadorId(User, out var operadorId))
            return Unauthorized(new { mensaje = "Sesión inválida." });

        var target = await _authService.GetUsuarioByIdAsync(id);
        if (!UsuarioAdministracionAccess.PuedeAdministrarObjetivo(User, target, operadorId))
            return NotFound(new { mensaje = "Usuario no encontrado." });

        var resultado = await _authService.CancelarInvitacionAsync(id);
        if (resultado.StatusCode == StatusCodes.Status200OK)
            return Ok(new { mensaje = resultado.Mensaje });

        if (resultado.StatusCode == StatusCodes.Status409Conflict)
        {
            return resultado.Codigo == null
                ? Conflict(new { mensaje = resultado.Mensaje })
                : Conflict(new { codigo = resultado.Codigo, mensaje = resultado.Mensaje });
        }

        if (resultado.StatusCode == StatusCodes.Status404NotFound)
            return NotFound(new { mensaje = resultado.Mensaje });

        return StatusCode(resultado.StatusCode, new { mensaje = resultado.Mensaje });
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

    private bool TryPrepararAlta(
        int empresaSolicitada,
        string? rolSolicitado,
        int empresaId,
        out int empresaDestino,
        out string rol,
        out IActionResult? error)
    {
        rol = string.Empty;
        empresaDestino = empresaId;
        error = null;

        if (User.IsInRole(AppRoles.SuperAdmin))
        {
            if (empresaSolicitada != empresaId)
            {
                error = Forbid();
                return false;
            }

            if (!AppRoles.TryNormalizeAssignableRole(rolSolicitado, out rol))
            {
                error = BadRequest(new { mensaje = "El rol debe ser ADMIN o RRHH." });
                return false;
            }

            return true;
        }

        if (User.IsInRole(AppRoles.Admin))
        {
            if (!string.Equals(rolSolicitado?.Trim(), AppRoles.Rrhh, StringComparison.OrdinalIgnoreCase))
            {
                error = BadRequest(new { mensaje = "El rol debe ser RRHH." });
                return false;
            }

            rol = AppRoles.Rrhh;
            return true;
        }

        error = Forbid();
        return false;
    }

    private IActionResult ResponderInvitacion(InvitacionOperacion operacion)
    {
        if (operacion.Estado is EstadoInvitacion.CorreoDuplicado or EstadoInvitacion.EmpresaInexistente)
            return Conflict(new { mensaje = "El correo ya está registrado o la empresa no existe." });

        if (operacion.Estado == EstadoInvitacion.NoEncontrada)
            return NotFound(new { mensaje = "Usuario no encontrado." });

        if (operacion.Estado == EstadoInvitacion.NoPendiente)
            return Conflict(new { mensaje = "No se puede reenviar la invitación." });

        if (operacion.Estado == EstadoInvitacion.EnrolamientoDeshabilitado)
            return Conflict(new { codigo = "mfa_enrolamiento_deshabilitado" });

        if (operacion.Estado == EstadoInvitacion.EnvioFallido || operacion.Usuario == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { mensaje = "No se pudo enviar la invitación." });

        return StatusCode(operacion.Reenvio ? StatusCodes.Status200OK : StatusCodes.Status201Created, new InvitacionCreadaDto
        {
            Id = operacion.Usuario.Id,
            NombreUsuario = operacion.Usuario.NombreUsuario,
            Correo = operacion.Usuario.Correo,
            Rol = operacion.Usuario.Rol,
            Activo = operacion.Usuario.Activo,
            Mensaje = operacion.Reenvio ? "Invitación reenviada." : "Invitación enviada."
        });
    }

    private static string NormalizarCorreo(string? correo)
    {
        var recortado = (correo ?? string.Empty).Trim();
        var buffer = new char[recortado.Length];
        var cantidad = 0;
        foreach (var caracter in recortado)
        {
            if (!char.IsControl(caracter))
                buffer[cantidad++] = caracter;
        }

        return new string(buffer, 0, cantidad).Trim();
    }
}