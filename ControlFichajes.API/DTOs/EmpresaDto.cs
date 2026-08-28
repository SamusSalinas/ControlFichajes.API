namespace ControlFichajes.API.DTOs
{
    public class EmpresaDto
    {
        public int Id {get; set;}
        public string Nombre {get; set;}= string.Empty;
        public string Cuit {get; set;} = string.Empty;
        public string Direccion {get; set;} = string.Empty;
    }
}