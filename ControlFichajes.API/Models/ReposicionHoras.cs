using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models;

public class ReposicionHoras
{
    public int Id { get; set; }
    [Required]
    public int EmpleadoId { get; set; }
    [Column(TypeName = "date")]
    public DateTime Fecha { get; set; }
    [Range(1, 1440)]
    public int Minutos { get; set; }
    [Required, MaxLength(500)]
    public string Detalle { get; set; } = string.Empty;
    public int? CreadoPorUsuarioId { get; set; }
    [Required, MaxLength(50)]
    public string CreadoPorNombre { get; set; } = string.Empty;
    public DateTime CreadoEn { get; set; }
    public Empleado? Empleado { get; set; }
    public Usuario? CreadoPorUsuario { get; set; }
}
