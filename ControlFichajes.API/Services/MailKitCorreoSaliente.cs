using ControlFichajes.API.Security;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ControlFichajes.API.Services;

public sealed class MailKitCorreoSaliente : ICorreoSaliente, ICorreoConConexion
{
    private readonly IConfiguracionCorreoActiva _activa;

    public MailKitCorreoSaliente(IConfiguracionCorreoActiva activa)
    {
        _activa = activa;
    }

    public Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
    {
        return EnviarAsync(_activa.Efectiva(), mensaje, cancellationToken);
    }

    public async Task EnviarAsync(ConexionSmtp conexion, CorreoSaliente mensaje, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conexion.Servidor) || string.IsNullOrWhiteSpace(conexion.Remitente))
            throw new InvalidOperationException("smtp_no_configurado");

        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(conexion.Remitente));
        mail.To.Add(MailboxAddress.Parse(mensaje.Destinatario));
        mail.Subject = mensaje.Asunto;
        mail.Body = new BodyBuilder
        {
            TextBody = mensaje.Texto,
            HtmlBody = mensaje.Html
        }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(conexion.Servidor, conexion.Puerto, Opciones(conexion.Cifrado), cancellationToken);
        if (!string.IsNullOrWhiteSpace(conexion.Usuario))
            await client.AuthenticateAsync(conexion.Usuario, conexion.Contrasena ?? string.Empty, cancellationToken);

        await client.SendAsync(mail, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static SecureSocketOptions Opciones(string cifrado)
    {
        return cifrado switch
        {
            ConexionSmtpReglas.Ninguno => SecureSocketOptions.None,
            ConexionSmtpReglas.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.SslOnConnect
        };
    }
}
