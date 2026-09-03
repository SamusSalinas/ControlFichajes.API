using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs
{
    public class EmpleadoRegistroDto
    {
        [Required]
        public int EmpresaId { get; set; }

        public string? Legajo { get; set; }

        [Required, MaxLength(15)]
        public string DNI { get; set; } = string.Empty;

        [Required, MaxLength(15)]
        public string CUIL { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Nombre { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Apellido { get; set; } = string.Empty;

        public string? Departamento { get; set; }
        public int? DepartamentoId { get; set; }
        public string? Categoria { get; set; }
        public string? Sucursal { get; set; }
        public int? SucursalId { get; set; }
        public string? Horario { get; set; }
    }

    public class EmpleadoAgenteDto
    {
        public int Id { get; set; }
        public string? Legajo { get; set; }
        public string DNI { get; set; } = string.Empty;
        public string CUIL { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public int? DepartamentoId { get; set; }
        public int? SucursalId { get; set; }
        public bool TieneHuella { get; set; }
    }
}