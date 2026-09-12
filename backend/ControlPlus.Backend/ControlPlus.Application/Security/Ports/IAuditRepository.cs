using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.Security;

namespace ControlPlus.Application.Security.Ports;

public interface IAuditRepository
{
    Task AddAsync(AuditRecord auditRecord, CancellationToken cancellationToken = default);

    Task<PagedResult<AuditRecord>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}
