using ControlPlus.Application.Common;
using ControlPlus.Application.Purchases.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Purchases.Ports;

public interface IPurchaseTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

public interface IPurchaseRepository
{
    Task<IPurchaseTransaction> BeginAsync(CancellationToken ct = default);
    Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken ct = default);
    Task<Terminal?> GetActiveTerminalAsync(Guid installationId, CancellationToken ct = default);
    Task<Proveedor?> GetSupplierForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyCollection<Producto>> GetProductsForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<Compra?> GetForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<PedidoCompra?> GetOrderForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<Compra?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Compra>> ListAsync(PurchaseListQuery query, int offset, CancellationToken ct = default);
    Task<ConsecutivoDocumento?> GetSequenceForUpdateAsync(Guid installationId, string series, CancellationToken ct = default);
    Task<OperacionIdempotente?> GetIdempotencyAsync(Guid installationId, string key, CancellationToken ct = default);
    Task<bool> DocumentExistsAsync(Guid supplierId, string document, Guid? excludingId, CancellationToken ct = default);
    Task AddAsync(Compra purchase, CancellationToken ct = default);
    Task ReplaceDetailsAsync(Compra purchase, IReadOnlyCollection<DetalleCompra> details, CancellationToken ct = default);
    Task AddMovementsAsync(IReadOnlyCollection<MovimientoInventario> movements, CancellationToken ct = default);
    Task AddIdempotencyAsync(OperacionIdempotente operation, CancellationToken ct = default);
    Task<IReadOnlyCollection<MotivoOperacion>> ListCancellationReasonsAsync(Guid establishmentId, CancellationToken ct = default);
    Task<MotivoOperacion?> GetCancellationReasonAsync(Guid establishmentId, Guid id, CancellationToken ct = default);
    Task AddCancellationReasonsAsync(IReadOnlyCollection<MotivoOperacion> reasons, CancellationToken ct = default);
    Task<IReadOnlyCollection<MovimientoInventario>> GetPurchaseMovementsAsync(Guid purchaseId, CancellationToken ct = default);
    Task<bool> HasLaterMovementAsync(Guid productId, long originalOrder, CancellationToken ct = default);
    Task<bool> HasReversalAsync(Guid movementId, CancellationToken ct = default);
    Task<Usuario?> GetUserForUpdateAsync(Guid id, CancellationToken ct = default);
    Task AddAuthorizationAsync(AutorizacionOperacion authorization, CancellationToken ct = default);
    Task AddReceiptCorrectionAsync(CorreccionRecepcion correction, CancellationToken ct = default) =>
        throw new NotSupportedException("Receipt correction persistence is not configured.");
}
