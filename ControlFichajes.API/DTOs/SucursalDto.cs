namespace ControlFichajes.API.DTOs
{
    public class SucursalDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public int EmpresaId { get; set; }
        public string SerialLector { get; set; } = string.Empty;
    }
}
