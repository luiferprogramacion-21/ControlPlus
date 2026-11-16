using ControlPlus.Application.Reports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class DashboardSummaryTests
{
    private static readonly ActorContext Actor = new(Guid.NewGuid(), RoleLevel.Administrador);

    [Fact]
    public async Task Summary_UsesEstablishmentDateAndKeepsSalesSeparateFromPayments()
    {
        var installation = Guid.NewGuid();
        var source = new Source(installation);
        var service = Create(source, new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero));
        var result = await service.GetAsync(Actor, default);
        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 10, 24), result.Value!.OperatingDate);
        Assert.Equal(new SalesAmount(1, 100m), result.Value.CashSales);
        Assert.Equal(new SalesAmount(1, 80m), result.Value.CreditSales);
        Assert.Equal(new SalesAmount(2, 180m), result.Value.TotalSales);
        Assert.Equal(20m, result.Value.OpeningBalance);
        Assert.Equal(60m, result.Value.PaymentTotal);
        Assert.Equal(-5m, result.Value.ManualTotal);
        Assert.Equal(55m, result.Value.NetCashFlow);
        Assert.Equal(installation, source.LastInstallation);
    }

    [Fact]
    public async Task Summary_ReportsTheLastExternalBackupOfTheActiveInstallation()
    {
        var installation = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 15, 20, 30, 0, TimeSpan.Zero);
        var backup = new DashboardExternalBackup(DashboardExternalBackupStates.Failed, at, at.AddMinutes(-30));
        var source = new Source(installation) { ExternalBackup = backup };
        var result = await Create(source, at.AddMinutes(5)).GetAsync(Actor, default);
        Assert.Equal(backup, result.Value!.ExternalBackup);
        Assert.Equal(installation, source.LastBackupInstallation);
    }

    [Fact]
    public async Task Summary_DeniesRevokedPermissionBeforeReadingInstallation()
    {
        var source = new Source(Guid.NewGuid());
        var service = new DashboardSummaryService(new Permission(false), new Clock(DateTimeOffset.UtcNow),
            source, source, source, source);
        var result = await service.GetAsync(Actor, default);
        Assert.Equal("authorization.forbidden", result.Error?.Code);
        Assert.False(source.Read);
    }

    [Theory]
    [InlineData(180, 150, 20)]
    [InlineData(100, 150, -33)]
    [InlineData(150, 150, 0)]
    [InlineData(100, 0, null)]
    public async Task Comparison_UsesTheSameSpanOfYesterdayAndHasNoPercentWithoutPreviousSales(
        int today, int previous, int? expectedPercent)
    {
        var source = new Source(Guid.NewGuid()) { TodayCashTotal = today, TodayCreditTotal = 0, PreviousTotal = previous };
        var now = new DateTimeOffset(2026, 10, 24, 17, 30, 0, TimeSpan.Zero);
        var result = await Create(source, now).GetAsync(Actor, default);
        var comparison = result.Value!.Comparison;
        Assert.Equal(new DateOnly(2026, 10, 23), comparison.PreviousDate);
        Assert.Equal(previous, comparison.PreviousTotal);
        Assert.Equal(expectedPercent, comparison.PercentChange is null ? null : (int)comparison.PercentChange);
        // «Mismo lapso»: el corte de ayer es exactamente ahora menos 24 horas y la fecha consultada es la de ayer.
        Assert.Equal(new DateOnly(2026, 10, 23), source.LastPreviousDate);
        Assert.Equal(now.AddDays(-1).UtcDateTime, source.LastCutoffUtc);
    }

    [Fact]
    public async Task HourlySales_UseLocalHoursFromFirstSaleToNowAndFillEmptyHoursWithZero()
    {
        var source = new Source(Guid.NewGuid())
        {
            SaleTimes =
            [
                new(new DateTime(2026, 10, 24, 13, 20, 0, DateTimeKind.Utc), 100m), // 08:20 en Bogotá
                new(new DateTime(2026, 10, 24, 15, 5, 0, DateTimeKind.Utc), 40m),   // 10:05
                new(new DateTime(2026, 10, 24, 15, 40, 0, DateTimeKind.Utc), 60m)   // 10:40
            ]
        };
        // 12:30 en Bogotá: la última barra es la hora actual aunque todavía no tenga ventas.
        var result = await Create(source, new DateTimeOffset(2026, 10, 24, 17, 30, 0, TimeSpan.Zero)).GetAsync(Actor, default);
        Assert.Equal(
            [(8, 1, 100m), (9, 0, 0m), (10, 2, 100m), (11, 0, 0m), (12, 0, 0m)],
            result.Value!.HourlySales.Select(x => (x.Hour, x.Transactions, x.Total)).ToArray());
    }

    [Fact]
    public async Task HourlySales_AreEmptyWhenThereAreNoSales()
    {
        var result = await Create(new Source(Guid.NewGuid()), new DateTimeOffset(2026, 10, 24, 17, 30, 0, TimeSpan.Zero))
            .GetAsync(Actor, default);
        Assert.Empty(result.Value!.HourlySales);
    }

    [Fact]
    public async Task Alerts_ReportCountersAndTheThreeDayLayawayWindow()
    {
        var source = new Source(Guid.NewGuid()) { LowStock = 7, OverdueCustomers = 3, UpcomingLayaways = 5 };
        var result = await Create(source, new DateTimeOffset(2026, 10, 24, 17, 30, 0, TimeSpan.Zero)).GetAsync(Actor, default);
        Assert.Equal(new DashboardAlerts(7, 3, 5, 3), result.Value!.Alerts);
        // Hoy y los próximos tres días, ambos incluidos; el vencimiento de créditos se mide contra hoy.
        Assert.Equal((new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 27)), source.LastLayawayRange);
        Assert.Equal(new DateOnly(2026, 10, 24), source.LastOverdueDate);
    }

    [Fact]
    public async Task RecentActivity_ComposesTheVisibleNumberAndKeepsTheStateAndTheOrder()
    {
        var source = new Source(Guid.NewGuid())
        {
            RecentSales =
            [
                new(Guid.NewGuid(), "VTA", "001", 148, new DateTime(2026, 10, 24, 19, 26, 0, DateTimeKind.Utc), null, 86500m, "CONTADO", "CONFIRMADA"),
                new(Guid.NewGuid(), "VTA", "001", 147, new DateTime(2026, 10, 24, 19, 18, 0, DateTimeKind.Utc), "María López", 154000m, "CREDITO", "ANULADA")
            ]
        };
        var result = await Create(source, new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.Zero)).GetAsync(Actor, default);
        var activity = result.Value!.RecentActivity;
        Assert.Equal(["VTA-001-148", "VTA-001-147"], activity.Select(x => x.Number).ToArray());
        Assert.Null(activity[0].CustomerName);
        Assert.Equal("María López", activity[1].CustomerName);
        Assert.Equal(("CREDITO", "ANULADA"), (activity[1].SaleType, activity[1].State));
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 19, 26, 0, TimeSpan.Zero), activity[0].OccurredAt);
        Assert.Equal(DashboardRules.RecentActivityLimit, source.LastRecentLimit);
    }

    [Fact]
    public async Task CurrentCash_IsPassedThroughAndIsNullWhenThereIsNoOpenShift()
    {
        var opened = new DateTimeOffset(2026, 10, 24, 13, 2, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 10, 24, 17, 30, 0, TimeSpan.Zero);
        var withShift = await Create(new Source(Guid.NewGuid()) { Cash = new DashboardCash(opened, 1_286_000m) }, now)
            .GetAsync(Actor, default);
        Assert.Equal(new DashboardCash(opened, 1_286_000m), withShift.Value!.CurrentCash);
        var withoutShift = await Create(new Source(Guid.NewGuid()), now).GetAsync(Actor, default);
        Assert.Null(withoutShift.Value!.CurrentCash);
    }

    private static DashboardSummaryService Create(Source source, DateTimeOffset now) =>
        new(new Permission(true), new Clock(now), source, source, source, source);

    private sealed class Permission(bool allowed) : IPermissionChecker
    {
        public Task<bool> HasPermissionAsync(Guid userId, string code, CancellationToken cancellationToken = default)
        {
            Assert.Equal(PermissionCodes.FinancialReportsRead, code);
            return Task.FromResult(allowed);
        }
    }

    private sealed class Clock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }

    private sealed class Source(Guid id) : IDashboardInstallationRepository, IFinancialSalesReportRepository,
        IDailyCashFlowRepository, IDashboardOverviewRepository
    {
        public bool Read { get; private set; }
        public Guid LastInstallation { get; private set; }
        public decimal TodayCashTotal { get; init; } = 100m;
        public decimal TodayCreditTotal { get; init; } = 80m;
        public decimal PreviousTotal { get; init; }
        public IReadOnlyList<DashboardSaleTime> SaleTimes { get; init; } = [];
        public IReadOnlyList<DashboardSaleRow> RecentSales { get; init; } = [];
        public DashboardCash? Cash { get; init; }
        public int LowStock { get; init; }
        public int OverdueCustomers { get; init; }
        public int UpcomingLayaways { get; init; }
        public DateOnly? LastPreviousDate { get; private set; }
        public DateTime? LastCutoffUtc { get; private set; }
        public (DateOnly From, DateOnly To)? LastLayawayRange { get; private set; }
        public DateOnly? LastOverdueDate { get; private set; }
        public int? LastRecentLimit { get; private set; }

        public Task<DashboardInstallation?> GetActiveAsync(CancellationToken cancellationToken)
        {
            Read = true;
            return Task.FromResult<DashboardInstallation?>(new(id, "America/Bogota"));
        }

        public Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SalesReportRow>> GetDailySalesAsync(Guid installationId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            LastInstallation = installationId;
            Assert.Equal(from, to);
            return Task.FromResult<IReadOnlyList<SalesReportRow>>(
                [new(from, "CONTADO", 1, TodayCashTotal), new(from, "CREDITO", 1, TodayCreditTotal)]);
        }

        public Task<IReadOnlyList<CashFlowEntry>> GetEntriesAsync(Guid installationId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            Assert.Equal(id, installationId);
            Assert.Equal(from, to);
            return Task.FromResult<IReadOnlyList<CashFlowEntry>>([
                new(from, "APERTURA", null, 20m),
                new(from, "VENTA_CONTADO", "EFECTIVO", 60m),
                new(from, "GASTO_MANUAL", null, -5m)]);
        }

        public Task<DashboardCash?> GetCurrentCashAsync(CancellationToken cancellationToken) => Task.FromResult(Cash);

        public Task<decimal> GetSalesTotalUntilAsync(Guid installationId, DateOnly operatingDate, DateTime untilUtc, CancellationToken cancellationToken)
        {
            Assert.Equal(id, installationId);
            LastPreviousDate = operatingDate;
            LastCutoffUtc = untilUtc;
            return Task.FromResult(PreviousTotal);
        }

        public Task<IReadOnlyList<DashboardSaleTime>> GetSaleTimesAsync(Guid installationId, DateOnly operatingDate, CancellationToken cancellationToken) =>
            Task.FromResult(SaleTimes);

        public Task<IReadOnlyList<DashboardSaleRow>> GetRecentSalesAsync(Guid installationId, DateOnly operatingDate, int limit, CancellationToken cancellationToken)
        {
            LastRecentLimit = limit;
            return Task.FromResult(RecentSales);
        }

        public Task<int> CountLowStockProductsAsync(CancellationToken cancellationToken) => Task.FromResult(LowStock);

        public Task<int> CountOverdueCreditCustomersAsync(Guid installationId, DateOnly operatingDate, CancellationToken cancellationToken)
        {
            LastOverdueDate = operatingDate;
            return Task.FromResult(OverdueCustomers);
        }

        public Task<int> CountUpcomingLayawaysAsync(Guid installationId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            LastLayawayRange = (from, to);
            return Task.FromResult(UpcomingLayaways);
        }

        public DashboardExternalBackup ExternalBackup { get; init; } =
            new(DashboardExternalBackupStates.NotConfigured, null, null);
        public Guid? LastBackupInstallation { get; private set; }

        public Task<DashboardExternalBackup> GetExternalBackupAsync(Guid installationId, CancellationToken cancellationToken)
        {
            LastBackupInstallation = installationId;
            return Task.FromResult(ExternalBackup);
        }
    }
}
