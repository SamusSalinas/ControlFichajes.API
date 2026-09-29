namespace ControlFichajes.API.Models;

public class TurnoDia
{
    public int Id { get; set; }
    public int TurnoId { get; set; }
    public int DiaSemana { get; set; } 
    public TimeSpan HoraEntrada { get; set; }
    public TimeSpan HoraSalida { get; set; }
    public int MinutosAlmuerzo { get; set; } // Fijo en 60 según requerimiento
    
    public Turno? Turno { get; set; }
}