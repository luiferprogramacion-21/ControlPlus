using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfSalesTransactionManager(OfficialControlPlusDbContext dbContext) : ISalesTransactionManager
{
    public async Task<ISalesTransaction> BeginAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default) =>
        new EfSalesTransaction(await dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken));

    private sealed class EfSalesTransaction(IDbContextTransaction transaction) : ISalesTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}

public sealed class EfSalesRepository(OfficialControlPlusDbContext dbContext) : ISalesRepository
{
    public async Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Instalacion.FromSqlRaw(
            """
            SELECT i.*
            FROM configuracion.instalacion AS i
            WHERE i.activo
            ORDER BY i.fecha_instalacion, i.id
            LIMIT 1
            FOR UPDATE
            """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<Establecimiento?> GetActiveEstablishmentAsync(CancellationToken cancellationToken = default) =>
        dbContext.Establecimiento.AsNoTracking()
            .Where(x => x.Activo)
            .OrderBy(x => x.FechaCreacion).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Terminal?> GetActiveTerminalAsync(Guid installationId, CancellationToken cancellationToken = default) =>
        dbContext.Terminal.AsNoTracking()
            .Where(x => x.InstalacionId == installationId && x.Activo)
            .OrderBy(x => x.FechaCreacion).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<TurnoCaja?> GetOpenShiftForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.TurnoCaja.FromSqlRaw(
            """
            SELECT t.*
            FROM caja.turno_caja AS t
            WHERE t.estado = 'ABIERTA'
            ORDER BY t.fecha_hora_apertura, t.id
            LIMIT 1
            FOR UPDATE
            """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<SesionOperador?> GetActiveOperatorSessionAsync(
        Guid shiftId,
        Guid terminalId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.SesionOperador.AsTracking().SingleOrDefaultAsync(
            x => x.TurnoCajaId == shiftId && x.TerminalId == terminalId && x.UsuarioId == userId &&
                 x.Estado == CashConstants.ActiveSessionState,
            cancellationToken);

    public async Task<ConsecutivoDocumento?> GetSaleSequenceForUpdateAsync(
        Guid installationId,
        string series,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ConsecutivoDocumento.FromSqlInterpolated(
            $"""
             SELECT c.*
             FROM configuracion.consecutivo_documento AS c
             WHERE c.instalacion_id = {installationId}
               AND c.tipo_documento = 'VENTA'
               AND c.serie = {series}
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<OperacionIdempotente?> GetIdempotencyAsync(
        Guid installationId,
        string key,
        CancellationToken cancellationToken = default) =>
        dbContext.OperacionIdempotente.AsNoTracking().SingleOrDefaultAsync(
            x => x.InstalacionId == installationId && x.ClaveIdempotencia == key,
            cancellationToken);

    public async Task<IReadOnlyCollection<Producto>> GetProductsForUpdateAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var ids = productIds.Distinct().Order().ToArray();
        return await dbContext.Producto.FromSqlInterpolated(
            $"""
             SELECT p.*
             FROM catalogo.producto AS p
             WHERE p.id = ANY ({ids})
             ORDER BY p.id
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetMeasurementUnitNamesAsync(
        IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken = default) =>
        await dbContext.UnidadMedida.AsNoTracking()
            .Where(x => unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Nombre, cancellationToken);

    public async Task<IReadOnlyCollection<MetodoPago>> GetPaymentMethodsAsync(
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken = default) =>
        await dbContext.MetodoPago.AsNoTracking()
            .Where(x => codes.Contains(x.Codigo))
            .OrderBy(x => x.OrdenVisual).ThenBy(x => x.Codigo)
            .ToArrayAsync(cancellationToken);

    public Task<Cliente?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        dbContext.Cliente.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);

    public Task AddSaleAsync(Venta sale, CancellationToken cancellationToken = default) =>
        dbContext.Venta.AddAsync(sale, cancellationToken).AsTask();

    public Task AddSaleDetailsAsync(IReadOnlyCollection<DetalleVenta> details, CancellationToken cancellationToken = default) =>
        dbContext.DetalleVenta.AddRangeAsync(details, cancellationToken);

    public Task AddPaymentsAsync(IReadOnlyCollection<PagoVenta> payments, CancellationToken cancellationToken = default) =>
        dbContext.PagoVenta.AddRangeAsync(payments, cancellationToken);

    public Task AddCashMovementAsync(MovimientoCaja movement, CancellationToken cancellationToken = default) =>
        dbContext.MovimientoCaja.AddAsync(movement, cancellationToken).AsTask();

    public Task AddInventoryMovementsAsync(IReadOnlyCollection<MovimientoInventario> movements, CancellationToken cancellationToken = default) =>
        dbContext.MovimientoInventario.AddRangeAsync(movements, cancellationToken);

    public Task AddIdempotencyAsync(OperacionIdempotente operation, CancellationToken cancellationToken = default) =>
        dbContext.OperacionIdempotente.AddAsync(operation, cancellationToken).AsTask();

    public async Task<PagedResult<Venta>> ListAsync(
        Guid? userId,
        SaleListQuery query,
        CancellationToken cancellationToken = default)
    {
        var source = dbContext.Venta.AsNoTracking().AsQueryable();
        if (userId is Guid ownUserId) source = source.Where(x => x.UsuarioId == ownUserId);
        var count = await source.CountAsync(cancellationToken);
        var items = await source.OrderByDescending(x => x.FechaHora).ThenByDescending(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return new PagedResult<Venta>(items, query.Page, query.PageSize, count);
    }

    public Task<Venta?> GetSaleAsync(Guid saleId, CancellationToken cancellationToken = default) =>
        dbContext.Venta.AsNoTracking()
            .Include(x => x.DetalleVenta)
            .Include(x => x.PagoVenta).ThenInclude(x => x.MetodoPago)
            .SingleOrDefaultAsync(x => x.Id == saleId, cancellationToken);
}
