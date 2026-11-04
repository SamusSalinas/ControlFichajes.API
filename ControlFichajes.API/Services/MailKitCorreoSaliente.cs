using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ControlFichajes.API.Services;

public sealed class MailKitCorreoSaliente : ICorreoSaliente
{
    private readonly IConfiguration _configuration;

    public MailKitCorreoSaliente(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task EnviarAsync(CorreoSaliente mensaje, CancellationToken cancellationToken = default)
    {
        var host = _configuration["Smtp:Host"]?.Trim();
        var from = _configuration["Smtp:From"]?.Trim();
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("smtp_no_configurado");

        var port = int.TryParse(_configuration["Smtp:Port"], out var puerto) ? puerto : 587;
        var ssl = !bool.TryParse(_configuration["Smtp:EnableSsl"], out var habilitar) || habilitar;
        var user = _configuration["Smtp:User"]?.Trim();
        var password = _configuration["Smtp:Password"];

        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(from));
        mail.To.Add(MailboxAddress.Parse(mensaje.Destinatario));
        mail.Subject = mensaje.Asunto;
        mail.Body = new BodyBuilder
        {
            TextBody = mensaje.Texto,
            HtmlBody = mensaje.Html
        }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, Opciones(ssl, port), cancellationToken);
        if (!string.IsNullOrWhiteSpace(user))
            await client.AuthenticateAsync(user, password ?? string.Empty, cancellationToken);

        await client.SendAsync(mail, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static SecureSocketOptions Opciones(bool ssl, int port)
    {
        if (!ssl)
            return SecureSocketOptions.None;

        return port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
    }
}
