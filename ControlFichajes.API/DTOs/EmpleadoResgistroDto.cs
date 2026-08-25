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
        public string? Categoria { get; set; }
        public string? Sucursal { get; set; }
        public string? Horario { get; set; }
    }
}