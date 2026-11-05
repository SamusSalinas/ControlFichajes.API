using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

public class VerificarMfaDto
{
    [Required]
    [MaxLength(128)]
    public string Desafio { get; set; } = string.Empty;

    [Required]
    [MaxLength(12)]
    public string Codigo { get; set; } = string.Empty;
}
