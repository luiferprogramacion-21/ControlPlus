using System.Data;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Numbering.Ports;

public interface IDocumentNumberingRepository
{
    Task<ConsecutivoDocumento?> GetSaleChangeNumberingAsync(
        Guid installationId, string series, bool forUpdate,
        CancellationToken cancellationToken = default);
    Task<Instalacion?> GetActiveInstallationAsync(
        bool forUpdate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the consecutive of a document type (its fixed prefix, starting at number 1) when it does not exist
    /// for the installation and series. It runs inside the caller's transaction when there is one, is safe under
    /// concurrency and never modifies an existing consecutive.
    /// </summary>
    Task EnsureNumberingAsync(
        Guid installationId, string series, string documentType,
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

    Task<ConsecutivoDocumento?> GetLayawayNumberingAsync(
        Guid installationId, string series, bool forUpdate, CancellationToken cancellationToken = default);

    Task<ConsecutivoDocumento?> GetLayawayPaymentNumberingAsync(
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
