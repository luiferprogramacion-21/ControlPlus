using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using User = ControlPlus.Domain.OfficialModel.Usuario;
using Role = ControlPlus.Domain.OfficialModel.Rol;

namespace ControlPlus.Application.Security.Ports;

public interface IUserRepository
{
    Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken = default);

    Task<bool> HasAvailableAdministratorAsync(CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a current, flattened authorization projection for an active user.
    /// Inactive roles and permissions must not be represented in the returned claims.
    /// </summary>
    Task<UserAuthorizationSnapshot?> GetAuthorizationSnapshotAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<User>> GetByRoleIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<PagedResult<User>> ListAsync(UserListQuery query, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);

    Task UpdateAsync(User user, CancellationToken cancellationToken = default);

    Task ReplaceRoleAsync(
        User user,
        Role role,
        Guid assignedByUserId,
        DateTimeOffset assignedAtUtc,
        CancellationToken cancellationToken = default);
}
