using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

public class InvitarUsuarioDto
{
    [Required]
    public int EmpresaId { get; set; }

    [Required, MaxLength(50)]
    public string NombreUsuario { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Rol { get; set; } = "RRHH";
}

public class InvitacionCreadaDto
{
    public int Id { get; set; }

    public string NombreUsuario { get; set; } = string.Empty;

    public string Correo { get; set; } = string.Empty;

    public string Rol { get; set; } = string.Empty;

    public bool Activo { get; set; }

    public string Mensaje { get; set; } = string.Empty;
}
