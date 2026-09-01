using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models
{
    public class Sucursal
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        public int EmpresaId { get; set; }

        [ForeignKey("EmpresaId")]
        public virtual Empresa? Empresa { get; set; }

        [Required]
        [MaxLength(200)]
        public string SerialLector { get; set; } = string.Empty;

        public virtual ICollection<Departamento> Departamentos { get; set; } = new List<Departamento>();
    }
}
