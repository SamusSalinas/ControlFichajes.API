namespace ControlFichajes.API.DTOs;

public class RestablecerPasswordDto
{
    public string Token { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ConfirmarPassword { get; set; } = string.Empty;
}
