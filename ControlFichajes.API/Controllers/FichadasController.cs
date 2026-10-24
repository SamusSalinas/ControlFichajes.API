using System.Security.Claims;
using ControlFichajes.API.Constants;
using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using ControlFichajes.API.Security;
using ControlFichajes.API.Services;
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
    private readonly FichadaBiometricaService _fichadaBiometricaService;
    private readonly ILaborClock _clock;

    public FichadasController(AppDbContext context, FichadaBiometricaService fichadaBiometricaService, ILaborClock clock)
    {
        _context = context;
        _fichadaBiometricaService = fichadaBiometricaService;
        _clock = clock;
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
                f.EsManual,
                f.Estado,
                f.MinutosHastaCorte,
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

        var consulta = new JornadaClasificacionConsulta(_context, _clock);
        var clasificacion = fichadas.Count == 0
            ? new Dictionary<int, ClasificacionFichada>()
            : await consulta.ClasificarEmpresaAsync(
                empresaIdUsuario,
                fichadas.Select(f => f.EmpleadoId).Distinct().ToList(),
                fichadas.Min(f => f.FechaHora),
                fichadas.Max(f => f.FechaHora));
        var respuesta = fichadas.Select(f =>
        {
            clasificacion.TryGetValue(f.Id, out var clase);
            return new
            {
                f.Id,
                f.EmpleadoId,
                f.Nombre,
                f.Apellido,
                f.Legajo,
                f.FechaHora,
                f.Tipo,
                f.Metodo,
                f.EsManual,
                f.Estado,
                MinutosHastaCorte = clase?.MinutosHastaCorte ?? f.MinutosHastaCorte,
                f.observacion,
                movimientoVisual = clase?.MovimientoVisual,
                jornadaEstado = clase?.JornadaEstado,
                jornadaMotivo = clase?.JornadaMotivo,
                jornadaFecha = clase?.JornadaFecha.ToString("yyyy-MM-dd")
            };
        });
        if (!string.IsNullOrWhiteSpace(tipo))
            respuesta = respuesta.Where(f => f.movimientoVisual == tipo);

        return Ok(respuesta);
    }

    [HttpGet("alertas/incompletas")]
    [Authorize(Roles = "ADMIN,SuperAdmin")]
    public async Task<IActionResult> GetAlertasIncompletas(
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] int limite = 100)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaId))
            return Forbid();
        if (limite is < 1 or > 500)
            return BadRequest(new { mensaje = "El límite debe estar entre 1 y 500." });

        var ahora = LaborTime.Ahora(_clock);
        var inicio = (desde ?? ahora.AddDays(-7)).Date;
        var fin = (hasta ?? ahora).Date.AddDays(1);
        if (fin <= inicio)
            return BadRequest(new { mensaje = "El rango de fechas no es válido." });

        var fichadas = await _context.Fichada
            .AsNoTracking()
            .Where(f => f.FechaHora >= inicio.AddDays(-1) && f.FechaHora < fin.AddDays(1) &&
                        f.Empleado != null && f.Empleado.EmpresaId == empresaId)
            .Select(f => new { f.Id, f.EmpleadoId, f.FechaHora, f.EsManual, nombre = f.Empleado!.Nombre, apellido = f.Empleado.Apellido })
            .ToListAsync();
        var consulta = new JornadaClasificacionConsulta(_context, _clock);
        var clasificacion = fichadas.Count == 0
            ? new Dictionary<int, ClasificacionFichada>()
            : await consulta.ClasificarEmpresaAsync(empresaId, fichadas.Select(f => f.EmpleadoId).Distinct().ToList(), inicio, fin);
        var alertas = fichadas
            .Where(f => clasificacion.TryGetValue(f.Id, out var clase) &&
                        clase.JornadaEstado == JornadaClasificador.Incompleta &&
                        clase.MovimientoVisual == JornadaClasificador.Entrada &&
                        clase.JornadaFecha >= DateOnly.FromDateTime(inicio) &&
                        clase.JornadaFecha < DateOnly.FromDateTime(fin))
            .OrderByDescending(f => f.FechaHora)
            .Take(limite)
            .Select(f => new
            {
                fichadaId = f.Id,
                f.EmpleadoId,
                f.nombre,
                f.apellido,
                f.FechaHora,
                minutosHastaCorte = clasificacion[f.Id].MinutosHastaCorte,
                f.EsManual,
                mensaje = "Falta una salida antes del cierre del día."
            });

        return Ok(alertas);
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

        var registros = new List<Fichada>(entrada.Count);
        var claves = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fichada in entrada)
        {
            var biometrica = fichada.Metodo is "Biometrico" or "Biométrico";
            var fecha = biometrica ? FichadaBiometricaIdempotencia.Normalizar(fichada.FechaHora) : fichada.FechaHora;
            var clave = biometrica ? FichadaBiometricaIdempotencia.Clave(fichada.EmpleadoId, fecha) : null;
            if (clave != null && !claves.Add(clave))
                continue;
            registros.Add(new Fichada
            {
                EmpleadoId = fichada.EmpleadoId,
                FechaHora = fecha,
                TipoRegistro = fichada.TipoRegistro,
                Metodo = fichada.Metodo,
                ClaveIdempotencia = clave
            });
        }

        if (claves.Count > 0)
        {
            var ocupadas = await _context.Fichada.AsNoTracking()
                .Where(f => f.ClaveIdempotencia != null && claves.Contains(f.ClaveIdempotencia))
                .Select(f => f.ClaveIdempotencia!)
                .ToListAsync();
            registros.RemoveAll(f => f.ClaveIdempotencia != null && ocupadas.Contains(f.ClaveIdempotencia));
        }

        for (var intento = 0; registros.Count > 0 && intento <= registros.Count; intento++)
        {
            _context.Fichada.AddRange(registros);
            try
            {
                await _context.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (FichadaBiometricaIdempotencia.EsConflictoDeClave(ex))
            {
                foreach (var entry in _context.ChangeTracker.Entries<Fichada>().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
                var pendientes = registros.Select(f => f.ClaveIdempotencia).Where(c => c != null).ToList();
                var confirmadas = await _context.Fichada.AsNoTracking()
                    .Where(f => f.ClaveIdempotencia != null && pendientes.Contains(f.ClaveIdempotencia))
                    .Select(f => f.ClaveIdempotencia!)
                    .ToListAsync();
                var antes = registros.Count;
                registros.RemoveAll(f => f.ClaveIdempotencia != null && confirmadas.Contains(f.ClaveIdempotencia));
                if (registros.Count == antes)
                    throw;
            }
        }

        return Ok(new { recibidas = entrada.Count });
    }

    [HttpPost("biometricas/bulk")]
    [Authorize(Policy = "SoloAgente")]
    public async Task<IActionResult> PostBulkBiometricas([FromBody] List<FichadaRequestDto> fichadas)
    {
        if (!EmpresaAccess.TryGetEmpresaId(User, out var empresaIdUsuario))
            return Forbid();
        if (fichadas is null || fichadas.Count == 0)
            return BadRequest(new { mensaje = "No se recibieron fichadas." });
        if (fichadas.Count > 500)
            return BadRequest(new { mensaje = "El lote no puede superar 500 fichadas." });
        if (fichadas.Any(f => f is null || f.EmpleadoId <= 0 || f.FechaHora == default))
            return BadRequest(new { mensaje = "EmpleadoId y FechaHora son obligatorios." });

        var resultado = await _fichadaBiometricaService.RegistrarAsync(fichadas, empresaIdUsuario);
        if (resultado.Error is not null)
            return BadRequest(new { mensaje = resultado.Error });

        return Ok(new { recibidas = resultado.Recibidas });
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
