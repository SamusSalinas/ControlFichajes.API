using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models
{
    public class Usuario
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int EmpresaId { get; set; }
        [ForeignKey("EmpresaId")]
        public virtual Empresa? Empresa { get; set; }

        [Required]
        [MaxLength(50)]
        public string NombreUsuario { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        [EmailAddress]
        public string Correo { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Rol { get; set; } = "RRHH";

        [Required]
        public bool Activo { get; set; } = true;

        public bool RequiereCambioPassword { get; set; } = false;

        public int IntentosFallidos { get; set; } = 0;

        public DateTime? BloqueadoHasta { get; set; }

        public DateTime? UltimoIntentoFallido { get; set; }

        public DateTime? PasswordTemporalVenceEn { get; set; }

        public bool PasswordTemporalUsada { get; set; } = false;

        public int TokenVersion { get; set; } = 0;
    }
}