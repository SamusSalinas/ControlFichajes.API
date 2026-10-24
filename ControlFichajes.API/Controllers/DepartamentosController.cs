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
    [Authorize]
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
        [Authorize(Policy = "PuedeAdministrarDepartamentos")]
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
        [Authorize(Policy = "PuedeAdministrarDepartamentos")]
        public async Task<IActionResult> PutDepartamento(int id, DepartamentoCrearDto request)
        {
            var departamentoDb = await _context.Departamento
                .Include(d => d.Sucursal)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (departamentoDb == null)
                return NotFound();

            if (departamentoDb.Sucursal == null || !EmpresaAccess.PerteneceAUsuario(User, departamentoDb.Sucursal.EmpresaId))
                return Forbid();

            var nombre = request.Nombre?.Trim() ?? string.Empty;
            if (nombre.Length == 0 || nombre.Length > 100)
                return BadRequest(new { mensaje = "El nombre del departamento es obligatorio y no puede superar 100 caracteres." });

            var duplicado = await _context.Departamento.AnyAsync(d =>
                d.Id != id && d.SucursalId == departamentoDb.SucursalId && d.Nombre == nombre);
            if (duplicado)
                return Conflict(new { mensaje = "Ya existe un departamento con ese nombre en esa sucursal." });

            departamentoDb.Nombre = nombre;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "PuedeAdministrarDepartamentos")]
        public async Task<IActionResult> DeleteDepartamento(int id)
        {
            var departamento = await _context.Departamento
                .Include(d => d.Sucursal)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (departamento == null)
                return NotFound();

            if (departamento.Sucursal == null || !EmpresaAccess.PerteneceAUsuario(User, departamento.Sucursal.EmpresaId))
                return Forbid();

            var asignados = await _context.Empleado.CountAsync(e => e.DepartamentoId == id);
            if (asignados > 0)
            {
                return Conflict(new
                {
                    mensaje = $"No se puede eliminar el departamento porque tiene {asignados} empleados asignados. Reasigná esos empleados antes de eliminarlo.",
                    cantidadEmpleados = asignados
                });
            }

            _context.Departamento.Remove(departamento);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                foreach (var entry in _context.ChangeTracker.Entries())
                {
                    if (entry.State == EntityState.Deleted)
                        entry.State = EntityState.Unchanged;
                }

                return Conflict(new
                {
                    mensaje = "No se puede eliminar el departamento porque tiene empleados asignados. Reasigná esos empleados antes de eliminarlo.",
                    cantidadEmpleados = await _context.Empleado.CountAsync(e => e.DepartamentoId == id)
                });
            }

            return NoContent();
        }
    }
}
