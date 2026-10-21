using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfLayawayPaymentRepository(OfficialControlPlusDbContext dbContext)
    : ILayawayPaymentRepository
{
    public async Task<Apartado?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Apartado.FromSqlInterpolated(
            $"SELECT a.* FROM ventas.apartado AS a WHERE a.id = {id} FOR UPDATE")
            .AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task AddPaymentsAsync(IReadOnlyCollection<PagoApartado> payments,
        CancellationToken cancellationToken) =>
        dbContext.PagoApartado.AddRangeAsync(payments, cancellationToken);
}
