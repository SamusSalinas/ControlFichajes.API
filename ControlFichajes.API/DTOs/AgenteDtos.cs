using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

public sealed class AgenteLoginDto
{
    [Required, MaxLength(100)]
    public string ClientId { get; set; } = string.Empty;

    [Required, MinLength(16), MaxLength(255)]
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class AgenteCrearDto
{
    [Required]
    public int SucursalId { get; set; }

    [Required, MaxLength(100)]
    public string ClientId { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;
}

public sealed class AgenteCreadoDto
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int SucursalId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class AgenteDto
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int SucursalId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public DateTime? UltimoAcceso { get; set; }
}

public sealed class AgenteHeartbeatDto
{
    [MaxLength(50)]
    public string? VersionApp { get; set; }

    [MaxLength(100)]
    public string? SerialLector { get; set; }

    [MaxLength(50)]
    public string? EstadoLector { get; set; }

    public DateTime? UltimaSincronizacion { get; set; }
}