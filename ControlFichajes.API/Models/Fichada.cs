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

        public virtual FichadaObservacion? Observacion { get; set; }
        public bool EsManual { get; set; } = false;
    
        [MaxLength(20)]
        public string Estado { get; set; } = "Completada";

        public int? MinutosHastaCorte { get; set; }

        /// <summary>
        /// Clave única de un evento biométrico. Las fichadas manuales y las
        /// filas anteriores quedan en null: MySQL admite varias filas null
        /// en un índice UNIQUE.
        /// </summary>
        [MaxLength(40)]
        public string? ClaveIdempotencia { get; set; }
    }
}