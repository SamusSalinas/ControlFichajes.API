using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models
{
    public class Huella
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EmpleadoId { get; set; }
        [ForeignKey("EmpleadoId")]
        public virtual Empleado? Empleado { get; set; }

        [Required]
        [MaxLength(30)]
        public string NombreDedo { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "TEXT")]
        public string TemplateBiometrico { get; set; } = string.Empty;
    }
}