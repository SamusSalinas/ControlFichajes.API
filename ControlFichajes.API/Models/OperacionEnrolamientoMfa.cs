using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.Models;

public class OperacionEnrolamientoMfa
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public int TokenVersion { get; set; }

    public Guid? SesionId { get; set; }

    public DateTime CreadaEn { get; set; }

    public DateTime ExpiraEn { get; set; }

    public DateTime? InvalidadaEn { get; set; }

    public DateTime? ConsumidaEn { get; set; }

    public int Intentos { get; set; }
}
