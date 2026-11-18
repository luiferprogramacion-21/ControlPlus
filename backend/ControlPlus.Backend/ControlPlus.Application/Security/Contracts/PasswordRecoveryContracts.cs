using ControlPlus.Application.Common;

namespace ControlPlus.Application.Security.Contracts;

public sealed record PasswordRecoveryRequest(string UserName);

public sealed record PasswordRecoveryConfirmRequest(string UserName, string Code, string NewPassword);

/// <summary>Respuesta siempre igual, exista o no el usuario, para no revelar quién tiene cuenta o correo.</summary>
public sealed record PasswordRecoveryRequestedDto(string Message);

/// <summary>
/// Decisión del 18 de noviembre de 2026: el usuario que olvidó su contraseña la recupera con un código enviado a su
/// correo. Necesita Internet solo para enviar el código; sin correo configurado, el Administrador la restablece.
/// </summary>
public interface IPasswordRecoveryService
{
    Task<Result<PasswordRecoveryRequestedDto>> RequestAsync(
        PasswordRecoveryRequest request, CancellationToken cancellationToken = default);

    Task<Result> ConfirmAsync(PasswordRecoveryConfirmRequest request, CancellationToken cancellationToken = default);
}
