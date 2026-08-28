using ControlFichajes.API.DTOs;
using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
public class UsuariosController : ControllerBase
{
    private readonly IAuthService _authService;

    public UsuariosController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost]
    public async Task<IActionResult> Crear(UsuarioRegistroDto request)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId)
            || request.EmpresaId != empresaId)
            return Forbid();

        if (request.Rol is not ("ADMIN" or "RRHH"))
            return BadRequest(new { mensaje = "El rol debe ser ADMIN o RRHH." });

        var response = await _authService.RegistrarUsuarioAsync(request, bootstrap: false);
        if (response == null)
            return Conflict(new { mensaje = "El correo ya está registrado o la empresa no existe." });

        return StatusCode(StatusCodes.Status201Created, response);
    }
}