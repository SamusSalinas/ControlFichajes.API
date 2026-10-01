using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models;

public class EmpleadoJornadaExcepcion
{
    public int Id { get; set; }
    [Required]
    public int EmpleadoId { get; set; }
    [Column(TypeName = "date")]
    public DateTime Fecha { get; set; }
    public bool EsDiaLibre { get; set; }
    public int? ToleranciaMinutos { get; set; }
    public TimeSpan? HoraEntrada { get; set; }
    public TimeSpan? HoraSalida { get; set; }
    public Empleado? Empleado { get; set; }
}
