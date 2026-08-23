using ControlFichajes.API.Data;
using ControlFichajes.API.Models;
using ControlFichajes.API.DTOs;
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
            // Filtramos automáticamente para no mostrar empleados dados de baja
            return await _context.Empleado.Where(e => e.Activo).ToListAsync();
        }

        public async Task<Empleado?> ObtenerPorIdAsync(int id)
        {
            return await _context.Empleado.FirstOrDefaultAsync(e => e.Id == id && e.Activo);
        }

        public async Task<Empleado> CrearAsync(EmpleadoRegistroDto dto)
        {
            // 1. Regla de negocio: Evitar duplicados
            bool existe = await _context.Empleado.AnyAsync(e => e.DNI == dto.DNI || e.CUIL == dto.CUIL);
            if (existe)
                throw new Exception("El DNI o CUIL ya se encuentra registrado en el sistema.");

            // 2. Mapeo manual del DTO a la Entidad
            var nuevoEmpleado = new Empleado
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

            _context.Empleado.Add(nuevoEmpleado);
            await _context.SaveChangesAsync();

            return nuevoEmpleado;
        }

        public async Task<bool> BorradoLogicoAsync(int id)
        {
            var empleado = await _context.Empleado.FindAsync(id);
            if (empleado == null) return false;

            // En lugar de borrar físicamente, cambiamos el estado
            empleado.Activo = false;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}