using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

public class UsuarioRegistroDto
{
    [Required]
    public int EmpresaId { get; set; }

    [Required, MaxLength(50)]
    public string NombreUsuario { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(255)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Rol { get; set; } = "RRHH";
}