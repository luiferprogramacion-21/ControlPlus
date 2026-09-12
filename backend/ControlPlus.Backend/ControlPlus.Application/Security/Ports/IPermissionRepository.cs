using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using Permission = ControlPlus.Domain.OfficialModel.Permiso;

namespace ControlPlus.Application.Security.Ports;

public interface IPermissionRepository
{
    Task<Permission?> GetByIdAsync(Guid permissionId, CancellationToken cancellationToken = default);

    Task<Permission?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<PagedResult<Permission>> ListAsync(PermissionListQuery query, CancellationToken cancellationToken = default);

    Task AddAsync(Permission permission, CancellationToken cancellationToken = default);

    Task UpdateAsync(Permission permission, CancellationToken cancellationToken = default);
}
