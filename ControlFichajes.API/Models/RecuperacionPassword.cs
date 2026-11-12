using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.Models;

public class RecuperacionPassword
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiraEn { get; set; }

    public DateTime? UsadaEn { get; set; }

    public DateTime CreadaEn { get; set; }
}
