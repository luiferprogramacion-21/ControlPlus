using ControlPlus.Application.Common;

namespace ControlPlus.Application.Security.Contracts;

/// <summary>
/// Used by the API JWT events to reject a token when its subject was disabled,
/// locked, or invalidated by a changed security stamp.
/// </summary>
public interface ICurrentUserSessionValidator
{
    Task<bool> ValidateAsync(Guid userId, string securityStamp, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves authorization from the current persistence state. API authorization handlers use it
/// instead of trusting permissions embedded in an old access token.
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default);
}

public interface IAuthenticationService
{
    Task<Result<UserDto>> SetupFirstAdministratorAsync(
        SetupFirstAdministratorRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<AuthenticationResult>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);
}
