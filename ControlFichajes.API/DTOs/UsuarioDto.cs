namespace ControlFichajes.API.DTOs;

public class UsuarioDto
{
    public int Id { get; set; }
    public int? EmpresaId { get; set; }
    public string NombreUsuario { get; set; } = string.Empty;
    public string Correo { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Activo { get; set; }
}
