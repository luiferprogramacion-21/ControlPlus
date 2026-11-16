using ControlPlus.Application.Credits.Ports;
using ControlPlus.Domain.Credits;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

/// <summary>
/// Saldo de los créditos abiertos (PENDIENTE o VENCIDO con saldo) de un cliente. Cuáles están vencidos lo decide
/// <see cref="CreditStatusPolicy"/> en memoria: un cliente tiene pocos créditos abiertos.
/// </summary>
public sealed class EfCustomerCreditExposureReader(OfficialControlPlusDbContext dbContext) : ICustomerCreditExposureReader
{
    public async Task<CustomerCreditExposure> GetAsync(Guid customerId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var open = await dbContext.Credito.AsNoTracking()
            .Where(x => x.Venta.ClienteId == customerId && (x.Estado == "PENDIENTE" || x.Estado == "VENCIDO") && x.SaldoPendiente > 0)
            .Select(x => new { x.Id, x.SaldoPendiente })
            .ToListAsync(cancellationToken);
        var ids = open.Select(x => x.Id).ToArray();
        var installments = await dbContext.CuotaCredito.AsNoTracking()
            .Where(x => ids.Contains(x.CreditoId) && x.SaldoPendiente > 0)
            .Select(x => new { x.CreditoId, x.FechaVencimiento, x.SaldoPendiente })
            .ToListAsync(cancellationToken);
        var overdue = installments.Where(x => CreditStatusPolicy.IsOverdue(
                x.FechaVencimiento, x.SaldoPendiente, today)).ToArray();
        return new CustomerCreditExposure(open.Sum(x => x.SaldoPendiente),
            overdue.Sum(x => x.SaldoPendiente), overdue.Select(x => x.CreditoId).Distinct().Count());
    }
}
