using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs
{
    public class HuellaEnrolarDto
    {
        public int EmpleadoId { get; set; }

        // La huella suele enviarse convertida a texto (Base64) por la red
        public string TemplateHuellaBase64 { get; set; } = string.Empty;

        // Opcional: Para saber qué dedo es (ej. 1 = Pulgar derecho)
        public int IndiceDedo { get; set; }
    }
}