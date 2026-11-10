using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs;

public class EstadoMfaDto
{
    public bool TotpHabilitado { get; set; }

    public bool MfaObligatorio { get; set; }

    public bool ConfiguracionDisponible { get; set; }
}

public class IniciarEnrolamientoMfaDto
{
    [Required]
    [MaxLength(255)]
    public string Password { get; set; } = string.Empty;
}

public class ConfirmarEnrolamientoMfaDto
{
    [Required]
    [MaxLength(128)]
    public string Operacion { get; set; } = string.Empty;

    [Required]
    [MaxLength(12)]
    public string Codigo { get; set; } = string.Empty;
}

public class IniciarVinculacionMfaDto
{
    [Required]
    [MaxLength(128)]
    public string Desafio { get; set; } = string.Empty;
}

public class ConfirmarVinculacionMfaDto
{
    [Required]
    [MaxLength(128)]
    public string Desafio { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Operacion { get; set; } = string.Empty;

    [Required]
    [MaxLength(12)]
    public string Codigo { get; set; } = string.Empty;
}

public class DesactivarMfaDto
{
    [Required]
    [MaxLength(255)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(12)]
    public string? Codigo { get; set; }

    [MaxLength(40)]
    public string? CodigoRecuperacion { get; set; }
}

public class RegenerarCodigosMfaDto
{
    [Required]
    [MaxLength(255)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(12)]
    public string? Codigo { get; set; }

    [MaxLength(40)]
    public string? CodigoRecuperacion { get; set; }
}
