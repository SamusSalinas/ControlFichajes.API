using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models
{
    public class Empleado
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EmpresaId { get; set; }
        [ForeignKey("EmpresaId")]
        public virtual Empresa? Empresa { get; set; }

        [MaxLength(20)]
        public string? Legajo { get; set; }

        [Required]
        [MaxLength(15)]
        public string DNI { get; set; } = string.Empty;

        [Required]
        [MaxLength(15)]
        public string CUIL { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Apellido { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Departamento { get; set; }

        public int? DepartamentoId { get; set; }

        [ForeignKey(nameof(DepartamentoId))]
        public virtual Departamento? DepartamentoEntidad { get; set; }

        [MaxLength(50)]
        public string? Categoria { get; set; }

        [MaxLength(50)]
        public string? Sucursal { get; set; }

        public int? SucursalId { get; set; }

        [ForeignKey(nameof(SucursalId))]
        public virtual Sucursal? SucursalEntidad { get; set; }

        [MaxLength(50)]
        public string? Horario { get; set; }

        [Required]
        public bool Activo { get; set; } = true;

        // Propiedades de navegación
        public virtual ICollection<Huella> Huellas { get; set; } = new List<Huella>();
        public virtual ICollection<Fichada> Fichadas { get; set; } = new List<Fichada>();
    }
}