using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.Models;

public class CodigoRecuperacionMfa
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    [MaxLength(255)]
    public string CodigoHash { get; set; } = string.Empty;

    public DateTime? UsadoEn { get; set; }
}
