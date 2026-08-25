using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs
{
    public class FichadaRequestDto
    {
        [Required]
        public int EmpleadoId { get; set; }

        [Required]
        public DateTime FechaHora { get; set; }
        
        // No enviamos "TipoRegistro" (Entrada/Salida) desde la app.
        // La API será lo suficientemente inteligente para calcularlo.
    }
}