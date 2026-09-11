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
        public bool RequiereCambioPassword { get; set; } = false;
    }

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

    public class UsuarioListItemDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public string NombreUsuario { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public bool RequiereCambioPassword { get; set; }
        public bool Bloqueado { get; set; }
        public DateTime? BloqueadoHasta { get; set; }
    }

    public class CambiarPasswordRequestDto
    {
        [Required]
        public string PasswordActual { get; set; } = string.Empty;

        [Required, MinLength(8), MaxLength(20)]
        public string NuevaPassword { get; set; } = string.Empty;

        [Required]
        public string ConfirmarPassword { get; set; } = string.Empty;
    }

    public class RestablecerPasswordResponseDto
    {
        public string Mensaje { get; set; } = string.Empty;
        public string PasswordTemporal { get; set; } = string.Empty;
        public DateTime VenceEn { get; set; }
    }

    public class CambiarEstadoUsuarioDto
    {
        public bool? Activo { get; set; }
    }

    public class CambiarRolUsuarioDto
    {
        [MaxLength(20)]
        public string Rol { get; set; } = string.Empty;
    }

    public class UsuarioActualizadoResponseDto
    {
        public string Mensaje { get; set; } = "Usuario actualizado correctamente.";
        public UsuarioListItemDto Usuario { get; set; } = new();
    }

    public class CambiarIdentidadUsuarioDto
    {
        [MaxLength(50)]
        public string NombreUsuario { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Correo { get; set; } = string.Empty;
    }
}