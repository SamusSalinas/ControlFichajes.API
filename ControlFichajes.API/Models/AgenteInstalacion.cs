using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ControlFichajes.API.Models;

[Table("AgenteInstalaciones")]
public class AgenteInstalacion
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string ClientId { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    [Column("ClientSecretHash")]
    public string SecretHash { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    public int SucursalId { get; set; }

    [ForeignKey(nameof(SucursalId))]
    public Sucursal? Sucursal { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime? UltimoAcceso { get; set; }
}