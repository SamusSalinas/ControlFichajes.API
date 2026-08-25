using ControlFichajes.API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HuellasController : ControllerBase
{
    private readonly AppDbContext _context;

    public HuellasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("empresa/{empresaId:int}")]
    public async Task<IActionResult> GetHuellasPorEmpresa(int empresaId)
    {
        var huellas = await _context.Huella
            .AsNoTracking()
            .Where(h => h.Empleado != null && h.Empleado.EmpresaId == empresaId && h.Empleado.Activo)
            .Select(h => new
            {
                h.Id,
                h.EmpleadoId,
                h.NombreDedo,
                h.TemplateBiometrico
            })
            .ToListAsync();

        return Ok(huellas);
    }
}
