using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.Models;

public class AuditoriaSeguridad
{
    public long Id { get; set; }

    public DateTime FechaUtc { get; set; }

    public int? UsuarioId { get; set; }

    [MaxLength(100)]
    public string? Correo { get; set; }

    [MaxLength(60)]
    public string Evento { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Resultado { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? Ip { get; set; }

    [MaxLength(256)]
    public string? UserAgent { get; set; }

    [MaxLength(300)]
    public string? Detalle { get; set; }
}
