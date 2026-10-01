namespace ControlFichajes.API.Models;

public class Turno
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int ToleranciaMinutos { get; set; }
    public int EmpresaId { get; set; }
    public bool Activo { get; set; }
    public Empresa? Empresa { get; set; }
    public ICollection<TurnoDia> Dias { get; set; } = new List<TurnoDia>();
    public ICollection<EmpleadoTurno> Asignaciones { get; set; } = new List<EmpleadoTurno>();
}