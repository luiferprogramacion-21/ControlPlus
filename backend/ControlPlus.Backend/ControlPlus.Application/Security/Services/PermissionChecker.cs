using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Application.Security.Services;

/// <summary>
/// Resolves permissions from current persistence state, so role/permission edits do not rely on stale JWT claims.
/// </summary>
public sealed class PermissionChecker : IPermissionChecker
{
    private readonly IUserRepository _userRepository;

    public PermissionChecker(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(permissionCode))
        {
            return false;
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive || user.IsLocked)
        {
            return false;
        }

        var authorization = await _userRepository.GetAuthorizationSnapshotAsync(userId, cancellationToken);
        return authorization is not null &&
               authorization.PermissionCodes.Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }
}
