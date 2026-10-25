using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Application.Reports;

public sealed record DashboardSummary(DateOnly OperatingDate, SalesAmount CashSales,
    SalesAmount CreditSales, SalesAmount TotalSales, decimal OpeningBalance,
    decimal PaymentTotal, decimal ManualTotal, decimal NetCashFlow);

public sealed record DashboardInstallation(Guid Id, string TimeZoneId);

public interface IDashboardInstallationRepository
{
    Task<DashboardInstallation?> GetActiveAsync(CancellationToken cancellationToken);
}

public interface IDashboardSummaryService
{
    Task<Result<DashboardSummary>> GetAsync(ActorContext actor, CancellationToken cancellationToken);
}

public sealed class DashboardSummaryService(IPermissionChecker permissions, IClock clock,
    IDashboardInstallationRepository installationRepository,
    IFinancialSalesReportRepository salesRepository, IDailyCashFlowRepository cashRepository) : IDashboardSummaryService
{
    public async Task<Result<DashboardSummary>> GetAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        if (actor.UserId == Guid.Empty) return Result.Failure<DashboardSummary>(ApplicationError.Unauthorized());
        if (!await permissions.HasPermissionAsync(actor.UserId, PermissionCodes.FinancialReportsRead, cancellationToken))
            return Result.Failure<DashboardSummary>(ApplicationError.Forbidden());

        var installation = await installationRepository.GetActiveAsync(cancellationToken);
        if (installation is null) return Result.Failure<DashboardSummary>(ApplicationError.NotFound("la instalación activa"));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(installation.TimeZoneId);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
        var sales = await salesRepository.GetDailySalesAsync(installation.Id, date, date, cancellationToken);
        var cash = new SalesAmount(sales.Where(x => x.SaleType == "CONTADO").Sum(x => x.Transactions),
            sales.Where(x => x.SaleType == "CONTADO").Sum(x => x.Total));
        var credit = new SalesAmount(sales.Where(x => x.SaleType == "CREDITO").Sum(x => x.Transactions),
            sales.Where(x => x.SaleType == "CREDITO").Sum(x => x.Total));
        var entries = await cashRepository.GetEntriesAsync(installation.Id, date, date, cancellationToken);
        var opening = entries.Where(x => x.Origin == "APERTURA").Sum(x => x.Amount);
        var payments = entries.Where(x => x.Method is not null).Sum(x => x.Amount);
        var manual = entries.Where(x => x.Method is null && x.Origin != "APERTURA").Sum(x => x.Amount);
        return Result.Success(new DashboardSummary(date, cash, credit,
            new SalesAmount(cash.Transactions + credit.Transactions, cash.Total + credit.Total),
            opening, payments, manual, payments + manual));
    }
}
