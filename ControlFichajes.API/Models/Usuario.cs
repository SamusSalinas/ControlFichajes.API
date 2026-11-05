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

        public DateTime? CorreoVerificadoEn { get; set; }

        public DateTime? InvitadoEn { get; set; }

        public DateTime? InvitacionAceptadaEn { get; set; }

        public bool TotpHabilitado { get; set; }

        [MaxLength(512)]
        public string? TotpSecretoCifrado { get; set; }

        public DateTime? MfaOmitidoHasta { get; set; }

        public long? UltimoPasoTotp { get; set; }

        public bool MfaObligatorio { get; set; }

        [MaxLength(512)]
        public string? TotpPendienteCifrado { get; set; }

        public DateTime? TotpPendienteExpiraEn { get; set; }

        public int MfaIntentosFallidos { get; set; }

        public DateTime? MfaBloqueadoHasta { get; set; }
    }
}