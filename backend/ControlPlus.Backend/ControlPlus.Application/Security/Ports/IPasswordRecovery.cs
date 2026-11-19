namespace ControlPlus.Application.Security.Ports;

/// <summary>Correo con alternativa de texto y, si incluye un código, presentación de seguridad de ControlPlus.</summary>
public sealed record EmailMessage(string To, string Subject, string Body, EmailVerificationCode? VerificationCode = null);

/// <summary>Contenido del código; el adaptador de correo se encarga del logo y la presentación HTML.</summary>
public sealed record EmailVerificationCode(
    string Heading, string RecipientName, string UserName, string Code, int ValidForMinutes, string Instructions);

/// <summary>Envío de correo saliente (SMTP). Solo se usa para la recuperación de contraseña.</summary>
public interface IEmailSender
{
    /// <summary>Falso si falta el servidor, la cuenta, su contraseña o el remitente.</summary>
    bool IsConfigured { get; }

    /// <exception cref="EmailDeliveryException">El servidor de correo no aceptó el mensaje o no hubo conexión.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class EmailDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Códigos de recuperación de 6 dígitos. No se guardan: se derivan del usuario, de su sello de seguridad y de la
/// ventana de tiempo, así que dejan de servir al cambiar la contraseña (el sello cambia) o al vencer.
/// </summary>
public interface IPasswordRecoveryCodes
{
    /// <summary>Tiempo mínimo durante el que un código recién enviado es válido.</summary>
    TimeSpan MinimumValidity { get; }

    string Create(Guid userId, string securityStamp, DateTimeOffset now);

    bool Verify(Guid userId, string securityStamp, string code, DateTimeOffset now);
}
