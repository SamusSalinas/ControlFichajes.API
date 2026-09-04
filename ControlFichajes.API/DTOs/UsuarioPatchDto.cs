using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

public class UsuarioPatchDto
{
    [MaxLength(50)]
    public string? NombreUsuario { get; set; }

    [MaxLength(20)]
    public string? Rol { get; set; }

    public bool? Activo { get; set; }
}
