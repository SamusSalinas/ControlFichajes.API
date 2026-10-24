using ControlFichajes.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/capabilities")]
[Authorize]
public class CapabilitiesController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();
        if (!User.HasClaim("token_use", "web"))
            return Forbid();

        return Ok(new { classification = JornadaClasificador.Version });
    }
}
