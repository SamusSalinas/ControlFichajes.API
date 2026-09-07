using ControlFichajes.API.Data;
using ControlFichajes.API.DTOs;
using ControlFichajes.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Services
{
    public class EmpleadoService : IEmpleadoService
    {
        private readonly AppDbContext _context;

        public EmpleadoService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<EmpleadoDto>> ObtenerTodosActivosAsync()
        {
            return await Proyectar(_context.Empleado
                    .AsNoTracking()
                    .Where(e => e.Activo))
                .ToListAsync();
        }

        public async Task<IEnumerable<EmpleadoDto>> ObtenerActivosPorEmpresaAsync(int empresaId)
        {
            return await Proyectar(_context.Empleado
                    .AsNoTracking()
                    .Where(e => e.EmpresaId == empresaId && e.Activo))
                .ToListAsync();
        }

        public async Task<EmpleadoDto?> ObtenerPorIdAsync(int id)
        {
            return await Proyectar(_context.Empleado
                    .AsNoTracking()
                    .Where(e => e.Id == id && e.Activo))
                .FirstOrDefaultAsync();
        }

        public async Task<EmpleadoDto> CrearAsync(EmpleadoRegistroDto dto)
        {
            await ValidarEmpleadoNoDuplicadoAsync(dto);

            var sucursalId = await ResolverSucursalIdAsync(dto.EmpresaId, dto.SucursalId, dto.Sucursal);
            var departamentoId = await ResolverDepartamentoIdAsync(
                dto.EmpresaId,
                sucursalId,
                dto.DepartamentoId,
                dto.Departamento);

            await ValidarRelacionesAsync(dto.EmpresaId, sucursalId, departamentoId);

            var nuevoEmpleado = MapearAEntidad(dto, sucursalId, departamentoId);
            _context.Empleado.Add(nuevoEmpleado);
            await _context.SaveChangesAsync();

            return (await ObtenerPorIdAsync(nuevoEmpleado.Id))!;
        }

        public async Task<EmpleadoDto?> ActualizarAsync(int id, int empresaId, EmpleadoPatchDto dto)
        {
            var empleado = await _context.Empleado
                .FirstOrDefaultAsync(e => e.Id == id && e.EmpresaId == empresaId && e.Activo);

            if (empleado == null)
                return null;

            var sucursalId = empleado.SucursalId;
            var departamentoId = empleado.DepartamentoId;
            var relacionesCambiaron = false;

            if (dto.SucursalId.HasValue)
            {
                sucursalId = dto.SucursalId;
                relacionesCambiaron = true;
            }
            else if (dto.Sucursal is not null)
            {
                sucursalId = string.IsNullOrWhiteSpace(dto.Sucursal)
                    ? null
                    : await BuscarSucursalIdPorNombreAsync(empresaId, dto.Sucursal);
                relacionesCambiaron = true;
            }

            if (dto.DepartamentoId.HasValue)
            {
                departamentoId = dto.DepartamentoId;
                relacionesCambiaron = true;
            }
            else if (dto.Departamento is not null)
            {
                departamentoId = string.IsNullOrWhiteSpace(dto.Departamento)
                    ? null
                    : await BuscarDepartamentoIdPorNombreAsync(empresaId, sucursalId, dto.Departamento);
                relacionesCambiaron = true;
            }

            if (relacionesCambiaron)
                await ValidarRelacionesAsync(empresaId, sucursalId, departamentoId);

            if (!string.IsNullOrWhiteSpace(dto.Legajo))
                empleado.Legajo = dto.Legajo;

            if (!string.IsNullOrWhiteSpace(dto.DNI))
            {
                if (await _context.Empleado.AnyAsync(e => e.Id != id && e.DNI == dto.DNI))
                    throw new Exception("El DNI ya se encuentra registrado en el sistema.");

                empleado.DNI = dto.DNI;
            }

            if (!string.IsNullOrWhiteSpace(dto.CUIL))
            {
                if (await _context.Empleado.AnyAsync(e => e.Id != id && e.CUIL == dto.CUIL))
                    throw new Exception("El CUIL ya se encuentra registrado en el sistema.");

                empleado.CUIL = dto.CUIL;
            }

            if (!string.IsNullOrWhiteSpace(dto.Nombre))
                empleado.Nombre = dto.Nombre;

            if (!string.IsNullOrWhiteSpace(dto.Apellido))
                empleado.Apellido = dto.Apellido;

            if (dto.Categoria != null)
                empleado.Categoria = dto.Categoria;

            if (dto.Horario != null)
                empleado.Horario = dto.Horario;

            if (relacionesCambiaron)
            {
                empleado.SucursalId = sucursalId;
                empleado.DepartamentoId = departamentoId;
            }

            await _context.SaveChangesAsync();
            return await ObtenerPorIdAsync(id);
        }

        public async Task<bool> BorradoLogicoAsync(int id, int empresaId)
        {
            var empleado = await _context.Empleado
                .FirstOrDefaultAsync(e => e.Id == id && e.EmpresaId == empresaId && e.Activo);

            if (empleado == null)
                return false;

            empleado.Activo = false;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> EnrolarHuellaAsync(HuellaEnrolarDto dto, int empresaId)
        {
            var empleado = await _context.Empleado
                .FirstOrDefaultAsync(e => e.Id == dto.EmpleadoId && e.EmpresaId == empresaId && e.Activo);

            if (empleado == null || string.IsNullOrWhiteSpace(dto.TemplateHuellaBase64))
                return false;

            var nuevaHuella = new Huella
            {
                EmpleadoId = dto.EmpleadoId,
                TemplateBiometrico = dto.TemplateHuellaBase64,
                IndiceDedo = dto.IndiceDedo,
                FechaRegistro = DateTime.UtcNow
            };

            _context.Huella.Add(nuevaHuella);
            await _context.SaveChangesAsync();
            return true;
        }

        private static IQueryable<EmpleadoDto> Proyectar(IQueryable<Empleado> query)
        {
            return query.Select(e => new EmpleadoDto
            {
                Id = e.Id,
                EmpresaId = e.EmpresaId,
                Legajo = e.Legajo,
                DNI = e.DNI,
                CUIL = e.CUIL,
                Nombre = e.Nombre,
                Apellido = e.Apellido,
                DepartamentoId = e.DepartamentoId,
                Departamento = e.DepartamentoEntidad != null ? e.DepartamentoEntidad.Nombre : null,
                Categoria = e.Categoria,
                SucursalId = e.SucursalId,
                Sucursal = e.SucursalEntidad != null ? e.SucursalEntidad.Nombre : null,
                Horario = e.Horario,
                Activo = e.Activo,
                TieneHuella = e.Huellas.Any()
            });
        }

        private async Task ValidarEmpleadoNoDuplicadoAsync(EmpleadoRegistroDto dto)
        {
            var existe = await _context.Empleado
                .AnyAsync(e => e.DNI == dto.DNI || e.CUIL == dto.CUIL);

            if (existe)
                throw new Exception("El DNI o CUIL ya se encuentra registrado en el sistema.");
        }

        private async Task<int?> ResolverSucursalIdAsync(int empresaId, int? sucursalId, string? sucursal)
        {
            if (sucursalId.HasValue)
                return sucursalId;

            return string.IsNullOrWhiteSpace(sucursal)
                ? null
                : await BuscarSucursalIdPorNombreAsync(empresaId, sucursal);
        }

        private async Task<int?> ResolverDepartamentoIdAsync(
            int empresaId,
            int? sucursalId,
            int? departamentoId,
            string? departamento)
        {
            if (departamentoId.HasValue)
                return departamentoId;

            return string.IsNullOrWhiteSpace(departamento)
                ? null
                : await BuscarDepartamentoIdPorNombreAsync(empresaId, sucursalId, departamento);
        }

        private async Task<int> BuscarSucursalIdPorNombreAsync(int empresaId, string nombre)
        {
            var nombreNormalizado = nombre.Trim();
            var sucursalId = await _context.Sucursal
                .AsNoTracking()
                .Where(s => s.EmpresaId == empresaId && s.Nombre == nombreNormalizado)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();

            return sucursalId
                ?? throw new Exception("La sucursal no existe en la empresa.");
        }

        private async Task<int> BuscarDepartamentoIdPorNombreAsync(
            int empresaId,
            int? sucursalId,
            string nombre)
        {
            var nombreNormalizado = nombre.Trim();
            var query = _context.Departamento
                .AsNoTracking()
                .Where(d => d.Nombre == nombreNormalizado);

            if (sucursalId.HasValue)
            {
                query = query.Where(d => d.SucursalId == sucursalId);
            }
            else
            {
                query = query.Where(d =>
                    _context.Sucursal.Any(s => s.Id == d.SucursalId && s.EmpresaId == empresaId));
            }

            var coincidencias = await query
                .Select(d => d.Id)
                .Take(2)
                .ToListAsync();

            if (coincidencias.Count == 0)
                throw new Exception("El departamento no existe en la empresa o sucursal.");
            if (coincidencias.Count > 1)
                throw new Exception("Hay más de un departamento con ese nombre. Envíe DepartamentoId.");

            return coincidencias[0];
        }

        private async Task ValidarRelacionesAsync(
            int empresaId,
            int? sucursalId,
            int? departamentoId)
        {
            if (sucursalId.HasValue &&
                !await _context.Sucursal.AnyAsync(s => s.Id == sucursalId && s.EmpresaId == empresaId))
            {
                throw new Exception("La sucursal no pertenece a la empresa.");
            }

            if (!departamentoId.HasValue)
                return;

            var departamentoValido = await _context.Departamento
                .Where(d => d.Id == departamentoId &&
                    (!sucursalId.HasValue || d.SucursalId == sucursalId))
                .Join(
                    _context.Sucursal,
                    departamento => departamento.SucursalId,
                    sucursal => sucursal.Id,
                    (_, sucursal) => sucursal.EmpresaId)
                .AnyAsync(id => id == empresaId);

            if (!departamentoValido)
                throw new Exception("El departamento no pertenece a la empresa o sucursal.");
        }

        private static Empleado MapearAEntidad(
            EmpleadoRegistroDto dto,
            int? sucursalId,
            int? departamentoId)
        {
            return new Empleado
            {
                EmpresaId = dto.EmpresaId,
                Legajo = dto.Legajo,
                DNI = dto.DNI,
                CUIL = dto.CUIL,
                Nombre = dto.Nombre,
                Apellido = dto.Apellido,
                DepartamentoId = departamentoId,
                Categoria = dto.Categoria,
                SucursalId = sucursalId,
                Horario = dto.Horario,
                Activo = true
            };
        }
    }
}