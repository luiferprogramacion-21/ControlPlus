using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using Role = ControlPlus.Domain.OfficialModel.Rol;

namespace ControlPlus.Application.Security.Ports;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<Role?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Role>> GetByPermissionIdAsync(Guid permissionId, CancellationToken cancellationToken = default);

    Task<PagedResult<Role>> ListAsync(RoleListQuery query, CancellationToken cancellationToken = default);

    Task AddAsync(Role role, CancellationToken cancellationToken = default);

    Task UpdateAsync(Role role, CancellationToken cancellationToken = default);
}
