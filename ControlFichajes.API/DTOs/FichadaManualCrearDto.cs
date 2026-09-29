using System;
using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

public class FichadaManualCrearDto
{
    [Required]
    public int EmpleadoId { get; set; }

    [Required]
    public DateTime FechaHora { get; set; }

    [Required, MaxLength(20)]
    public string Tipo { get; set; } = string.Empty; // "Entrada" o "Salida"

    [Required(ErrorMessage = "El motivo es obligatorio.")]
    [MaxLength(100)]
    public string Motivo { get; set; } = string.Empty;

    [Required(ErrorMessage = "El detalle es obligatorio.")]
    [MaxLength(500)]
    public string Detalle { get; set; } = string.Empty;
}