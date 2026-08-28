using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs
{
    public class HuellaEnrolarDto
    {
        public int EmpleadoId { get; set; }

        // La huella suele enviarse convertida a texto (Base64) por la red
        public string TemplateHuellaBase64 { get; set; } = string.Empty;

        // Opcional: Para saber qué dedo es (ej. 1 = Pulgar derecho)
        /*
        1 = Pulgar derecho
        2 = Indice derecho
        3 = Medio derecho
        4 = Anular derecho
        5 = Meñique derecho
        6 = Pulgar izquierdo
        7 = Indice izquierdo
        8 = Medio izquierdo
        9 = Anular izquierdo
        0 = Meñique izquierdo
        */
        public int IndiceDedo { get; set; }
    }
}