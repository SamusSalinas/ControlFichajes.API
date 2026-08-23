using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.Models
{
    public class Empresa
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string NombreFantasia { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string RazonSocial { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string CUIT { get; set; } = string.Empty;

        // Propiedades de navegación (Magia de Entity Framework)
        public virtual ICollection<Empleado> Empleados { get; set; } = new List<Empleado>();
        public virtual ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
    }
}