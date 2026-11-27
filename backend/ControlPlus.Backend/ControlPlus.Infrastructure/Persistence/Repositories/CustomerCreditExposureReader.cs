using ControlPlus.Application.Credits.Ports;
using ControlPlus.Domain.Credits;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

/// <summary>
/// Saldo de los créditos abiertos (PENDIENTE o VENCIDO con saldo) de uno o varios clientes. Cuáles están vencidos lo
/// decide <see cref="CreditStatusPolicy"/> en memoria: cada cliente tiene pocos créditos abiertos.
/// </summary>
public sealed class EfCustomerCreditExposureReader(OfficialControlPlusDbContext dbContext) : ICustomerCreditExposureReader
{
    public async Task<CustomerCreditExposure> GetAsync(Guid customerId, DateOnly today, CancellationToken cancellationToken = default) =>
        (await GetManyAsync([customerId], today, cancellationToken)).TryGetValue(customerId, out var exposure)
            ? exposure
            : new CustomerCreditExposure(0, 0, 0);

    public async Task<IReadOnlyDictionary<Guid, CustomerCreditExposure>> GetManyAsync(
        IReadOnlyCollection<Guid> customerIds, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0) return new Dictionary<Guid, CustomerCreditExposure>();
        var ids = customerIds.Distinct().ToArray();
        return await ReadAsync(ids, today, cancellationToken);
    }

    public Task<IReadOnlyDictionary<Guid, CustomerCreditExposure>> GetAllAsync(DateOnly today, CancellationToken cancellationToken = default) =>
        ReadAsync(null, today, cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, CustomerCreditExposure>> ReadAsync(
        Guid[]? ids, DateOnly today, CancellationToken cancellationToken)
    {
        var credits = dbContext.Credito.AsNoTracking()
            .Where(x => x.Venta.ClienteId != null && (x.Estado == "PENDIENTE" || x.Estado == "VENCIDO") && x.SaldoPendiente > 0);
        if (ids is not null) credits = credits.Where(x => ids.Contains(x.Venta.ClienteId!.Value));
        var open = await credits
            .Select(x => new { x.Id, CustomerId = x.Venta.ClienteId!.Value, x.SaldoPendiente })
            .ToListAsync(cancellationToken);
        var creditIds = open.Select(x => x.Id).ToArray();
        var installments = await dbContext.CuotaCredito.AsNoTracking()
            .Where(x => creditIds.Contains(x.CreditoId) && x.SaldoPendiente > 0)
            .Select(x => new { x.CreditoId, x.FechaVencimiento, x.SaldoPendiente })
            .ToListAsync(cancellationToken);
        var customerOf = open.ToDictionary(x => x.Id, x => x.CustomerId);
        return open.GroupBy(x => x.CustomerId).ToDictionary(group => group.Key, group =>
        {
            var own = installments.Where(x => customerOf[x.CreditoId] == group.Key).ToArray();
            var overdue = own.Where(x => CreditStatusPolicy.IsOverdue(x.FechaVencimiento, x.SaldoPendiente, today)).ToArray();
            var pending = own.Except(overdue).ToArray();
            return new CustomerCreditExposure(group.Sum(x => x.SaldoPendiente), overdue.Sum(x => x.SaldoPendiente),
                overdue.Select(x => x.CreditoId).Distinct().Count(),
                pending.Length == 0 ? null : pending.Min(x => x.FechaVencimiento),
                overdue.Length == 0 ? null : overdue.Min(x => x.FechaVencimiento));
        });
    }
}
