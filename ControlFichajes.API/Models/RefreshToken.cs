using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.Models;

public class RefreshToken
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public Guid FamiliaId { get; set; }

    public DateTime ExpiraEn { get; set; }

    public DateTime? RevocadaEn { get; set; }

    public int? ReemplazadaPorId { get; set; }

    public DateTime CreadaEn { get; set; }

    [MaxLength(64)]
    public string? Ip { get; set; }

    [MaxLength(256)]
    public string? UserAgent { get; set; }

    public int TokenVersion { get; set; }

    public Guid? Generacion { get; set; }
}
