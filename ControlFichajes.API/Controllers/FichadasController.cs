using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Mvc;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FichadasController : ControllerBase
{
    private readonly AppDbContext _context;

    public FichadasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> PostBulk([FromBody] IEnumerable<Fichada> fichadas)
    {
        var registros = fichadas.Select(f => new Fichada
        {
            EmpleadoId = f.EmpleadoId,
            FechaHora = f.FechaHora,
            TipoRegistro = f.TipoRegistro,
            Metodo = f.Metodo
        }).ToList();

        if (registros.Count == 0)
            return BadRequest(new { mensaje = "No se recibieron fichadas." });

        _context.Fichada.AddRange(registros);
        await _context.SaveChangesAsync();

        return Ok(new { recibidas = registros.Count });
    }
}
