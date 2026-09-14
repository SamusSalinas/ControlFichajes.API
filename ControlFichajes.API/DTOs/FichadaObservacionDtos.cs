using System.ComponentModel.DataAnnotations;
using ControlFichajes.API.Constants;

namespace ControlFichajes.API.DTOs;

public sealed class FichadaObservacionWriteDto
{
    [Required]
    [MaxLength(FichadaObservacionMotivos.MotivoMaxLength)]
    public string Motivo { get; set; } = string.Empty;

    [Required]
    [MaxLength(FichadaObservacionMotivos.DetalleMaxLength)]
    public string Detalle { get; set; } = string.Empty;
}

public sealed class FichadaObservacionDto
{
    public int FichadaId { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
    public string CreadoPor { get; set; } = string.Empty;
    public DateTime CreadoEn { get; set; }
    public string? ModificadoPor { get; set; }
    public DateTime? ModificadoEn { get; set; }
}
