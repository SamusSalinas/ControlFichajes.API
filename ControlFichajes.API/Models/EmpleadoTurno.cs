using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models;

public class EmpleadoTurno
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int EmpleadoId { get; set; }

    [Required]
    public int TurnoId { get; set; }

    [Required]
    public DateTime FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }

    // Propiedad de navegación para relacionarlo con el Turno en Entity Framework
    public Turno? Turno { get; set; }
}