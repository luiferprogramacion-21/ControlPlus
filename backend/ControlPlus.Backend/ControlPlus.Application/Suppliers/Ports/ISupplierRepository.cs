using ControlPlus.Application.Common;
using ControlPlus.Application.Suppliers.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Suppliers.Ports;

public interface ISupplierTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}

public interface ISupplierRepository
{
    Task<PagedResult<Proveedor>> ListAsync(SupplierListQuery query, int offset, CancellationToken ct = default);
    Task<Proveedor?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Proveedor supplier, CancellationToken ct = default);
    Task<ISupplierTransaction> BeginAsync(CancellationToken ct = default);
}
