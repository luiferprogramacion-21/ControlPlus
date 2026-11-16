using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Application.Reports;

public sealed record DashboardSummary(DateOnly OperatingDate, SalesAmount CashSales,
    SalesAmount CreditSales, SalesAmount TotalSales, decimal OpeningBalance,
    decimal PaymentTotal, decimal ManualTotal, decimal NetCashFlow,
    DashboardComparison Comparison, DashboardCash? CurrentCash, DashboardAlerts Alerts,
    IReadOnlyList<DashboardHour> HourlySales, IReadOnlyList<DashboardActivity> RecentActivity,
    DashboardExternalBackup ExternalBackup);

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
    IFinancialSalesReportRepository salesRepository, IDailyCashFlowRepository cashRepository,
    IDashboardOverviewRepository overviewRepository) : IDashboardSummaryService
{
    // Evita un bucle desmedido si datos anómalos abarcaran muchos días.
    private const int MaximumHourBuckets = 48;

    public async Task<Result<DashboardSummary>> GetAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        if (actor.UserId == Guid.Empty) return Result.Failure<DashboardSummary>(ApplicationError.Unauthorized());
        if (!await permissions.HasPermissionAsync(actor.UserId, PermissionCodes.FinancialReportsRead, cancellationToken))
            return Result.Failure<DashboardSummary>(ApplicationError.Forbidden());

        var installation = await installationRepository.GetActiveAsync(cancellationToken);
        if (installation is null) return Result.Failure<DashboardSummary>(ApplicationError.NotFound("la instalación activa"));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(installation.TimeZoneId);
        var now = clock.UtcNow;
        var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var date = DateOnly.FromDateTime(localNow);
        var sales = await salesRepository.GetDailySalesAsync(installation.Id, date, date, cancellationToken);
        var cash = new SalesAmount(sales.Where(x => x.SaleType == "CONTADO").Sum(x => x.Transactions),
            sales.Where(x => x.SaleType == "CONTADO").Sum(x => x.Total));
        var credit = new SalesAmount(sales.Where(x => x.SaleType == "CREDITO").Sum(x => x.Transactions),
            sales.Where(x => x.SaleType == "CREDITO").Sum(x => x.Total));
        var total = new SalesAmount(cash.Transactions + credit.Transactions, cash.Total + credit.Total);
        var entries = await cashRepository.GetEntriesAsync(installation.Id, date, date, cancellationToken);
        var opening = entries.Where(x => x.Origin == "APERTURA").Sum(x => x.Amount);
        var payments = entries.Where(x => x.Method is not null).Sum(x => x.Amount);
        var manual = entries.Where(x => x.Method is null && x.Origin != "APERTURA").Sum(x => x.Amount);

        var comparison = await BuildComparisonAsync(installation.Id, date, total.Total, now, cancellationToken);
        var currentCash = await overviewRepository.GetCurrentCashAsync(cancellationToken);
        var alerts = new DashboardAlerts(
            await overviewRepository.CountLowStockProductsAsync(cancellationToken),
            await overviewRepository.CountOverdueCreditCustomersAsync(installation.Id, date, cancellationToken),
            await overviewRepository.CountUpcomingLayawaysAsync(installation.Id, date,
                date.AddDays(DashboardRules.UpcomingLayawayDays), cancellationToken),
            DashboardRules.UpcomingLayawayDays);
        var hourly = BuildHourlySales(
            await overviewRepository.GetSaleTimesAsync(installation.Id, date, cancellationToken), zone, localNow);
        var recent = (await overviewRepository.GetRecentSalesAsync(installation.Id, date,
                DashboardRules.RecentActivityLimit, cancellationToken))
            .Select(x => new DashboardActivity(x.Id, $"{x.Prefix}-{x.Series}-{x.Consecutive}",
                new DateTimeOffset(DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc)),
                x.CustomerName, x.Total, x.SaleType, x.State))
            .ToArray();
        var externalBackup = await overviewRepository.GetExternalBackupAsync(installation.Id, cancellationToken);

        return Result.Success(new DashboardSummary(date, cash, credit, total,
            opening, payments, manual, payments + manual, comparison, currentCash, alerts, hourly, recent, externalBackup));
    }

    /// <summary>
    /// Compara las ventas de hoy con las de ayer hasta esta misma hora (ahora menos 24 horas), no con el día completo.
    /// Si ayer no hubo ventas no hay porcentaje: dividir por cero no tiene sentido.
    /// </summary>
    private async Task<DashboardComparison> BuildComparisonAsync(Guid installationId, DateOnly date,
        decimal todayTotal, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var previousDate = date.AddDays(-1);
        var previousTotal = await overviewRepository.GetSalesTotalUntilAsync(installationId, previousDate,
            now.AddDays(-1).UtcDateTime, cancellationToken);
        decimal? percent = previousTotal > 0
            ? Math.Round((todayTotal - previousTotal) / previousTotal * 100m, 0, MidpointRounding.AwayFromZero)
            : null;
        return new DashboardComparison(previousDate, previousTotal, percent);
    }

    /// <summary>
    /// Una barra por hora local desde la primera venta hasta la hora actual (o la última venta), con las horas
    /// sin ventas en cero. Sin ventas no hay barras.
    /// </summary>
    private static IReadOnlyList<DashboardHour> BuildHourlySales(IReadOnlyList<DashboardSaleTime> sales,
        TimeZoneInfo zone, DateTime localNow)
    {
        if (sales.Count == 0) return [];
        var byHour = sales
            .GroupBy(x => HourStart(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), zone)))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(x => x.Total)));
        var first = byHour.Keys.Min();
        var last = new[] { byHour.Keys.Max(), HourStart(localNow) }.Max();
        var result = new List<DashboardHour>();
        for (var hour = first; hour <= last && result.Count < MaximumHourBuckets; hour = hour.AddHours(1))
            result.Add(byHour.TryGetValue(hour, out var value)
                ? new DashboardHour(hour.Hour, value.Count, value.Total)
                : new DashboardHour(hour.Hour, 0, 0m));
        return result;
    }

    private static DateTime HourStart(DateTime local) =>
        new(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified);
}
