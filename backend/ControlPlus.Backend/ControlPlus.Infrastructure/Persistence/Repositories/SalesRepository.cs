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

    public async Task<Establecimiento?> GetActiveEstablishmentForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Establecimiento.FromSqlRaw("""
            SELECT e.* FROM configuracion.establecimiento AS e
            WHERE e.activo ORDER BY e.fecha_creacion, e.id LIMIT 1 FOR UPDATE
            """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

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

    public async Task<SesionOperador?> GetActiveOperatorSessionForUpdateAsync(Guid shiftId,
        Guid terminalId, Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.SesionOperador.FromSqlInterpolated($"""
            SELECT s.* FROM caja.sesion_operador AS s
            WHERE s.turno_caja_id = {shiftId} AND s.terminal_id = {terminalId}
              AND s.usuario_id = {userId} AND s.estado = 'ACTIVA'
            FOR UPDATE
            """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

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

    public async Task<Usuario?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Usuario.FromSqlInterpolated(
            $"SELECT u.* FROM seguridad.usuario AS u WHERE u.id = {userId} FOR UPDATE")
            .AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task AddCreditAsync(Credito credit, CancellationToken cancellationToken = default) =>
        dbContext.Credito.AddAsync(credit, cancellationToken).AsTask();

    public Task AddAuthorizationAsync(AutorizacionOperacion authorization, CancellationToken cancellationToken = default) =>
        dbContext.AutorizacionOperacion.AddAsync(authorization, cancellationToken).AsTask();

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

    public Task AddReceiptSnapshotAsync(DocumentoEmitido snapshot, CancellationToken cancellationToken = default) =>
        dbContext.DocumentoEmitido.AddAsync(snapshot, cancellationToken).AsTask();

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
            .Include(x => x.Credito)
            .SingleOrDefaultAsync(x => x.Id == saleId, cancellationToken);

    public Task<Venta?> GetReceiptSaleHeaderAsync(Guid saleId, CancellationToken cancellationToken = default) =>
        dbContext.Venta.AsNoTracking().SingleOrDefaultAsync(x => x.Id == saleId, cancellationToken);

    public Task<DocumentoEmitido?> GetReceiptSnapshotAsync(Guid installationId, Guid saleId,
        CancellationToken cancellationToken = default) =>
        dbContext.DocumentoEmitido.AsNoTracking().SingleOrDefaultAsync(x =>
            x.InstalacionId == installationId && x.TipoDocumento == "COMPROBANTE_VENTA" &&
            x.EntidadId == saleId && x.VersionDocumento == 1, cancellationToken);

    public async Task<Venta?> FindSaleByVisibleNumberAsync(string visibleNumber,
        CancellationToken cancellationToken = default)
    {
        var installation = await dbContext.Instalacion.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Activo, cancellationToken);
        if (installation is null) return null;
        var separator = $"-{installation.Serie}-";
        var boundary = visibleNumber.LastIndexOf(separator, StringComparison.Ordinal);
        if (boundary <= 0 || !long.TryParse(visibleNumber.AsSpan(boundary + separator.Length),
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                out var consecutive) || consecutive <= 0)
            return null;
        var prefix = visibleNumber[..boundary];
        var saleId = await dbContext.Venta.AsNoTracking()
            .Where(x => x.InstalacionId == installation.Id && x.Prefijo == prefix &&
                x.Serie == installation.Serie && x.Consecutivo == consecutive)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        return saleId is Guid id ? await GetSaleAsync(id, cancellationToken) : null;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetConfirmedReturnedQuantitiesAsync(Guid saleId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.DetalleCambioVenta.AsNoTracking()
            .Where(x => x.DetalleVentaOrigenId != null && x.TipoDetalle == "DEVUELTO" &&
                x.CambioVenta.VentaId == saleId && x.CambioVenta.Estado == "CONFIRMADO")
            .GroupBy(x => x.DetalleVentaOrigenId!.Value)
            .Select(x => new { SaleLineId = x.Key, Quantity = x.Sum(y => y.Cantidad) })
            .ToArrayAsync(cancellationToken);
        return rows.ToDictionary(x => x.SaleLineId, x => x.Quantity);
    }

    public async Task<IReadOnlyCollection<CambioVenta>> GetChangesAsync(Guid saleId,
        CancellationToken cancellationToken = default) =>
        await dbContext.CambioVenta.AsNoTracking()
            .Where(x => x.VentaId == saleId)
            .Include(x => x.DetalleCambioVenta)
            .Include(x => x.PagoCambioVenta).ThenInclude(x => x.MetodoPago)
            .OrderBy(x => x.FechaHora).ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);

    public Task<bool> HasConfirmedChangesAsync(Guid saleId,
        CancellationToken cancellationToken = default) =>
        dbContext.CambioVenta.AsNoTracking()
            .AnyAsync(x => x.VentaId == saleId && x.Estado == "CONFIRMADO", cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetConfirmedChangeNetPaymentsByMethodAsync(Guid saleId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.PagoCambioVenta.AsNoTracking()
            .Where(x => x.CambioVenta.VentaId == saleId && x.CambioVenta.Estado == "CONFIRMADO" &&
                x.Estado == "APLICADO")
            .GroupBy(x => x.MetodoPagoId)
            .Select(x => new { MethodId = x.Key, Amount = x.Sum(y =>
                y.TipoMovimiento == "COBRO" ? y.Valor : -y.Valor) })
            .ToArrayAsync(cancellationToken);
        return rows.ToDictionary(x => x.MethodId, x => x.Amount);
    }

    public async Task<ConsecutivoDocumento?> GetChangeSequenceForUpdateAsync(Guid installationId,
        string series, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ConsecutivoDocumento.FromSqlInterpolated($"""
            SELECT c.* FROM configuracion.consecutivo_documento AS c
            WHERE c.instalacion_id = {installationId}
              AND c.tipo_documento = 'CAMBIO_VENTA' AND c.serie = {series}
            FOR UPDATE
            """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<Credito?> GetSaleCreditForUpdateAsync(Guid saleId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Credito.FromSqlInterpolated($"""
            SELECT c.* FROM ventas.credito AS c WHERE c.venta_id = {saleId} FOR UPDATE
            """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task AddChangeAsync(CambioVenta change, CancellationToken cancellationToken = default) =>
        dbContext.CambioVenta.AddAsync(change, cancellationToken).AsTask();

    public Task AddChangeDetailsAsync(IReadOnlyCollection<DetalleCambioVenta> details,
        CancellationToken cancellationToken = default) =>
        dbContext.DetalleCambioVenta.AddRangeAsync(details, cancellationToken);

    public Task AddChangePaymentsAsync(IReadOnlyCollection<PagoCambioVenta> payments,
        CancellationToken cancellationToken = default) =>
        dbContext.PagoCambioVenta.AddRangeAsync(payments, cancellationToken);

    public Task AddCreditChangeAdjustmentAsync(AjusteCreditoCambio adjustment,
        CancellationToken cancellationToken = default) =>
        dbContext.AjusteCreditoCambio.AddAsync(adjustment, cancellationToken).AsTask();

    public void MarkSaleChanged(Venta sale) =>
        dbContext.Entry(sale).Property(x => x.Estado).IsModified = true;

    public async Task<Venta?> GetSaleForUpdateAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Venta.FromSqlInterpolated($"SELECT * FROM ventas.venta WHERE id = {saleId} FOR UPDATE")
            .AsTracking().ToArrayAsync(cancellationToken);
        var sale = rows.SingleOrDefault();
        if (sale is null) return null;
        await dbContext.Entry(sale).Collection(x => x.DetalleVenta).LoadAsync(cancellationToken);
        await dbContext.Entry(sale).Collection(x => x.PagoVenta).LoadAsync(cancellationToken);
        foreach (var payment in sale.PagoVenta)
            await dbContext.Entry(payment).Reference(x => x.MetodoPago).LoadAsync(cancellationToken);
        await dbContext.Entry(sale).Reference(x => x.Credito).LoadAsync(cancellationToken);
        return sale;
    }

    public async Task<IReadOnlyCollection<MotivoOperacion>> GetCancellationReasonsAsync(Guid establishmentId,
        string operationType, CancellationToken cancellationToken = default) =>
        await dbContext.MotivoOperacion.AsNoTracking()
            .Where(x => x.EstablecimientoId == establishmentId && x.TipoOperacion == operationType)
            .OrderBy(x => x.OrdenVisual).ThenBy(x => x.Codigo).ToArrayAsync(cancellationToken);

    public Task AddCancellationReasonsAsync(IReadOnlyCollection<MotivoOperacion> reasons,
        CancellationToken cancellationToken = default) =>
        dbContext.MotivoOperacion.AddRangeAsync(reasons, cancellationToken);

    public Task AddSaleCancellationAsync(AnulacionVenta cancellation, CancellationToken cancellationToken = default) =>
        dbContext.AnulacionVenta.AddAsync(cancellation, cancellationToken).AsTask();

    public async Task<IReadOnlyCollection<MovimientoInventario>> GetSaleInventoryMovementsAsync(Guid saleId,
        CancellationToken cancellationToken = default) =>
        await dbContext.MovimientoInventario.AsNoTracking()
            .Where(x => x.VentaId == saleId && x.TipoMovimiento == "VENTA")
            .OrderBy(x => x.ProductoId).ToArrayAsync(cancellationToken);

    public Task<bool> HasInventoryReversalAsync(Guid movementId, CancellationToken cancellationToken = default) =>
        dbContext.MovimientoInventario.AnyAsync(x => x.MovimientoRevertidoId == movementId, cancellationToken);

    public Task<MovimientoCaja?> GetOriginalSaleCashMovementAsync(Guid saleId,
        CancellationToken cancellationToken = default) =>
        dbContext.MovimientoCaja.AsNoTracking().SingleOrDefaultAsync(x => x.PagoVenta.Any(p => p.VentaId == saleId),
            cancellationToken);

    public Task<bool> HasCashReversalAsync(Guid movementId, CancellationToken cancellationToken = default) =>
        dbContext.MovimientoCaja.AnyAsync(x => x.MovimientoRevertidoId == movementId, cancellationToken);

    public Task<int> GetCreditPaymentCountAsync(Guid creditId, CancellationToken cancellationToken = default) =>
        dbContext.PagoCredito.CountAsync(x => x.CreditoId == creditId, cancellationToken);
}
