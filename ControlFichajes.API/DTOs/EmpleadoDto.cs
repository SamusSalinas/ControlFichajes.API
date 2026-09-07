namespace ControlFichajes.API.DTOs;

public class EmpleadoDto
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public string? Legajo { get; set; }
    public string DNI { get; set; } = string.Empty;
    public string CUIL { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public int? DepartamentoId { get; set; }
    public string? Departamento { get; set; }
    public string? Categoria { get; set; }
    public int? SucursalId { get; set; }
    public string? Sucursal { get; set; }
    public string? Horario { get; set; }
    public bool Activo { get; set; }
    public bool TieneHuella { get; set; }
}
