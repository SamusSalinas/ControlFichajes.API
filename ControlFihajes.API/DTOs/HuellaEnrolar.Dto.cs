using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs
{
    public class HuellaEnrolarDto
    {
        [Required]
        public int EmpleadoId { get; set; }

        [Required]
        public string NombreDedo { get; set; } = string.Empty; // Ej: "Indice Derecho"

        [Required]
        public string TemplateBiometrico { get; set; } = string.Empty; // El string gigante en Base64
    }
}