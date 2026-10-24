using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Reports;

public sealed record CashFlowEntry(DateOnly Date, string Origin, string? Method, decimal Amount);
public sealed record DailyCashFlow(DateOnly Date, decimal OpeningBalance,
    IReadOnlyList<CashFlowEntry> Payments, IReadOnlyList<CashFlowEntry> ManualMovements,
    decimal PaymentTotal, decimal ManualTotal);
public sealed record DailyCashFlowReport(DateOnly From, DateOnly To, IReadOnlyList<DailyCashFlow> Days,
    decimal OpeningBalance, decimal PaymentTotal, decimal ManualTotal);

public interface IDailyCashFlowRepository
{
    Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CashFlowEntry>> GetEntriesAsync(Guid installationId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken);
}

public interface IDailyCashFlowService
{
    Task<Result<DailyCashFlowReport>> GetAsync(ActorContext actor, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken);
}
