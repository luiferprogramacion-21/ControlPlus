using ControlPlus.Application.Common;
using ControlPlus.Application.PurchaseOrders.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.PurchaseOrders.Ports;

public interface IPurchaseOrderTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

public interface IPurchaseOrderRepository
{
    Task<IPurchaseOrderTransaction> BeginAsync(CancellationToken ct = default);
    Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken ct = default);
    Task<Instalacion?> GetActiveInstallationAsync(CancellationToken ct = default);
    Task<Proveedor?> GetSupplierForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyCollection<Producto>> GetProductsForUpdateAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<ConsecutivoDocumento?> GetNumberingForUpdateAsync(
        Guid installationId, string series, CancellationToken ct = default);
    Task<ConsecutivoDocumento?> GetNumberingAsync(
        Guid installationId, string series, CancellationToken ct = default);
    Task AddNumberingAsync(ConsecutivoDocumento numbering, CancellationToken ct = default);
    /// <summary>Creates the PED consecutive (prefix PED, starting at 1) when it does not exist; never touches an existing one.</summary>
    Task EnsureNumberingAsync(Guid installationId, string series, CancellationToken ct = default);
    Task<PedidoCompra?> GetForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<PedidoCompra?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<PedidoCompra>> ListAsync(Guid installationId, PurchaseOrderListQuery query, int offset,
        CancellationToken ct = default);
    Task<bool> HasDraftForSupplierAsync(Guid installationId, Guid supplierId,
        Guid? excludingOrderId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> PendingOtherOrdersAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);
    Task AddAsync(PedidoCompra order, CancellationToken ct = default);
    Task ReplaceLinesAsync(PedidoCompra order, IReadOnlyCollection<DetallePedidoCompra> lines,
        CancellationToken ct = default);
    Task<IReadOnlyCollection<MotivoOperacion>> ListReasonsAsync(Guid establishmentId,
        string type, CancellationToken ct = default);
    Task<MotivoOperacion?> GetReasonAsync(Guid establishmentId, string type,
        Guid id, CancellationToken ct = default);
    Task AddReasonsAsync(IReadOnlyCollection<MotivoOperacion> reasons, CancellationToken ct = default);
    Task<Usuario?> GetUserForUpdateAsync(Guid id, CancellationToken ct = default);
    Task AddAuthorizationAsync(AutorizacionOperacion authorization, CancellationToken ct = default);
}
