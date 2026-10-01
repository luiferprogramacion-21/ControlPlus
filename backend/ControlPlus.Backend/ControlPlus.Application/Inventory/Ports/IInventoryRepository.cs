using ControlPlus.Application.Common;
using ControlPlus.Application.Inventory.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Inventory.Ports;

public sealed record InventoryStockRecord(
    Producto Product,
    string MeasurementUnit,
    string? LastMovementType,
    DateTime? LastMovementAtUtc);

public interface IInventoryRepository
{
    Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken cancellationToken = default);
    Task<Terminal?> GetActiveTerminalAsync(Guid installationId, CancellationToken cancellationToken = default);
    Task<PagedResult<InventoryStockRecord>> ListStockAsync(InventoryStockQuery query, CancellationToken cancellationToken = default);
    Task<PagedResult<MovimientoInventario>> ListMovementsAsync(InventoryMovementQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MotivoOperacion>> ListActiveAdjustmentReasonsAsync(CancellationToken cancellationToken = default);
    Task<MotivoOperacion?> GetActiveAdjustmentReasonAsync(Guid reasonId, CancellationToken cancellationToken = default);
    Task<Producto?> GetProductForUpdateAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<Usuario?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<OperacionIdempotente?> GetIdempotencyAsync(Guid installationId, string key, CancellationToken cancellationToken = default);
    Task<MovimientoInventario?> GetMovementAsync(Guid movementId, CancellationToken cancellationToken = default);
    Task AddAuthorizationAsync(AutorizacionOperacion authorization, CancellationToken cancellationToken = default);
    Task AddMovementAsync(MovimientoInventario movement, CancellationToken cancellationToken = default);
    Task AddIdempotencyAsync(OperacionIdempotente operation, CancellationToken cancellationToken = default);
}
