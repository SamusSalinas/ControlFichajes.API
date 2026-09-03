using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FichadasController : ControllerBase
{
    private readonly AppDbContext _context;

    public FichadasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize(Policy = "LeeEmpresa")]
    public async Task<IActionResult> GetFichadas(
        [FromQuery] int? empleadoId,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] string? tipo,
        [FromQuery] string? metodo,
        [FromQuery] int limite = 100)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaIdUsuario))
            return Forbid();
        if (limite is < 1 or > 500)
            return BadRequest(new { mensaje = "El límite debe estar entre 1 y 500." });

        var query = _context.Fichada
            .AsNoTracking()
            .Where(f => f.Empleado != null && f.Empleado.Activo && f.Empleado.EmpresaId == empresaIdUsuario)
            .AsQueryable();

        if (empleadoId.HasValue)
            query = query.Where(f => f.EmpleadoId == empleadoId.Value);
        if (desde.HasValue)
            query = query.Where(f => f.FechaHora >= desde.Value);
        if (hasta.HasValue)
            query = query.Where(f => f.FechaHora < hasta.Value);
        if (!string.IsNullOrWhiteSpace(tipo))
            query = query.Where(f => f.TipoRegistro == tipo);
        if (!string.IsNullOrWhiteSpace(metodo))
            query = query.Where(f => f.Metodo == metodo);

        var fichadas = await query
            .OrderByDescending(f => f.FechaHora)
            .Take(limite)
            .Select(f => new
            {
                f.Id,
                f.EmpleadoId,
                Nombre = f.Empleado!.Nombre,
                Apellido = f.Empleado.Apellido,
                Legajo = f.Empleado.Legajo,
                f.FechaHora,
                Tipo = f.TipoRegistro,
                f.Metodo
            })
            .ToListAsync();

        return Ok(fichadas);
    }

    [HttpPost("bulk")]
    [Authorize(Policy = "SoloAgente")]
    public async Task<IActionResult> PostBulk([FromBody] IEnumerable<Fichada> fichadas)
    {
        var entrada = fichadas?.ToList() ?? [];
        if (entrada.Count == 0)
            return BadRequest(new { mensaje = "No se recibieron fichadas." });
        if (entrada.Count > 500)
            return BadRequest(new { mensaje = "El lote no puede superar 500 fichadas." });

        var empleadoIds = entrada.Select(f => f.EmpleadoId).Distinct().ToList();
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaIdUsuario))
            return Forbid();
        var sucursalId = int.TryParse(User.FindFirst("sucursal_id")?.Value, out var claimSucursalId)
            ? claimSucursalId
            : (int?)null;

        var empleadosActivos = await _context.Empleado
            .Where(e => empleadoIds.Contains(e.Id) && e.Activo && e.EmpresaId == empresaIdUsuario &&
                (!sucursalId.HasValue || e.SucursalId == sucursalId))
            .Select(e => e.Id)
            .ToListAsync();

        if (empleadoIds.Except(empleadosActivos).Any())
            return BadRequest(new { mensaje = "El lote contiene empleados inexistentes o inactivos." });

        var tiposValidos = new[] { "Entrada", "Salida" };
        var metodosValidos = new[] { "Biometrico", "Biométrico", "Manual" };
        if (entrada.Any(f => !tiposValidos.Contains(f.TipoRegistro) || !metodosValidos.Contains(f.Metodo)))
            return BadRequest(new { mensaje = "TipoRegistro o Metodo no válido." });

        var registros = entrada.Select(f => new Fichada
        {
            EmpleadoId = f.EmpleadoId,
            FechaHora = f.FechaHora,
            TipoRegistro = f.TipoRegistro,
            Metodo = f.Metodo
        }).ToList();

        _context.Fichada.AddRange(registros);
        await _context.SaveChangesAsync();

        return Ok(new { recibidas = registros.Count });
    }
}
