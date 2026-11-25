using System.Net;
using System.Net.Mail;
using ControlPlus.Application.Security.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ControlPlus.Infrastructure.Email;

/// <summary>
/// Correo saliente. Sección <c>Email</c> de la configuración (User Secrets o api-secrets.json del servicio); la
/// contraseña nunca va en el repositorio. Para Gmail: smtp.gmail.com, puerto 587 con STARTTLS y una «contraseña de
/// aplicación» de la cuenta (requiere verificación en dos pasos).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? SmtpHost { get; init; }
    public int SmtpPort { get; init; } = 587;
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public string? FromAddress { get; init; }
    public string FromName { get; init; } = "ControlPlus";
    public int TimeoutSeconds { get; init; } = 20;
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public bool IsConfigured
    {
        get
        {
            var value = options.Value;
            return !string.IsNullOrWhiteSpace(value.SmtpHost) && !string.IsNullOrWhiteSpace(value.FromAddress) &&
                   !string.IsNullOrWhiteSpace(value.UserName) && !string.IsNullOrEmpty(value.Password) &&
                   value.SmtpPort is > 0 and <= 65535;
        }
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsConfigured) throw new EmailDeliveryException("El correo saliente no está configurado.");
        var value = options.Value;
        try
        {
            using var client = new SmtpClient(value.SmtpHost!.Trim(), value.SmtpPort)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(value.UserName!.Trim(), value.Password),
                Timeout = Math.Clamp(value.TimeoutSeconds, 5, 120) * 1000
            };
            using var mail = SecurityEmailPresentation.Create(message, value);
            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException)
        {
            // Sin la dirección del destinatario ni credenciales en el registro técnico.
            logger.LogWarning("No se pudo enviar un correo de recuperación: {ErrorType}: {Error}",
                exception.GetType().Name, exception.Message);
            throw new EmailDeliveryException("No se pudo enviar el correo.", exception);
        }
    }
}
