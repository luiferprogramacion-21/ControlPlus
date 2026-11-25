using ControlPlus.Application.Common;
using ControlPlus.Application.Suppliers.Contracts;
using ControlPlus.Application.Suppliers.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfSupplierRepository(OfficialControlPlusDbContext dbContext) : ISupplierRepository
{
    public async Task<PagedResult<Proveedor>> ListAsync(SupplierListQuery query, int offset, CancellationToken ct = default)
    {
        var rows = dbContext.Proveedor.AsNoTracking().AsQueryable();
        if (query.IsActive is bool active) rows = rows.Where(x => x.Activo == active);
        if (query.Name is not null) rows = rows.Where(x => x.NombreRazonSocial.Equals(query.Name, StringComparison.CurrentCultureIgnoreCase));
        if (query.DocumentType is not null)
            rows = rows.Where(x => x.TipoDocumento == query.DocumentType && x.NumeroDocumento == query.DocumentNumber);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.NombreRazonSocial).ThenBy(x => x.Id)
            .Skip(offset).Take(query.PageSize).ToArrayAsync(ct);
        return new PagedResult<Proveedor>(items, query.Page, query.PageSize, total);
    }

    public Task<Proveedor?> GetAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Proveedor.SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task AddAsync(Proveedor supplier, CancellationToken ct = default) =>
        dbContext.Proveedor.AddAsync(supplier, ct).AsTask();

    public async Task<ISupplierTransaction> BeginAsync(CancellationToken ct = default) =>
        new Transaction(await dbContext.Database.BeginTransactionAsync(ct));

    private sealed class Transaction(IDbContextTransaction transaction) : ISupplierTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
