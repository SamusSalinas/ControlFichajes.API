using OtpNet;

namespace ControlFichajes.API.Security;

public readonly record struct ResultadoTotp(bool Coincide, long Paso);

public static class ValidadorTotp
{
    public const int PasoSegundos = 30;
    public const int Digitos = 6;
    public const int VentanaAnterior = 1;
    public const int VentanaFutura = 0;

    public static ResultadoTotp Verificar(byte[] secreto, string codigo, DateTime ahora)
    {
        var totp = new Totp(secreto, step: PasoSegundos, mode: OtpHashMode.Sha1, totpSize: Digitos);
        var ventana = new VerificationWindow(previous: VentanaAnterior, future: VentanaFutura);
        var coincide = totp.VerifyTotp(ahora, codigo, out var paso, ventana);
        return new ResultadoTotp(coincide, paso);
    }

    public static string CodigoEn(byte[] secreto, DateTime ahora)
    {
        var totp = new Totp(secreto, step: PasoSegundos, mode: OtpHashMode.Sha1, totpSize: Digitos);
        return totp.ComputeTotp(ahora);
    }
}
