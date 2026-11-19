using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Infrastructure.Email;

/// <summary>Presentación de correo autónoma: texto alternativo y logo incluido, sin descargas externas.</summary>
public static class SecurityEmailPresentation
{
    public const string LogoContentId = "controlplus-security-logo";

    public static MailMessage Create(EmailMessage message, EmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);
        var mail = new MailMessage(
            new MailAddress(options.FromAddress!.Trim(), options.FromName, Encoding.UTF8),
            new MailAddress(message.To))
        {
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        try
        {
            if (message.VerificationCode is not { } content)
            {
                mail.Body = message.Body;
                mail.IsBodyHtml = false;
                return mail;
            }

            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.Body, Encoding.UTF8, MediaTypeNames.Text.Plain));
            var html = AlternateView.CreateAlternateViewFromString(Render(content), Encoding.UTF8, MediaTypeNames.Text.Html);
            mail.AlternateViews.Add(html);
            var logo = typeof(SecurityEmailPresentation).Assembly.GetManifestResourceStream("ControlPlus.Email.LogoWhite.png")
                ?? throw new InvalidOperationException("No se encontró el logo del correo de ControlPlus.");
            var resource = new LinkedResource(logo, MediaTypeNames.Image.Png)
            {
                ContentId = LogoContentId,
                TransferEncoding = TransferEncoding.Base64
            };
            html.LinkedResources.Add(resource);
            return mail;
        }
        catch
        {
            mail.Dispose();
            throw;
        }
    }

    public static string Render(EmailVerificationCode content)
    {
        ArgumentNullException.ThrowIfNull(content);
        static string Escape(string value) => WebUtility.HtmlEncode(value);
        return $$"""
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"><title>{{Escape(content.Heading)}} | ControlPlus</title></head>
            <body style="margin:0;padding:0;background-color:#F2F4F7;color:#243142;font-family:'Segoe UI',Arial,sans-serif;">
            <div style="display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">Tu código de un solo uso para {{Escape(content.Heading.ToLowerInvariant())}} en ControlPlus.</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#F2F4F7;"><tr><td align="center" style="padding:32px 12px;">
              <table role="presentation" width="560" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:560px;background-color:#FFFFFF;border:1px solid #DCE3EA;border-radius:16px;overflow:hidden;">
                <tr><td bgcolor="#0F2C4D" style="padding:28px 32px;background-color:#0F2C4D;border-bottom:4px solid #FD6D32;">
                  <img src="cid:{{LogoContentId}}" width="230" height="44" alt="ControlPlus" style="display:block;width:230px;max-width:100%;height:auto;border:0;">
                  <p style="margin:15px 0 0;color:#D7E0EA;font-size:12px;font-weight:600;letter-spacing:1.5px;">SEGURIDAD DE TU CUENTA</p>
                </td></tr>
                <tr><td style="padding:32px 28px 28px;">
                  <h1 style="margin:0 0 22px;color:#0F2C4D;font-size:28px;line-height:1.25;font-weight:700;">{{Escape(content.Heading)}}</h1>
                  <p style="margin:0 0 12px;font-size:16px;line-height:1.6;">Hola, <strong>{{Escape(content.RecipientName)}}</strong>:</p>
                  <p style="margin:0 0 24px;font-size:15px;line-height:1.7;color:#526172;">Recibimos una solicitud para tu cuenta <strong style="color:#0F2C4D;">{{Escape(content.UserName)}}</strong>. {{Escape(content.Instructions)}}</p>
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="#F3F6FA" style="padding:24px 8px;background-color:#F3F6FA;border:1px solid #D5DFEA;border-radius:12px;">
                    <p style="margin:0 0 12px;color:#526172;font-size:12px;line-height:1.4;font-weight:700;letter-spacing:1px;">CÓDIGO DE VERIFICACIÓN</p>
                    <p dir="ltr" style="margin:0;color:#0F2C4D;font-family:'Segoe UI',Arial,sans-serif;font-size:40px;line-height:1.35;font-weight:700;letter-spacing:8px;font-variant-numeric:tabular-nums;white-space:nowrap;">{{Escape(content.Code)}}</p>
                    <p style="margin:14px 0 0;color:#A13F16;font-size:13px;line-height:1.5;font-weight:600;">Válido por {{content.ValidForMinutes}} minutos · Un solo uso</p>
                  </td></tr></table>
                  <p style="margin:24px 0 12px;font-size:14px;line-height:1.7;color:#526172;">Si no solicitaste este cambio, ignora este correo. Tu contraseña se conserva.</p>
                  <p style="margin:0;font-size:13px;line-height:1.7;color:#526172;"><strong style="color:#0F2C4D;">No compartas este código.</strong> ControlPlus nunca te pedirá tu contraseña por correo.</p>
                </td></tr>
                <tr><td style="padding:20px 28px;border-top:1px solid #E5EAF0;background-color:#FAFBFD;"><p style="margin:0;color:#607285;font-size:12px;line-height:1.7;">Enviado por ControlPlus · Punto de venta<br>Este es un mensaje automático de seguridad.</p></td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;
    }
}
