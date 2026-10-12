using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs
{
    public class FichadaRequestDto
    {
        [Range(1, int.MaxValue)]
        public int EmpleadoId { get; set; }

        [Required]
        public DateTime FechaHora { get; set; }
    }
}