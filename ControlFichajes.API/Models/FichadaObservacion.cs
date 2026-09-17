using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models;

public class FichadaObservacion
{
    [Key]
    public int FichadaId { get; set; }

    [ForeignKey(nameof(FichadaId))]
    public virtual Fichada Fichada { get; set; } = null!;

    [Required]
    [MaxLength(40)]
    public string Motivo { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Detalle { get; set; } = string.Empty;

    public int? CreadoPorUsuarioId { get; set; }

    [ForeignKey(nameof(CreadoPorUsuarioId))]
    public virtual Usuario? CreadoPorUsuario { get; set; }

    [Required]
    [MaxLength(50)]
    public string CreadoPorNombre { get; set; } = string.Empty;

    [Required]
    public DateTime CreadoEn { get; set; }

    public int? ModificadoPorUsuarioId { get; set; }

    [ForeignKey(nameof(ModificadoPorUsuarioId))]
    public virtual Usuario? ModificadoPorUsuario { get; set; }

    [MaxLength(50)]
    public string? ModificadoPorNombre { get; set; }

    public DateTime? ModificadoEn { get; set; }
}
