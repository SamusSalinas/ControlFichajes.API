using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

public class VerificarMfaDto
{
    [Required]
    [MaxLength(128)]
    public string Desafio { get; set; } = string.Empty;

    [MaxLength(12)]
    public string? Codigo { get; set; }

    [MaxLength(40)]
    public string? CodigoRecuperacion { get; set; }
}
