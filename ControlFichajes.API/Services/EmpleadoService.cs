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

        public async Task<IEnumerable<Empleado>> ObtenerTodosActivosAsync()
        {
            return await _context.Empleado
                .AsNoTracking()
                .Where(e => e.Activo)
                .ToListAsync();
        }

        public async Task<IEnumerable<Empleado>> ObtenerActivosPorEmpresaAsync(int empresaId)
        {
            return await _context.Empleado
                .AsNoTracking()
                .Where(e => e.EmpresaId == empresaId && e.Activo)
                .ToListAsync();
        }

        public async Task<IEnumerable<EmpleadoAgenteDto>> ObtenerCatalogoAgenteAsync(int empresaId, int sucursalId)
        {
            return await _context.Empleado
                .AsNoTracking()
                .Where(e => e.EmpresaId == empresaId && e.SucursalId == sucursalId && e.Activo)
                .Select(e => new EmpleadoAgenteDto
                {
                    Id = e.Id,
                    Legajo = e.Legajo,
                    DNI = e.DNI,
                    CUIL = e.CUIL,
                    Nombre = e.Nombre,
                    Apellido = e.Apellido,
                    DepartamentoId = e.DepartamentoId,
                    SucursalId = e.SucursalId,
                    TieneHuella = e.Huellas.Any()
                })
                .ToListAsync();
        }

        public async Task<Empleado?> ObtenerPorIdAsync(int id)
        {
            return await _context.Empleado
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.Activo);
        }

        public async Task<Empleado> CrearAsync(EmpleadoRegistroDto dto)
        {
            await ValidarRelacionesAsync(dto.EmpresaId, dto.SucursalId, dto.DepartamentoId);
            await ValidarEmpleadoNoDuplicadoAsync(dto);

            var nuevoEmpleado = MapearAEntidad(dto);
            _context.Empleado.Add(nuevoEmpleado);
            await _context.SaveChangesAsync();

            return nuevoEmpleado;
        }

        public async Task<Empleado?> ActualizarAsync(int id, int empresaId, EmpleadoPatchDto dto)
        {
            var empleado = await _context.Empleado
                .FirstOrDefaultAsync(e => e.Id == id && e.EmpresaId == empresaId && e.Activo);

            if (empleado == null)
                return null;

            await ValidarRelacionesAsync(empresaId, dto.SucursalId, dto.DepartamentoId);

            if (!string.IsNullOrWhiteSpace(dto.Legajo))
                empleado.Legajo = dto.Legajo;

            if (!string.IsNullOrWhiteSpace(dto.DNI))
            {
                if (await _context.Empleado.AnyAsync(e => e.Id != id && e.EmpresaId == empresaId && e.DNI == dto.DNI))
                    throw new Exception("El DNI ya se encuentra registrado en el sistema.");

                empleado.DNI = dto.DNI;
            }

            if (!string.IsNullOrWhiteSpace(dto.CUIL))
            {
                if (await _context.Empleado.AnyAsync(e => e.Id != id && e.EmpresaId == empresaId && e.CUIL == dto.CUIL))
                    throw new Exception("El CUIL ya se encuentra registrado en el sistema.");

                empleado.CUIL = dto.CUIL;
            }

            if (!string.IsNullOrWhiteSpace(dto.Nombre))
                empleado.Nombre = dto.Nombre;

            if (!string.IsNullOrWhiteSpace(dto.Apellido))
                empleado.Apellido = dto.Apellido;

            if (dto.Departamento != null)
                empleado.Departamento = dto.Departamento;

            if (dto.Categoria != null)
                empleado.Categoria = dto.Categoria;

            if (dto.Sucursal != null)
                empleado.Sucursal = dto.Sucursal;

            if (dto.Horario != null)
                empleado.Horario = dto.Horario;

            await _context.SaveChangesAsync();
            return empleado;
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

        public async Task<bool> EnrolarHuellaAsync(HuellaEnrolarDto dto, int empresaId, int? sucursalId = null)
        {
            var empleado = await _context.Empleado
                .FirstOrDefaultAsync(e => e.Id == dto.EmpleadoId && e.EmpresaId == empresaId && e.Activo &&
                    (!sucursalId.HasValue || e.SucursalId == sucursalId));

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

        private async Task ValidarEmpleadoNoDuplicadoAsync(EmpleadoRegistroDto dto)
        {
            var existe = await _context.Empleado
                .AnyAsync(e => e.EmpresaId == dto.EmpresaId && (e.DNI == dto.DNI || e.CUIL == dto.CUIL));

            if (existe)
                throw new Exception("El DNI o CUIL ya se encuentra registrado en el sistema.");
        }

        private async Task ValidarRelacionesAsync(int empresaId, int? sucursalId, int? departamentoId)
        {
            if (sucursalId.HasValue && !await _context.Sucursal.AnyAsync(s => s.Id == sucursalId && s.EmpresaId == empresaId))
                throw new Exception("La sucursal no pertenece a la empresa.");

            if (departamentoId.HasValue)
            {
                var departamentoValido = await _context.Departamento
                    .Where(d => d.Id == departamentoId && (!sucursalId.HasValue || d.SucursalId == sucursalId))
                    .Join(_context.Sucursal,
                        departamento => departamento.SucursalId,
                        sucursal => sucursal.Id,
                        (_, sucursal) => sucursal.EmpresaId)
                    .AnyAsync(id => id == empresaId);

                if (!departamentoValido)
                    throw new Exception("El departamento no pertenece a la empresa o sucursal.");
            }
        }

        private static Empleado MapearAEntidad(EmpleadoRegistroDto dto)
        {
            return new Empleado
            {
                EmpresaId = dto.EmpresaId,
                Legajo = dto.Legajo,
                DNI = dto.DNI,
                CUIL = dto.CUIL,
                Nombre = dto.Nombre,
                Apellido = dto.Apellido,
                Departamento = dto.Departamento,
                DepartamentoId = dto.DepartamentoId,
                Categoria = dto.Categoria,
                Sucursal = dto.Sucursal,
                SucursalId = dto.SucursalId,
                Horario = dto.Horario,
                Activo = true
            };
        }
    }
}