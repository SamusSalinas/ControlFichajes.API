using System.Globalization;
using System.Net;
using ControlFichajes.API.Models;

namespace ControlFichajes.API.Services;

internal static class PlantillaCorreoInvitacion
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-AR");

    public static CorreoSaliente Crear(string destinatario, string empresa, string enlace, DateTime? vence)
    {
        var nombre = string.IsNullOrWhiteSpace(empresa) ? "tu organización" : empresa.Trim();
        var plazo = Plazo(vence);
        return new CorreoSaliente
        {
            Destinatario = destinatario,
            Asunto = "Invitación a Control de Accesos",
            Texto = Texto(nombre, enlace, plazo),
            Html = Html(nombre, enlace, plazo)
        };
    }

    public static string NombreEmpresa(Empresa? empresa)
    {
        var fantasia = empresa?.NombreFantasia?.Trim();
        if (!string.IsNullOrEmpty(fantasia))
            return fantasia;

        var razon = empresa?.RazonSocial?.Trim();
        return string.IsNullOrEmpty(razon) ? "tu organización" : razon;
    }

    private static string? Plazo(DateTime? vence)
    {
        if (vence == null || vence.Value == default)
            return null;

        var utc = vence.Value.Kind == DateTimeKind.Local
            ? vence.Value.ToUniversalTime()
            : DateTime.SpecifyKind(vence.Value, DateTimeKind.Utc);
        return utc.ToString("d 'de' MMMM 'de' yyyy 'a las' HH:mm 'UTC'", Cultura);
    }

    private static string Texto(string empresa, string enlace, string? plazo)
    {
        var vencimiento = plazo == null
            ? null
            : "La invitación vence el " + plazo + ".";
        return string.Join(Environment.NewLine, new[]
        {
            "Control de Accesos",
            "",
            empresa + " te invitó a crear tu acceso.",
            "Elegí una contraseña para activar la cuenta.",
            vencimiento,
            "",
            "Aceptar invitación:",
            enlace,
            "",
            "Si el botón no funciona, abrí este enlace:",
            enlace,
            "",
            "Recibiste este mensaje porque " + empresa + " creó un acceso para este correo. Si no lo esperabas, podés ignorarlo."
        }.Where(linea => linea != null));
    }

    private static string Html(string empresa, string enlace, string? plazo)
    {
        var nombre = WebUtility.HtmlEncode(empresa);
        var url = WebUtility.HtmlEncode(enlace);
        var vencimiento = plazo == null
            ? ""
            : "<p style=\"margin:12px 0 0;font-size:15px;line-height:1.5;color:#334155;\">La invitación vence el " + WebUtility.HtmlEncode(plazo) + ".</p>";
        return "<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
            "<title>Control de Accesos</title></head>" +
            "<body style=\"margin:0;padding:0;background:#f1f5f9;\">" +
            "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f1f5f9;\">" +
            "<tr><td align=\"center\" style=\"padding:24px 12px;\">" +
            "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;width:100%;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;\">" +
            "<tr><td style=\"padding:28px 24px 0;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#0f172a;\">" +
            "<p style=\"margin:0;font-size:13px;font-weight:600;letter-spacing:.04em;text-transform:uppercase;color:#64748b;\">Control de Accesos</p>" +
            "<h1 style=\"margin:12px 0 0;font-size:22px;line-height:1.3;\">" + nombre + " te invitó a crear tu acceso</h1>" +
            "<p style=\"margin:12px 0 0;font-size:15px;line-height:1.5;color:#334155;\">Elegí una contraseña para activar la cuenta.</p>" +
            vencimiento +
            "</td></tr>" +
            "<tr><td style=\"padding:24px 24px 8px;\">" +
            "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\"><tr>" +
            "<td bgcolor=\"#2563eb\" style=\"border-radius:8px;\">" +
            "<a href=\"" + url + "\" style=\"display:inline-block;padding:12px 20px;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:15px;font-weight:600;line-height:1.2;color:#ffffff;text-decoration:none;\">Aceptar invitación</a>" +
            "</td></tr></table></td></tr>" +
            "<tr><td style=\"padding:8px 24px 24px;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:13px;line-height:1.5;color:#64748b;\">" +
            "Si el botón no funciona, abrí este enlace:<br>" +
            "<a href=\"" + url + "\" style=\"color:#2563eb;text-decoration:underline;word-break:break-all;overflow-wrap:anywhere;\">" + url + "</a>" +
            "</td></tr>" +
            "<tr><td style=\"padding:14px 24px 20px;border-top:1px solid #e2e8f0;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;font-size:12px;line-height:1.5;color:#94a3b8;\">" +
            "Recibiste este mensaje porque " + nombre + " creó un acceso para este correo. Si no lo esperabas, podés ignorarlo." +
            "</td></tr></table></td></tr></table></body></html>";
    }
}
