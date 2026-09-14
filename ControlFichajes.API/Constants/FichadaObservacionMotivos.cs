namespace ControlFichajes.API.Constants;

public static class FichadaObservacionMotivos
{
    public const int DetalleMaxLength = 500;
    public const int MotivoMaxLength = 40;
    public const int NombreMaxLength = 50;

    public static readonly string[] Valores =
    [
        "LlegadaTarde",
        "SalidaAnticipada",
        "OlvidoDeFichaje",
        "FichajeIncorrecto",
        "AusenciaJustificada",
        "HorarioExcepcional",
        "Otro"
    ];

    public static bool EsValido(string? motivo)
    {
        return Valores.Contains(motivo);
    }
}
