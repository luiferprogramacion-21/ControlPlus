using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Customers.Ports;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfCustomerHistoryRepository(OfficialControlPlusDbContext db) : ICustomerHistoryRepository
{
    // Keep the set-operation projection scalar. Format public document numbers after SQL pagination.
    private sealed class Row
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = null!;
        public Guid? ParentId { get; init; }
        public string Prefix { get; init; } = null!;
        public string Series { get; init; } = null!;
        public long Consecutive { get; init; }
        public DateTime OccurredAt { get; init; }
        public string Status { get; init; } = null!;
        public decimal Amount { get; init; }
        public decimal? OutstandingAmount { get; init; }
    }

    public Task<bool?> GetCustomerStatusAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Cliente.AsNoTracking().Where(x => x.Id == customerId).Select(x => (bool?)x.Activo)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<CustomerHistoryItem>> ListAsync(Guid customerId, Guid actorId,
        bool allSales, bool ownSales, bool credits, bool layaways, int page, int pageSize,
        int offset, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var installationId = await db.Instalacion.AsNoTracking().Where(x => x.Activo)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (installationId is null)
            return new PagedResult<CustomerHistoryItem>([], page, pageSize, 0);

        IQueryable<Row> rows = db.Venta.AsNoTracking()
            .Where(x => x.InstalacionId == installationId && x.ClienteId == customerId &&
                (allSales || (ownSales && x.UsuarioId == actorId)))
            .Select(x => new Row
            {
                Id = x.Id, Type = "SALE", ParentId = null, Prefix = x.Prefijo, Series = x.Serie,
                Consecutive = x.Consecutivo, OccurredAt = x.FechaHora, Status = x.Estado,
                Amount = x.Total, OutstandingAmount = null
            });
        rows = rows.Concat(db.Credito.AsNoTracking()
            .Where(x => credits && x.Venta.InstalacionId == installationId && x.Venta.ClienteId == customerId)
            .Select(x => new Row
            {
                Id = x.Id, Type = "CREDIT", ParentId = x.VentaId, Prefix = x.Venta.Prefijo,
                Series = x.Venta.Serie, Consecutive = x.Venta.Consecutivo, OccurredAt = x.Venta.FechaHora,
                Status = x.Estado, Amount = x.MontoAjustado, OutstandingAmount = x.SaldoPendiente
            }));
        rows = rows.Concat(db.PagoCredito.AsNoTracking()
            .Where(x => credits && x.InstalacionId == installationId &&
                x.Credito.Venta.InstalacionId == installationId && x.Credito.Venta.ClienteId == customerId)
            .Select(x => new Row
            {
                Id = x.Id, Type = "CREDIT_PAYMENT", ParentId = x.CreditoId, Prefix = x.Prefijo,
                Series = x.Serie, Consecutive = x.Consecutivo, OccurredAt = x.FechaHora,
                Status = x.Estado, Amount = x.ValorAplicado, OutstandingAmount = null
            }));
        rows = rows.Concat(db.Apartado.AsNoTracking()
            .Where(x => layaways && x.InstalacionId == installationId && x.ClienteId == customerId)
            .Select(x => new Row
            {
                Id = x.Id, Type = "LAYAWAY", ParentId = null, Prefix = x.Prefijo, Series = x.Serie,
                Consecutive = x.Consecutivo, OccurredAt = x.FechaHora, Status = x.Estado,
                Amount = x.Total, OutstandingAmount = x.SaldoPendiente
            }));
        rows = rows.Concat(db.PagoApartado.AsNoTracking()
            .Where(x => layaways && x.InstalacionId == installationId &&
                x.Apartado.InstalacionId == installationId && x.Apartado.ClienteId == customerId)
            .Select(x => new Row
            {
                Id = x.Id, Type = "LAYAWAY_PAYMENT", ParentId = x.ApartadoId, Prefix = x.Prefijo,
                Series = x.Serie, Consecutive = x.Consecutivo, OccurredAt = x.FechaHora,
                Status = x.Estado, Amount = x.ValorAplicado, OutstandingAmount = null
            }));
        var count = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Type)
            .ThenByDescending(x => x.Id).Skip(offset).Take(pageSize).ToArrayAsync(cancellationToken);
        return new PagedResult<CustomerHistoryItem>(items.Select(x => new CustomerHistoryItem(x.Id, x.Type,
            x.ParentId, $"{x.Prefix}-{x.Series}-{x.Consecutive}",
            new DateTimeOffset(DateTime.SpecifyKind(x.OccurredAt, DateTimeKind.Utc)),
            x.Status, x.Amount, x.OutstandingAmount)).ToArray(), page, pageSize, count);
    }
}
