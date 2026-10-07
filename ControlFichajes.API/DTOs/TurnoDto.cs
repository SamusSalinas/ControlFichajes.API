using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

// DTO para lectura (GET)
public class TurnoDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int ToleranciaMinutos { get; set; }
    public List<TurnoDiaDto> Dias { get; set; } = new();
}

public class TurnoDiaDto
{
    public int DiaSemana { get; set; }
    public string HoraEntrada { get; set; } = string.Empty; // Formato "HH:mm"
    public string HoraSalida { get; set; } = string.Empty;
    public int MinutosAlmuerzo { get; set; } = 60;
}

// DTO para creación (POST/PUT)
public class TurnoCrearDto
{
    [Required, MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;
    
    [Required]
    public int ToleranciaMinutos { get; set; }
    
    [Required]
    public List<TurnoDiaDto> Dias { get; set; } = new();
}