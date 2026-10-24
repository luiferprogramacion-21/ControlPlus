using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Reports;

public sealed class FinancialSalesReportService(
    IPermissionChecker permissionChecker,
    IFinancialSalesReportRepository repository) : IFinancialSalesReportService
{
    public async Task<Result<FinancialSalesReport>> GetAsync(
        ActorContext actor, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (actor.UserId == Guid.Empty) return Result.Failure<FinancialSalesReport>(ApplicationError.Unauthorized());
        if (!await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.FinancialReportsRead, cancellationToken))
            return Result.Failure<FinancialSalesReport>(ApplicationError.Forbidden());
        if (from is null || to is null || from > to || to.Value.DayNumber - from.Value.DayNumber > 30)
            return Result.Failure<FinancialSalesReport>(ApplicationError.Validation("El rango from/to es obligatorio, inclusivo y admite hasta 31 días."));

        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null) return Result.Failure<FinancialSalesReport>(ApplicationError.NotFound("la instalación activa"));
        var rows = await repository.GetDailySalesAsync(installationId.Value, from.Value, to.Value, cancellationToken);
        var lookup = rows.ToDictionary(x => (x.Date, x.SaleType));
        var days = new List<DailySalesReport>();
        for (var offset = 0; offset <= to.Value.DayNumber - from.Value.DayNumber; offset++)
        {
            var day = from.Value.AddDays(offset);
            var cash = Amount(day, "CONTADO");
            var credit = Amount(day, "CREDITO");
            days.Add(new DailySalesReport(day, cash, credit, Add(cash, credit)));
        }
        var cashTotal = Sum(days.Select(x => x.Cash));
        var creditTotal = Sum(days.Select(x => x.Credit));
        return Result.Success(new FinancialSalesReport(from.Value, to.Value, days, cashTotal, creditTotal, Add(cashTotal, creditTotal)));

        SalesAmount Amount(DateOnly date, string type) => lookup.TryGetValue((date, type), out var row)
            ? new SalesAmount(row.Transactions, row.Total) : new SalesAmount(0, 0m);
    }

    private static SalesAmount Add(SalesAmount a, SalesAmount b) => new(a.Transactions + b.Transactions, a.Total + b.Total);
    private static SalesAmount Sum(IEnumerable<SalesAmount> values) => values.Aggregate(new SalesAmount(0, 0m), Add);
}
