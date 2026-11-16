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
    Task<PagoCredito?> GetPaymentForUpdateAsync(Guid creditId, Guid paymentId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PagoCredito>> GetPaymentsForCashMovementForUpdateAsync(Guid cashMovementId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MetodoPago>> GetPaymentMethodsByIdAsync(IReadOnlyCollection<Guid> methodIds,
        CancellationToken cancellationToken = default);
    Task<MovimientoCaja?> GetCashMovementForUpdateAsync(Guid movementId,
        CancellationToken cancellationToken = default);
    Task<bool> HasCashReversalAsync(Guid movementId, CancellationToken cancellationToken = default);
    Task<PagedResult<PagoCredito>> ListPaymentsAsync(Guid creditId, int page, int pageSize, int offset,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CuotaCredito>> GetInstallmentsForUpdateAsync(Guid creditId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<AplicacionPagoCuota>> GetPaymentApplicationsForUpdateAsync(
        IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken = default);
    Task AddPaymentsAsync(IReadOnlyCollection<PagoCredito> payments, CancellationToken cancellationToken = default);
    Task AddPaymentApplicationsAsync(IReadOnlyCollection<AplicacionPagoCuota> applications,
        CancellationToken cancellationToken = default);
}
