using System.Text.RegularExpressions;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security.Enums;
using User = ControlPlus.Domain.OfficialModel.Usuario;

namespace ControlPlus.Application.Security.Services;

/// <summary>
/// Recuperación de contraseña por correo (decisión del 18 de noviembre de 2026).
/// <list type="bullet">
/// <item>Pedir el código responde siempre lo mismo; solo se envía si el usuario existe, está activo, no está bloqueado y
/// tiene correo.</item>
/// <item>Un código equivocado cuenta como intento fallido de inicio de sesión: al quinto el usuario se bloquea y solo el
/// Administrador lo reactiva, como hasta ahora.</item>
/// <item>El código correcto cambia la contraseña, reinicia los intentos y cierra todas las sesiones (cambia el sello).</item>
/// </list>
/// </summary>
public sealed partial class PasswordRecoveryService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IAuditRepository auditRepository,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILoginAttemptCoordinator loginAttemptCoordinator,
    IEmailSender emailSender,
    IPasswordRecoveryCodes codes) : IPasswordRecoveryService
{
    private static readonly ApplicationError NotConfigured = new("email.not_configured",
        "La recuperación por correo no está configurada. Pida al Administrador que restablezca su contraseña.");
    private static readonly ApplicationError DeliveryFailed = new("email.delivery_failed",
        "No se pudo enviar el correo. Revise la conexión a Internet o pida al Administrador que restablezca su contraseña.");
    private static readonly ApplicationError InvalidCode = ApplicationError.Validation(
        "El código no es válido o ya venció. Pida uno nuevo.");

    public async Task<Result<PasswordRecoveryRequestedDto>> RequestAsync(
        PasswordRecoveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!emailSender.IsConfigured) return Result.Failure<PasswordRecoveryRequestedDto>(NotConfigured);
        var userName = SecurityInput.RequiredText(request.UserName, "usuario");
        if (userName.IsFailure) return Result.Failure<PasswordRecoveryRequestedDto>(userName.Error!);

        var minutes = (int)codes.MinimumValidity.TotalMinutes;
        var response = new PasswordRecoveryRequestedDto(
            $"Si el usuario tiene un correo registrado, le enviamos un código. Revise su correo: el código vale {minutes} minutos.");
        var user = await users.GetByUserNameAsync(userName.Value!, cancellationToken);
        if (user is null || !user.IsActive || user.Email is null) return Result.Success(response);

        var now = clock.UtcNow;
        var code = codes.Create(user.Id, user.SecurityStamp, now);
        try
        {
            await emailSender.SendAsync(new EmailMessage(user.Email,
                "Código para cambiar su contraseña de ControlPlus",
                $"""
                Hola, {user.DisplayName}:

                Su código para cambiar la contraseña de ControlPlus (usuario «{user.UserName}») es:

                    {code}

                Escríbalo en la pantalla de ingreso, en «¿Olvidaste tu contraseña?». Vale {minutes} minutos y sirve una sola vez.

                Si usted no lo pidió, ignore este correo: su contraseña no cambia.
                """,
                new EmailVerificationCode("Cambia tu contraseña", user.DisplayName, user.UserName, code, minutes,
                    "Vuelve a la pantalla de ingreso y escribe este código en «¿Olvidaste tu contraseña?» para elegir tu nueva contraseña.")),
                cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            return Result.Failure<PasswordRecoveryRequestedDto>(DeliveryFailed);
        }

        await AuditWriter.WriteAsync(auditRepository, clock, user.Id, AuditAction.PasswordRecoveryRequested,
            nameof(User), user.Id, new { user.UserName }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(response);
    }

    public async Task<Result> ConfirmAsync(PasswordRecoveryConfirmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userName = SecurityInput.RequiredText(request.UserName, "usuario");
        if (userName.IsFailure) return Result.Failure(userName.Error!);
        var code = request.Code?.Trim() ?? string.Empty;
        if (!SixDigits().IsMatch(code)) return Result.Failure(ApplicationError.Validation("El código tiene 6 números."));
        var password = SecurityInput.RequiredPassword(request.NewPassword, "nueva contraseña");
        if (password.IsFailure) return Result.Failure(password.Error!);

        return await loginAttemptCoordinator.ExecuteAsync(
            userName.Value!.ToUpperInvariant(),
            attemptCancellationToken => ConfirmWithinAttemptLockAsync(userName.Value!, code, password.Value!, attemptCancellationToken),
            cancellationToken);
    }

    private async Task<Result> ConfirmWithinAttemptLockAsync(
        string userName, string code, string newPassword, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var user = await users.GetByUserNameAsync(userName, cancellationToken);
        if (user is null || !user.IsActive || user.Email is null) return Result.Failure(InvalidCode);

        if (!codes.Verify(user.Id, user.SecurityStamp, code, now))
        {
            var locked = user.RegisterFailedLogin(now);
            await users.UpdateAsync(user, cancellationToken);
            await AuditWriter.WriteAsync(auditRepository, clock, null, AuditAction.PasswordRecoveryFailed,
                nameof(User), user.Id, new { user.UserName, FailedLoginAttempts = user.FailedLoginAttempts }, cancellationToken);
            if (locked)
            {
                await AuditWriter.WriteAsync(auditRepository, clock, user.Id, AuditAction.UserLocked,
                    nameof(User), user.Id, new { user.UserName, FailedLoginAttempts = user.FailedLoginAttempts }, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure(InvalidCode);
        }

        user.RecoverPassword(passwordHasher.Hash(newPassword), now);
        await users.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(auditRepository, clock, user.Id, AuditAction.PasswordRecovered,
            nameof(User), user.Id, new { user.UserName }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    [GeneratedRegex("^[0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex SixDigits();
}
