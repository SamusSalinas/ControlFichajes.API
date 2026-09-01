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

        public async Task<Empleado?> ObtenerPorIdAsync(int id)
        {
            return await _context.Empleado
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.Activo);
        }

        public async Task<Empleado> CrearAsync(EmpleadoRegistroDto dto)
        {
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

        private async Task ValidarEmpleadoNoDuplicadoAsync(EmpleadoRegistroDto dto)
        {
            var existe = await _context.Empleado
                .AnyAsync(e => e.DNI == dto.DNI || e.CUIL == dto.CUIL);

            if (existe)
                throw new Exception("El DNI o CUIL ya se encuentra registrado en el sistema.");
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
                Categoria = dto.Categoria,
                Sucursal = dto.Sucursal,
                Horario = dto.Horario,
                Activo = true
            };
        }
    }
}