using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models
{
    public class Fichada
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EmpleadoId { get; set; }
        [ForeignKey("EmpleadoId")]
        public virtual Empleado? Empleado { get; set; }

        [Required]
        public DateTime FechaHora { get; set; }

        [Required]
        [MaxLength(10)]
        public string TipoRegistro { get; set; } = string.Empty; 

        [Required]
        [MaxLength(20)]
        public string Metodo { get; set; } = "Biometrico";
    }
}