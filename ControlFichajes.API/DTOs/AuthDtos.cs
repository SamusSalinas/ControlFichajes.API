using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs
{
    public class LoginRequestDto
    {
        [Required, EmailAddress, MaxLength(100)]
        public string Email {get; set;} = string.Empty;

        [Required, MinLength(8), MaxLength(255)]
        public string Password {get; set;} = string.Empty;
    }

    public class AuthResponseDto
    {
        public string Token {get; set;} = string.Empty;
        public string Mensaje {get; set;} = string.Empty;
    }

    public class UsuarioRegistroDto
    {
        public int? EmpresaId { get; set; }

        [Required, MaxLength(50)]
        public string NombreUsuario { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8), MaxLength(255)]
        public string Password { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Rol { get; set; } = "RRHH";
    }
}