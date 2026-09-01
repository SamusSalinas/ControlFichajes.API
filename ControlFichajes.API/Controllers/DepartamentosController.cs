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
    public class DepartamentosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DepartamentosController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Departamento>>> GetDepartamentos()
        {
            if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
                return Forbid();

            return await _context.Departamento
                .Include(d => d.Sucursal)
                .Where(d => d.Sucursal != null && d.Sucursal.EmpresaId == empresaId)
                .ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Departamento>> GetDepartamento(int id)
        {
            var departamento = await _context.Departamento
                .Include(d => d.Sucursal)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (departamento == null)
                return NotFound();

            if (departamento.Sucursal == null || !EmpresaAccess.PerteneceAUsuario(User, departamento.Sucursal.EmpresaId))
                return Forbid();

            return Ok(departamento);
        }

        [HttpPost]
        public async Task<ActionResult<Departamento>> PostDepartamento(Departamento departamento)
        {
            var sucursal = await _context.Sucursal.FirstOrDefaultAsync(s => s.Id == departamento.SucursalId);
            if (sucursal == null)
                return BadRequest(new { mensaje = "La sucursal no existe." });

            if (!EmpresaAccess.PerteneceAUsuario(User, sucursal.EmpresaId))
                return Forbid();

            _context.Departamento.Add(departamento);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetDepartamento), new { id = departamento.Id }, departamento);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutDepartamento(int id, Departamento departamento)
        {
            if (id != departamento.Id)
                return BadRequest();

            var departamentoDb = await _context.Departamento
                .Include(d => d.Sucursal)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (departamentoDb == null)
                return NotFound();

            if (departamentoDb.Sucursal == null || !EmpresaAccess.PerteneceAUsuario(User, departamentoDb.Sucursal.EmpresaId))
                return Forbid();

            departamentoDb.Nombre = departamento.Nombre;
            departamentoDb.SucursalId = departamento.SucursalId;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDepartamento(int id)
        {
            var departamento = await _context.Departamento
                .Include(d => d.Sucursal)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (departamento == null)
                return NotFound();

            if (departamento.Sucursal == null || !EmpresaAccess.PerteneceAUsuario(User, departamento.Sucursal.EmpresaId))
                return Forbid();

            _context.Departamento.Remove(departamento);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
