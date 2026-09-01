using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SucursalesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SucursalesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Sucursal>>> GetSucursales()
        {
            if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
                return Forbid();

            return await _context.Sucursal
                .Where(s => s.EmpresaId == empresaId)
                .Include(s => s.Departamentos)
                .ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Sucursal>> GetSucursal(int id)
        {
            var sucursal = await _context.Sucursal
                .Include(s => s.Departamentos)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sucursal == null)
                return NotFound();

            if (!EmpresaAccess.PerteneceAUsuario(User, sucursal.EmpresaId))
                return Forbid();

            return Ok(sucursal);
        }

        [HttpPost]
        public async Task<ActionResult<Sucursal>> PostSucursal(Sucursal sucursal)
        {
            if (!EmpresaAccess.PerteneceAUsuario(User, sucursal.EmpresaId))
                return Forbid();

            _context.Sucursal.Add(sucursal);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSucursal), new { id = sucursal.Id }, sucursal);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutSucursal(int id, Sucursal sucursal)
        {
            if (id != sucursal.Id)
                return BadRequest();

            var sucursalDb = await _context.Sucursal.FirstOrDefaultAsync(s => s.Id == id);
            if (sucursalDb == null)
                return NotFound();

            if (!EmpresaAccess.PerteneceAUsuario(User, sucursalDb.EmpresaId))
                return Forbid();

            if (!EmpresaAccess.PerteneceAUsuario(User, sucursal.EmpresaId))
                return Forbid();

            sucursalDb.Nombre = sucursal.Nombre;
            sucursalDb.SerialLector = sucursal.SerialLector;
            sucursalDb.EmpresaId = sucursal.EmpresaId;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSucursal(int id)
        {
            var sucursal = await _context.Sucursal.FirstOrDefaultAsync(s => s.Id == id);
            if (sucursal == null)
                return NotFound();

            if (!EmpresaAccess.PerteneceAUsuario(User, sucursal.EmpresaId))
                return Forbid();

            _context.Sucursal.Remove(sucursal);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
