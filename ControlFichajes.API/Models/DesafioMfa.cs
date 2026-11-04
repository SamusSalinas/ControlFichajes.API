using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.Models;

public class DesafioMfa
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiraEn { get; set; }

    public DateTime? ConsumidoEn { get; set; }

    [MaxLength(20)]
    public string Proposito { get; set; } = string.Empty;
}
