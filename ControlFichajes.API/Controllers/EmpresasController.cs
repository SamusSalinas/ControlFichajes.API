using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Protegido por JWT
    public class EmpresasController : ControllerBase
    {
        private readonly AppDbContext _context;

        public EmpresasController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Empresa>>> GetEmpresas()
        {
            if (EmpresaAccess.IsSuperAdmin(User))
                return await _context.Empresa.ToListAsync();

            if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
                return Forbid();

            return await _context.Empresa.Where(e => e.Id == empresaId).ToListAsync();
        }

        [HttpPost]
        [Authorize(Policy = "SoloSuperadmin")]
        public async Task<ActionResult<Empresa>> PostEmpresa(Empresa empresa)
        {
            if (!EmpresaAccess.IsSuperAdmin(User))
                return Forbid();

            _context.Empresa.Add(empresa);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetEmpresas), new { id = empresa.Id }, empresa);
        }
    }
}