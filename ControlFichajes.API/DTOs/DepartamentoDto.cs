namespace ControlFichajes.API.DTOs
{
    public class DepartamentoDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int SucursalId { get; set; }
    }
}
