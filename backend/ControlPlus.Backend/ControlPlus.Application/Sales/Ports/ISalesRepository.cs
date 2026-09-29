using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Sales.Ports;

public interface ISalesTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface ISalesTransactionManager
{
    Task<ISalesTransaction> BeginAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}

public interface ISalesRepository
{
    Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken cancellationToken = default);
    Task<Establecimiento?> GetActiveEstablishmentAsync(CancellationToken cancellationToken = default);
    Task<Terminal?> GetActiveTerminalAsync(Guid installationId, CancellationToken cancellationToken = default);
    Task<TurnoCaja?> GetOpenShiftForUpdateAsync(CancellationToken cancellationToken = default);
    Task<SesionOperador?> GetActiveOperatorSessionAsync(Guid shiftId, Guid terminalId, Guid userId, CancellationToken cancellationToken = default);
    Task<ConsecutivoDocumento?> GetSaleSequenceForUpdateAsync(Guid installationId, string series, CancellationToken cancellationToken = default);
    Task<OperacionIdempotente?> GetIdempotencyAsync(Guid installationId, string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Producto>> GetProductsForUpdateAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, string>> GetMeasurementUnitNamesAsync(IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MetodoPago>> GetPaymentMethodsAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default);
    Task<Cliente?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task AddSaleAsync(Venta sale, CancellationToken cancellationToken = default);
    Task AddSaleDetailsAsync(IReadOnlyCollection<DetalleVenta> details, CancellationToken cancellationToken = default);
    Task AddPaymentsAsync(IReadOnlyCollection<PagoVenta> payments, CancellationToken cancellationToken = default);
    Task AddCashMovementAsync(MovimientoCaja movement, CancellationToken cancellationToken = default);
    Task AddInventoryMovementsAsync(IReadOnlyCollection<MovimientoInventario> movements, CancellationToken cancellationToken = default);
    Task AddIdempotencyAsync(OperacionIdempotente operation, CancellationToken cancellationToken = default);

    Task<PagedResult<Venta>> ListAsync(Guid? userId, SaleListQuery query, CancellationToken cancellationToken = default);
    Task<Venta?> GetSaleAsync(Guid saleId, CancellationToken cancellationToken = default);
}
