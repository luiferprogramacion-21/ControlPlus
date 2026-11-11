using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Reports;

public sealed record OverviewSales(long Transactions, decimal CashTotal, decimal CreditTotal, decimal Total);
public sealed record OverviewChanges(long Transactions, decimal NetTotal);
public sealed record OverviewDay(DateOnly Date, decimal SalesTotal, decimal ChangesNetTotal, decimal NetSalesTotal);
public sealed record OverviewPaymentMethod(string Code, string Name, decimal Total, decimal Percentage);
public sealed record SalesOverviewReport(DateOnly From, DateOnly To, string Currency,
    OverviewSales Sales, OverviewChanges Changes, decimal NetSalesTotal, decimal PreviousSalesTotal,
    decimal? SalesVariationPercentage, decimal? AverageTicket, decimal? EstimatedCost,
    decimal? EstimatedMargin, decimal? EstimatedMarginPercentage, string CostCompleteness,
    long MissingCostLines, long MissingMovementLines, IReadOnlyList<OverviewDay> Days,
    decimal PaymentMethodsTotal, IReadOnlyList<OverviewPaymentMethod> PaymentMethods);

public sealed record OverviewSaleRow(DateOnly Date, string Type, long Transactions, decimal Total);
public sealed record OverviewChangeRow(DateOnly Date, long Transactions, decimal NetTotal);
public sealed record OverviewCostRow(decimal KnownCost, long MissingCostLines, long MissingMovementLines);
public sealed record OverviewPaymentRow(string Code, string Name, decimal Total);
public sealed record SalesOverviewData(IReadOnlyList<OverviewSaleRow> Sales,
    IReadOnlyList<OverviewChangeRow> Changes, OverviewCostRow OriginalCost,
    OverviewCostRow ReturnedCost, OverviewCostRow DeliveredCost,
    IReadOnlyList<OverviewPaymentRow> Payments, decimal IncludedVat = 0m);

public interface ISalesOverviewRepository
{
    Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken);
    Task<SalesOverviewData> GetAsync(Guid installationId, DateOnly previousFrom,
        DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public interface ISalesOverviewService
{
    Task<Result<SalesOverviewReport>> GetAsync(ActorContext actor, DateOnly? from,
        DateOnly? to, CancellationToken cancellationToken);
}

public sealed class SalesOverviewService(IPermissionChecker permissions,
    ISalesOverviewRepository repository) : ISalesOverviewService
{
    public async Task<Result<SalesOverviewReport>> GetAsync(ActorContext actor,
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (actor.UserId == Guid.Empty)
            return Result.Failure<SalesOverviewReport>(ApplicationError.Unauthorized());
        if (!await permissions.HasPermissionAsync(actor.UserId, PermissionCodes.FinancialReportsRead, cancellationToken) ||
            !await permissions.HasPermissionAsync(actor.UserId, PermissionCodes.ProductCostsRead, cancellationToken))
            return Result.Failure<SalesOverviewReport>(ApplicationError.Forbidden());
        if (from is null || to is null || from > to || to.Value.DayNumber - from.Value.DayNumber > 30)
            return Result.Failure<SalesOverviewReport>(ApplicationError.Validation(
                "El rango from/to es obligatorio, inclusivo y admite hasta 31 días."));

        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null)
            return Result.Failure<SalesOverviewReport>(ApplicationError.NotFound("la instalación activa"));
        var length = to.Value.DayNumber - from.Value.DayNumber + 1;
        DateOnly previousFrom;
        try { previousFrom = from.Value.AddDays(-length); }
        catch (ArgumentOutOfRangeException)
        {
            return Result.Failure<SalesOverviewReport>(ApplicationError.Validation("El período anterior está fuera del calendario admitido."));
        }
        var data = await repository.GetAsync(installationId.Value, previousFrom, from.Value, to.Value, cancellationToken);
        var currentSales = data.Sales.Where(x => x.Date >= from.Value).ToArray();
        var previousSalesTotal = data.Sales.Where(x => x.Date < from.Value).Sum(x => x.Total);
        var cash = currentSales.Where(x => x.Type == "CONTADO").Sum(x => x.Total);
        var credit = currentSales.Where(x => x.Type == "CREDITO").Sum(x => x.Total);
        var salesTotal = cash + credit;
        var transactions = currentSales.Sum(x => x.Transactions);
        var changesTotal = data.Changes.Sum(x => x.NetTotal);
        var net = salesTotal + changesTotal;
        var missingCost = data.OriginalCost.MissingCostLines + data.ReturnedCost.MissingCostLines +
                          data.DeliveredCost.MissingCostLines;
        var missingMovement = data.OriginalCost.MissingMovementLines + data.ReturnedCost.MissingMovementLines +
                              data.DeliveredCost.MissingMovementLines;
        decimal? cost = missingCost + missingMovement == 0
            ? data.OriginalCost.KnownCost - data.ReturnedCost.KnownCost + data.DeliveredCost.KnownCost : null;
        var commercialBase = net - data.IncludedVat;
        decimal? margin = cost is decimal completeCost ? commercialBase - completeCost : null;
        var salesByDate = currentSales.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.Sum(y => y.Total));
        var changesByDate = data.Changes.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.Sum(y => y.NetTotal));
        var days = Enumerable.Range(0, length).Select(offset =>
        {
            var date = from.Value.AddDays(offset);
            salesByDate.TryGetValue(date, out var saleAmount);
            changesByDate.TryGetValue(date, out var changeAmount);
            return new OverviewDay(date, saleAmount, changeAmount, saleAmount + changeAmount);
        }).ToArray();
        var methodsTotal = data.Payments.Sum(x => x.Total);
        var methods = data.Payments.OrderByDescending(x => x.Total).ThenBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => new OverviewPaymentMethod(x.Code, x.Name, x.Total,
                methodsTotal == 0 ? 0 : Round(100m * x.Total / methodsTotal))).ToArray();
        return Result.Success(new SalesOverviewReport(from.Value, to.Value, "COP",
            new OverviewSales(transactions, cash, credit, salesTotal),
            new OverviewChanges(data.Changes.Sum(x => x.Transactions), changesTotal), net,
            previousSalesTotal, previousSalesTotal == 0 ? null : Round(100m * (salesTotal - previousSalesTotal) / previousSalesTotal),
            transactions == 0 ? null : salesTotal / transactions,
            cost, margin, commercialBase == 0 || margin is null ? null : Round(100m * margin.Value / commercialBase),
            cost is null ? "INCOMPLETE" : "COMPLETE", missingCost, missingMovement,
            days, methodsTotal, methods));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
