namespace ControlFichajes.API.DTOs
{
    public class EmpleadoPatchDto
    {
        public string? Legajo { get; set; }

        public string? DNI { get; set; }

        public string? CUIL { get; set; }

        public string? Nombre { get; set; }

        public string? Apellido { get; set; }

        public string? Departamento { get; set; }
        public int? DepartamentoId { get; set; }

        public string? Categoria { get; set; }

        public string? Sucursal { get; set; }
        public int? SucursalId { get; set; }

        public string? Horario { get; set; }
    }
}
