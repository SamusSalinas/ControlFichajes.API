using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models
{
    public class Departamento
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        public int SucursalId { get; set; }

        [ForeignKey("SucursalId")]
        public virtual Sucursal? Sucursal { get; set; }
    }
}
