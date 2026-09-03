using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models;

public class AgenteInstalacion
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int EmpresaId { get; set; }

    [ForeignKey(nameof(EmpresaId))]
    public Empresa? Empresa { get; set; }

    [Required]
    public int SucursalId { get; set; }

    [ForeignKey(nameof(SucursalId))]
    public Sucursal? Sucursal { get; set; }

    [Required, MaxLength(100)]
    public string ClientId { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string SecretHash { get; set; } = string.Empty;

    public bool Activo { get; set; } = true;

    public DateTime? UltimoHeartbeat { get; set; }
}
