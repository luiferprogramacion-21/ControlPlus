using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Reports;

public sealed class DailyCashFlowService(IPermissionChecker permissionChecker, IDailyCashFlowRepository repository)
    : IDailyCashFlowService
{
    public async Task<Result<DailyCashFlowReport>> GetAsync(ActorContext actor, DateOnly? from, DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (actor.UserId == Guid.Empty) return Result.Failure<DailyCashFlowReport>(ApplicationError.Unauthorized());
        if (!await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.FinancialReportsRead, cancellationToken))
            return Result.Failure<DailyCashFlowReport>(ApplicationError.Forbidden());
        if (from is null || to is null || from > to || to.Value.DayNumber - from.Value.DayNumber > 30)
            return Result.Failure<DailyCashFlowReport>(ApplicationError.Validation("El rango from/to es obligatorio, inclusivo y admite hasta 31 días."));
        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null) return Result.Failure<DailyCashFlowReport>(ApplicationError.NotFound("la instalación activa"));

        var entries = await repository.GetEntriesAsync(installationId.Value, from.Value, to.Value, cancellationToken);
        var days = new List<DailyCashFlow>();
        for (var offset = 0; offset <= to.Value.DayNumber - from.Value.DayNumber; offset++)
        {
            var date = from.Value.AddDays(offset);
            var dayEntries = entries.Where(x => x.Date == date).ToArray();
            var opening = dayEntries.Where(x => x.Origin == "APERTURA").Sum(x => x.Amount);
            var payments = dayEntries.Where(x => x.Method is not null)
                .GroupBy(x => new { x.Origin, x.Method })
                .Select(g => new CashFlowEntry(date, g.Key.Origin, g.Key.Method, g.Sum(x => x.Amount)))
                .OrderBy(x => x.Origin).ThenBy(x => x.Method).ToArray();
            var manual = dayEntries.Where(x => x.Method is null && x.Origin != "APERTURA")
                .GroupBy(x => x.Origin)
                .Select(g => new CashFlowEntry(date, g.Key, null, g.Sum(x => x.Amount)))
                .OrderBy(x => x.Origin).ToArray();
            days.Add(new DailyCashFlow(date, opening, payments, manual,
                payments.Sum(x => x.Amount), manual.Sum(x => x.Amount)));
        }
        return Result.Success(new DailyCashFlowReport(from.Value, to.Value, days,
            days.Sum(x => x.OpeningBalance), days.Sum(x => x.PaymentTotal), days.Sum(x => x.ManualTotal)));
    }
}
