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
            return await ProjectToDto(_context.Empleado.AsNoTracking().Where(e => e.Activo))
                .ToListAsync();
        }

        public async Task<IEnumerable<EmpleadoDto>> ObtenerActivosPorEmpresaAsync(int empresaId)
        {
            return await ProjectToDto(_context.Empleado
                    .AsNoTracking()
                    .Where(e => e.EmpresaId == empresaId && e.Activo))
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

        public async Task<EmpleadoDto?> ObtenerPorIdAsync(int id)
        {
            return await ProjectToDto(_context.Empleado
                    .AsNoTracking()
                    .Where(e => e.Id == id && e.Activo))
                .FirstOrDefaultAsync();
        }

        public async Task<EmpleadoDto> CrearAsync(EmpleadoRegistroDto dto)
        {
            var sucursalId = await ResolverSucursalIdAsync(dto.EmpresaId, dto.SucursalId, dto.Sucursal);
            var departamentoId = await ResolverDepartamentoIdAsync(
                dto.EmpresaId, sucursalId, dto.DepartamentoId, dto.Departamento);

            await ValidarRelacionesAsync(dto.EmpresaId, sucursalId, departamentoId);
            await ValidarEmpleadoNoDuplicadoAsync(dto);

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

            var (sucursalId, sucursalCambia) = await ResolverSucursalPatchAsync(empresaId, empleado.SucursalId, dto);
            var (departamentoId, departamentoCambia) = await ResolverDepartamentoPatchAsync(
                empresaId, sucursalId, empleado.DepartamentoId, dto);

            if (sucursalCambia && sucursalId != empleado.SucursalId && !departamentoCambia)
            {
                if (!await DepartamentoPerteneceASucursalAsync(empleado.DepartamentoId, sucursalId))
                {
                    departamentoId = null;
                    departamentoCambia = true;
                }
            }

            if (!sucursalId.HasValue && departamentoId.HasValue)
            {
                departamentoId = null;
                departamentoCambia = true;
            }

            await ValidarRelacionesAsync(empresaId, sucursalId, departamentoId);

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

            if (dto.Categoria != null)
                empleado.Categoria = dto.Categoria;

            if (dto.Horario != null)
                empleado.Horario = dto.Horario;

            if (sucursalCambia)
                empleado.SucursalId = sucursalId;

            if (departamentoCambia)
                empleado.DepartamentoId = departamentoId;

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

        private static IQueryable<EmpleadoDto> ProjectToDto(IQueryable<Empleado> query)
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
                Departamento = e.DepartamentoEntidad != null ? e.DepartamentoEntidad.Nombre : null,
                DepartamentoId = e.DepartamentoId,
                Categoria = e.Categoria,
                Sucursal = e.SucursalEntidad != null ? e.SucursalEntidad.Nombre : null,
                SucursalId = e.SucursalId,
                Horario = e.Horario,
                Activo = e.Activo
            });
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

        private async Task<int?> ResolverSucursalIdAsync(int empresaId, int? sucursalId, string? sucursalNombre)
        {
            if (sucursalId.HasValue)
                return sucursalId;

            if (string.IsNullOrWhiteSpace(sucursalNombre))
                return null;

            return await BuscarSucursalIdPorNombreAsync(empresaId, sucursalNombre);
        }

        private async Task<int?> ResolverDepartamentoIdAsync(
            int empresaId, int? sucursalId, int? departamentoId, string? departamentoNombre)
        {
            if (departamentoId.HasValue)
                return departamentoId;

            if (string.IsNullOrWhiteSpace(departamentoNombre))
                return null;

            return await BuscarDepartamentoIdPorNombreAsync(empresaId, sucursalId, departamentoNombre);
        }

        private async Task<(int? Id, bool Cambia)> ResolverSucursalPatchAsync(
            int empresaId, int? sucursalActualId, EmpleadoPatchDto dto)
        {
            if (dto.SucursalId.HasValue)
                return (dto.SucursalId, true);

            if (dto.Sucursal is null)
                return (sucursalActualId, false);

            if (string.IsNullOrWhiteSpace(dto.Sucursal))
                return (null, true);

            return (await BuscarSucursalIdPorNombreAsync(empresaId, dto.Sucursal), true);
        }

        private async Task<(int? Id, bool Cambia)> ResolverDepartamentoPatchAsync(
            int empresaId, int? sucursalId, int? departamentoActualId, EmpleadoPatchDto dto)
        {
            if (dto.DepartamentoId.HasValue)
                return (dto.DepartamentoId, true);

            if (dto.Departamento is null)
                return (departamentoActualId, false);

            if (string.IsNullOrWhiteSpace(dto.Departamento))
                return (null, true);

            return (await BuscarDepartamentoIdPorNombreAsync(empresaId, sucursalId, dto.Departamento), true);
        }

        private async Task<int> BuscarSucursalIdPorNombreAsync(int empresaId, string nombre)
        {
            var normalizado = nombre.Trim();
            var sucursal = await _context.Sucursal
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.EmpresaId == empresaId && s.Nombre == normalizado);

            if (sucursal == null)
                throw new Exception("La sucursal no existe en la empresa. Envíe sucursalId o el nombre de una sucursal existente.");

            return sucursal.Id;
        }

        private async Task<int> BuscarDepartamentoIdPorNombreAsync(int empresaId, int? sucursalId, string nombre)
        {
            var normalizado = nombre.Trim();
            var query = _context.Departamento
                .AsNoTracking()
                .Where(d => d.Nombre == normalizado);

            if (sucursalId.HasValue)
            {
                query = query.Where(d => d.SucursalId == sucursalId);
            }
            else
            {
                query = query.Where(d => d.Sucursal != null && d.Sucursal.EmpresaId == empresaId);
            }

            var coincidencias = await query.Select(d => d.Id).Take(2).ToListAsync();
            if (coincidencias.Count == 0)
                throw new Exception("El departamento no existe en la empresa o sucursal. Envíe departamentoId o el nombre de un departamento existente.");
            if (coincidencias.Count > 1)
                throw new Exception("Hay más de un departamento con ese nombre. Envíe departamentoId.");

            return coincidencias[0];
        }

        private async Task<bool> DepartamentoPerteneceASucursalAsync(int? departamentoId, int? sucursalId)
        {
            if (!departamentoId.HasValue)
                return true;
            if (!sucursalId.HasValue)
                return false;

            return await _context.Departamento
                .AsNoTracking()
                .AnyAsync(d => d.Id == departamentoId && d.SucursalId == sucursalId);
        }

        private static Empleado MapearAEntidad(EmpleadoRegistroDto dto, int? sucursalId, int? departamentoId)
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
