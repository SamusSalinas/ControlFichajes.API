namespace ControlFichajes.API.DTOs;

public class EstadoCorreoSalienteDto
{
    public string Servidor { get; set; } = string.Empty;

    public int Puerto { get; set; }

    public string Cifrado { get; set; } = string.Empty;

    public string Usuario { get; set; } = string.Empty;

    public string Remitente { get; set; } = string.Empty;

    public bool ContrasenaConfigurada { get; set; }

    public bool Guardada { get; set; }

    public bool Configurado { get; set; }
}

public class GuardarConfiguracionCorreoDto
{
    public string Servidor { get; set; } = string.Empty;

    public int Puerto { get; set; }

    public string Cifrado { get; set; } = string.Empty;

    public string Usuario { get; set; } = string.Empty;

    public string Remitente { get; set; } = string.Empty;

    public bool CambiarContrasena { get; set; }

    public string? Contrasena { get; set; }
}

public class ProbarCorreoSalienteDto
{
    public string Destinatario { get; set; } = string.Empty;

    public bool UsarBorrador { get; set; }

    public string? Servidor { get; set; }

    public int Puerto { get; set; }

    public string? Cifrado { get; set; }

    public string? Usuario { get; set; }

    public string? Remitente { get; set; }

    public bool CambiarContrasena { get; set; }

    public string? Contrasena { get; set; }
}

public class ProbarCorreoSalienteRespuestaDto
{
    public string Mensaje { get; set; } = string.Empty;
}

public class GuardarConfiguracionCorreoRespuestaDto
{
    public string Mensaje { get; set; } = string.Empty;

    public EstadoCorreoSalienteDto Configuracion { get; set; } = new();
}
