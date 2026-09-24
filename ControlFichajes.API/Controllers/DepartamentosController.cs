using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize (Policy = "PuedeAdministrarDepartamentos")]
    public class DepartamentosController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DepartamentosController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetDepartamentos()
        {
            if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
                return Forbid();

            var departamentos = await _context.Departamento
                .Where(d => d.Sucursal != null && d.Sucursal.EmpresaId == empresaId)
                .Select(d => new DepartamentoDto
                {
                    Id = d.Id,
                    Nombre = d.Nombre,
                    SucursalId = d.SucursalId
                })
                .ToListAsync();

            return Ok(departamentos);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetDepartamento(int id)
        {
            var departamento = await _context.Departamento
                .Include(d => d.Sucursal)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (departamento == null)
                return NotFound();

            if (departamento.Sucursal == null || !EmpresaAccess.PerteneceAUsuario(User, departamento.Sucursal.EmpresaId))
                return Forbid();

            return Ok(new DepartamentoDto
            {
                Id = departamento.Id,
                Nombre = departamento.Nombre,
                SucursalId = departamento.SucursalId
            });
        }

        [HttpPost]
        public async Task<IActionResult> PostDepartamento(DepartamentoCrearDto request)
        {
            var sucursal = await _context.Sucursal.FirstOrDefaultAsync(s => s.Id == request.SucursalId);
            if (sucursal == null)
                return BadRequest(new { mensaje = "La sucursal no existe." });

            if (!EmpresaAccess.PerteneceAUsuario(User, sucursal.EmpresaId))
                return Forbid();

            bool existe = await _context.Departamento
                .AnyAsync(d => d.Nombre == request.Nombre && d.SucursalId == request.SucursalId);
            if (existe)
            {
                return Conflict(new { mensaje = "Ya existe un departamento con ese nombre en esa sucursal." });
            }

            var departamento = new Departamento
            {
                Nombre = request.Nombre,
                SucursalId = request.SucursalId
            };

            _context.Departamento.Add(departamento);
            await _context.SaveChangesAsync();

            var dto = new DepartamentoDto
            {
                Id = departamento.Id,
                Nombre = departamento.Nombre,
                SucursalId = departamento.SucursalId
            };

            return CreatedAtAction(nameof(GetDepartamento), new { id = departamento.Id }, dto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutDepartamento(int id, DepartamentoCrearDto request)
        {
            var departamentoDb = await _context.Departamento
                .Include(d => d.Sucursal)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (departamentoDb == null)
                return NotFound();

            if (departamentoDb.Sucursal == null || !EmpresaAccess.PerteneceAUsuario(User, departamentoDb.Sucursal.EmpresaId))
                return Forbid();

            departamentoDb.Nombre = request.Nombre;
            departamentoDb.SucursalId = request.SucursalId;

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
