using System.ComponentModel.DataAnnotations;

namespace ControlFichajes.API.DTOs
{
    public class DepartamentoDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int SucursalId { get; set; }
    }
    public class DepartamentoCrearDto
    {
        [Required, MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        public int SucursalId { get; set; }
    }


}