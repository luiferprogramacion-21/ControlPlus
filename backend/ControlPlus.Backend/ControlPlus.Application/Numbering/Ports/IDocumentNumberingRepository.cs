using System.Data;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Numbering.Ports;

public interface IDocumentNumberingRepository
{
    Task<Instalacion?> GetActiveInstallationAsync(
        bool forUpdate,
        CancellationToken cancellationToken = default);

    Task<ConsecutivoDocumento?> GetSalesNumberingAsync(
        Guid installationId,
        string series,
        bool forUpdate,
        CancellationToken cancellationToken = default);

    Task<bool> HasConfirmedSalesAsync(
        Guid installationId,
        string series,
        CancellationToken cancellationToken = default);

    Task<ConsecutivoDocumento?> GetCreditPaymentNumberingAsync(
        Guid installationId, string series, bool forUpdate, CancellationToken cancellationToken = default);

    Task<bool> HasCreditPaymentsAsync(
        Guid installationId, string series, CancellationToken cancellationToken = default);

    Task<ConsecutivoDocumento?> GetPurchaseNumberingAsync(
        Guid installationId, string series, bool forUpdate,
        CancellationToken cancellationToken = default);

    Task<bool> HasNumberedPurchasesAsync(
        Guid installationId, string series, CancellationToken cancellationToken = default);

    Task AddAsync(ConsecutivoDocumento numbering, CancellationToken cancellationToken = default);
}

public interface IDocumentNumberingTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IDocumentNumberingTransactionManager
{
    Task<IDocumentNumberingTransaction> BeginAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}
