using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FichadasController : ControllerBase
{
    private const string FichadaNoEncontrada = "Fichada no encontrada.";
    private const string PuedeEscribirObservacionFichada = "PuedeEscribirObservacionFichada";

    private readonly AppDbContext _context;

    public FichadasController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize]
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

        var query = ConsultaFichadasDeEmpresa(empresaIdUsuario).AsNoTracking();

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
                f.Metodo,
                observacion = f.Observacion == null
                    ? null
                    : new FichadaObservacionDto
                    {
                        FichadaId = f.Observacion.FichadaId,
                        Motivo = f.Observacion.Motivo,
                        Detalle = f.Observacion.Detalle,
                        CreadoPor = f.Observacion.CreadoPorNombre,
                        CreadoEn = f.Observacion.CreadoEn,
                        ModificadoPor = f.Observacion.ModificadoPorNombre,
                        ModificadoEn = f.Observacion.ModificadoEn
                    }
            })
            .ToListAsync();

        return Ok(fichadas);
    }

    [HttpPatch("{id:int}/observacion")]
    [Authorize(Policy = PuedeEscribirObservacionFichada)]
    public async Task<IActionResult> PatchObservacion(int id, [FromBody] FichadaObservacionWriteDto? request)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaIdUsuario))
            return Forbid();

        var motivo = request?.Motivo?.Trim() ?? string.Empty;
        var detalle = request?.Detalle?.Trim() ?? string.Empty;

        if (!FichadaObservacionMotivos.EsValido(motivo))
            return BadRequest(new { mensaje = "El motivo de la observación no es válido." });
        if (string.IsNullOrEmpty(detalle))
            return BadRequest(new { mensaje = "El detalle de la observación es obligatorio." });
        if (detalle.Length > FichadaObservacionMotivos.DetalleMaxLength)
            return BadRequest(new { mensaje = $"El detalle no puede superar {FichadaObservacionMotivos.DetalleMaxLength} caracteres." });

        var fichada = await ConsultaFichadasDeEmpresa(empresaIdUsuario)
            .Include(f => f.Observacion)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (fichada == null)
            return NotFound(new { mensaje = FichadaNoEncontrada });

        var ahora = DateTime.UtcNow;
        var autorId = await ResolverUsuarioIdAsync();
        var autorNombre = ResolverNombreAutor();

        if (fichada.Observacion == null)
        {
            var nueva = new FichadaObservacion
            {
                FichadaId = fichada.Id,
                Motivo = motivo,
                Detalle = detalle,
                CreadoPorUsuarioId = autorId,
                CreadoPorNombre = autorNombre,
                CreadoEn = ahora
            };
            fichada.Observacion = nueva;
            _context.FichadaObservacion.Add(nueva);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (FichadaObservacionPersistencia.EsDuplicadoClaveMySql(ex))
            {
                var recuperada = await FichadaObservacionPersistencia.RecuperarTrasInsertDuplicadoAsync(
                    _context,
                    fichada,
                    nueva,
                    motivo,
                    detalle,
                    autorId,
                    autorNombre,
                    ahora);
                if (recuperada is null)
                    throw;
                return Ok(MapearObservacion(recuperada));
            }
        }
        else
        {
            fichada.Observacion.Motivo = motivo;
            fichada.Observacion.Detalle = detalle;
            fichada.Observacion.ModificadoPorUsuarioId = autorId;
            fichada.Observacion.ModificadoPorNombre = autorNombre;
            fichada.Observacion.ModificadoEn = ahora;
            await _context.SaveChangesAsync();
        }

        return Ok(MapearObservacion(fichada.Observacion));
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

        var empleadosActivos = await _context.Empleado
            .Where(e => empleadoIds.Contains(e.Id) && e.Activo && e.EmpresaId == empresaIdUsuario)
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

    private IQueryable<Fichada> ConsultaFichadasDeEmpresa(int empresaId)
    {
        return _context.Fichada
            .Where(f => f.Empleado != null && f.Empleado.Activo && f.Empleado.EmpresaId == empresaId);
    }

    private static FichadaObservacionDto MapearObservacion(FichadaObservacion observacion)
    {
        return new FichadaObservacionDto
        {
            FichadaId = observacion.FichadaId,
            Motivo = observacion.Motivo,
            Detalle = observacion.Detalle,
            CreadoPor = observacion.CreadoPorNombre,
            CreadoEn = observacion.CreadoEn,
            ModificadoPor = observacion.ModificadoPorNombre,
            ModificadoEn = observacion.ModificadoEn
        };
    }

    private string ResolverNombreAutor()
    {
        var nombre = User.FindFirstValue(ClaimTypes.Name)
            ?? User.Identity?.Name
            ?? "Usuario";
        nombre = nombre.Trim();
        if (nombre.Length == 0)
            nombre = "Usuario";
        if (nombre.Length > FichadaObservacionMotivos.NombreMaxLength)
            return nombre[..FichadaObservacionMotivos.NombreMaxLength];
        return nombre;
    }

    private async Task<int?> ResolverUsuarioIdAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId) || usuarioId <= 0)
            return null;

        var existe = await _context.Usuario.AsNoTracking().AnyAsync(u => u.Id == usuarioId);
        return existe ? usuarioId : null;
    }
}
