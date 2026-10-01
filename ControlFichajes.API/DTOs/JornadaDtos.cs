using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

public class TurnoAsignacionCrearDto
{
    [Range(1, int.MaxValue)]
    public int TurnoId { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
}

public class TurnoAsignacionDto
{
    public int Id { get; set; }
    public int TurnoId { get; set; }
    public string NombreTurno { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
}

public class JornadaExcepcionEscribirDto
{
    public bool EsDiaLibre { get; set; }
    [Range(0, 1440)]
    public int? ToleranciaMinutos { get; set; }
    public string? HoraEntrada { get; set; }
    public string? HoraSalida { get; set; }
}

public class JornadaDiaDto
{
    public DateTime Fecha { get; set; }
    public bool TieneTurnoAsignado { get; set; }
    public bool TieneHorarioConfigurado { get; set; }
    public bool EsDiaLibre { get; set; }
    public bool EsExcepcionManual { get; set; }
    public int ToleranciaMinutos { get; set; }
    public int MinutosAlmuerzo { get; set; }
    public string? HoraEntrada { get; set; }
    public string? HoraSalida { get; set; }
    public int MinutosProgramados { get; set; }
}

public class ReposicionHorasCrearDto
{
    public DateTime Fecha { get; set; }
    [Range(1, 1440)]
    public int Minutos { get; set; }
    [Required, MaxLength(500)]
    public string Detalle { get; set; } = string.Empty;
}

public class ReposicionHorasDto
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public int Minutos { get; set; }
    public string Detalle { get; set; } = string.Empty;
    public string CreadoPor { get; set; } = string.Empty;
    public DateTime CreadoEn { get; set; }
}

public class ResumenHorasDiaDto
{
    public DateTime Fecha { get; set; }
    public int MinutosProgramados { get; set; }
    public int MinutosTrabajados { get; set; }
    public int MinutosTarde { get; set; }
    public int MinutosSaldo { get; set; }
    public bool Incompleta { get; set; }
}

public class ResumenHorasDto
{
    public int EmpleadoId { get; set; }
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public int MinutosProgramados { get; set; }
    public int MinutosTrabajados { get; set; }
    public int MinutosRepuestos { get; set; }
    public int SaldoMinutos { get; set; }
    public List<ResumenHorasDiaDto> Dias { get; set; } = new();
}
