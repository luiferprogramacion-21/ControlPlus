using ControlPlus.Application.Common;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Credits.Ports;

public interface ICreditsRepository
{
    Task<PagedResult<Credito>> ListAsync(CreditListQuery query, DateOnly today, int offset,
        CancellationToken cancellationToken = default);
    Task<Credito?> GetAsync(Guid creditId, CancellationToken cancellationToken = default);
    Task<Credito?> GetForUpdateAsync(Guid creditId, CancellationToken cancellationToken = default);
    Task<PagedResult<PagoCredito>> ListPaymentsAsync(Guid creditId, int page, int pageSize, int offset,
        CancellationToken cancellationToken = default);
    Task AddPaymentsAsync(IReadOnlyCollection<PagoCredito> payments, CancellationToken cancellationToken = default);
}
