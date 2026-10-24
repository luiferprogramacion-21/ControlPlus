using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Reports;

public sealed record SalesAmount(long Transactions, decimal Total);
public sealed record DailySalesReport(DateOnly Date, SalesAmount Cash, SalesAmount Credit, SalesAmount Total);
public sealed record FinancialSalesReport(DateOnly From, DateOnly To, IReadOnlyList<DailySalesReport> Days, SalesAmount Cash, SalesAmount Credit, SalesAmount Total);
public sealed record SalesReportRow(DateOnly Date, string SaleType, long Transactions, decimal Total);

public interface IFinancialSalesReportRepository
{
    Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesReportRow>> GetDailySalesAsync(Guid installationId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public interface IFinancialSalesReportService
{
    Task<Result<FinancialSalesReport>> GetAsync(ActorContext actor, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
