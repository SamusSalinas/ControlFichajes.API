using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class EmpresasController : ControllerBase
    {
        private readonly AppDbContext _context;

        public EmpresasController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [Authorize(Policy = "LeeEmpresa")]
        public async Task<ActionResult<IEnumerable<EmpresaDto>>> GetEmpresas()
        {
            var query = _context.Empresa.AsNoTracking().AsQueryable();
            if (User.IsInRole(Constants.AppRoles.Superadmin))
            {
                if (Request.Headers.TryGetValue("X-Empresa-Id", out var empresaHeader) &&
                    int.TryParse(empresaHeader, out var empresaContexto))
                {
                    query = query.Where(e => e.Id == empresaContexto);
                }
            }
            else
            {
                if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
                    return Forbid();

                query = query.Where(e => e.Id == empresaId);
            }

            return await query.Select(e => new EmpresaDto
            {
                Id = e.Id,
                Nombre = e.NombreFantasia,
                Cuit = e.CUIT,
                Direccion = e.RazonSocial
            }).ToListAsync();
        }

        [HttpPost]
        [Authorize(Policy = "SoloSuperadmin")]
        public async Task<ActionResult<Empresa>> PostEmpresa(Empresa empresa)
        {
            _context.Empresa.Add(empresa);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetEmpresas), new { id = empresa.Id }, empresa);
        }
    }
}