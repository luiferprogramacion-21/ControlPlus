using System.Text;
using ControlPlus.Api.Controllers;
using ControlPlus.Application.Reports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class SalesOverviewTests
{
    private static readonly DateOnly From = new(2026, 11, 4);
    private static readonly DateOnly To = new(2026, 11, 5);

    [Fact]
    public async Task Overview_UsesSavedVatForEstimatedGainAndPreservesGrossSales()
    {
        var repo = new Repository
        {
            Data = new SalesOverviewData(
                [new(From, "CONTADO", 1, 119m)], [],
                new(60m, 0, 0), new(0m, 0, 0), new(0m, 0, 0), [], 19m)
        };
        var result = await new SalesOverviewService(new Checker(), repo).GetAsync(Actor(), From, To, default);
        var report = Assert.IsType<SalesOverviewReport>(result.Value);
        Assert.Equal(119m, report.Sales.Total);
        Assert.Equal(40m, report.EstimatedMargin);
        Assert.Equal(40m, report.EstimatedMarginPercentage);
    }

    [Fact]
    public async Task Overview_SeparatesChangesByShiftDateAndUsesHistoricalCostsAndAppliedPayments()
    {
        var repo = new Repository
        {
            Data = new SalesOverviewData(
                [new(new(2026, 11, 2), "CONTADO", 1, 40m),
                 new(From, "CONTADO", 2, 120m), new(From, "CREDITO", 1, 80m)],
                [new(To, 1, -20m)],
                new(100m, 0, 0), new(12m, 0, 0), new(8m, 0, 0),
                [new("EFECTIVO", "Efectivo", 50m), new("TRANSFERENCIA", "Transferencia", 70m)])
        };
        var result = await new SalesOverviewService(new Checker(), repo).GetAsync(Actor(), From, To, default);

        Assert.True(result.IsSuccess);
        var report = result.Value!;
        Assert.Equal(200m, report.Sales.Total);
        Assert.Equal(3, report.Sales.Transactions);
        Assert.Equal(-20m, report.Changes.NetTotal);
        Assert.Equal(180m, report.NetSalesTotal);
        Assert.Equal(96m, report.EstimatedCost);
        Assert.Equal(84m, report.EstimatedMargin);
        Assert.Equal(46.67m, report.EstimatedMarginPercentage);
        Assert.Equal(40m, report.PreviousSalesTotal);
        Assert.Equal(400m, report.SalesVariationPercentage);
        Assert.Equal(2, report.Days.Count);
        Assert.Equal(new OverviewDay(To, 0, -20m, -20m), report.Days[1]);
        Assert.Equal(120m, report.PaymentMethodsTotal);
        Assert.Equal("TRANSFERENCIA", report.PaymentMethods[0].Code);
        Assert.Equal(58.33m, report.PaymentMethods[0].Percentage);
        Assert.Equal(repo.InstallationId, repo.RequestedInstallationId);
        Assert.Equal(new DateOnly(2026, 11, 2), repo.RequestedPreviousFrom);
    }

    [Fact]
    public async Task Overview_MissingHistoricalCostDoesNotPublishPartialMargin()
    {
        var repo = new Repository
        {
            Data = EmptyData() with
            {
                Sales = [new(From, "CONTADO", 1, 100m)],
                OriginalCost = new(50m, 1, 0),
                DeliveredCost = new(10m, 0, 1)
            }
        };
        var result = await new SalesOverviewService(new Checker(), repo).GetAsync(Actor(), From, To, default);
        var report = Assert.IsType<SalesOverviewReport>(result.Value);
        Assert.Equal("INCOMPLETE", report.CostCompleteness);
        Assert.Equal(1, report.MissingCostLines);
        Assert.Equal(1, report.MissingMovementLines);
        Assert.Null(report.EstimatedCost);
        Assert.Null(report.EstimatedMargin);
        Assert.Null(report.EstimatedMarginPercentage);
        Assert.Null(report.SalesVariationPercentage);
    }

    [Fact]
    public async Task Overview_RequiresBothCurrentPermissionsBeforeReadingRepository()
    {
        var repo = new Repository();
        var checker = new Checker(PermissionCodes.ProductCostsRead);
        var result = await new SalesOverviewService(checker, repo).GetAsync(Actor(), From, To, default);
        Assert.Equal("authorization.forbidden", result.Error?.Code);
        Assert.Null(repo.RequestedInstallationId);
        Assert.Contains(PermissionCodes.ProductCostsRead, checker.Checked);
    }

    [Theory]
    [InlineData("2026-11-05", "2026-11-04")]
    [InlineData("2026-10-01", "2026-11-01")]
    public async Task Overview_RejectsInvalidDateRange(string from, string to)
    {
        var repo = new Repository();
        var result = await new SalesOverviewService(new Checker(), repo).GetAsync(Actor(),
            DateOnly.Parse(from), DateOnly.Parse(to), default);
        Assert.Equal("validation.failed", result.Error?.Code);
        Assert.Null(repo.RequestedInstallationId);
    }

    [Fact]
    public void Pdf_ContainsApprovedMetricsAndValidCrossReferenceOffsets()
    {
        var report = new SalesOverviewReport(From, To, "COP", new(1, 100m, 0, 100m),
            new(1, -20m), 80m, 0m, null, 100m, 40m, 40m, 50m,
            "COMPLETE", 0, 0, [new(From, 100m, 0, 100m), new(To, 0, -20m, -20m)],
            100m, [new("EFECTIVO", "Efectivo", 100m, 100m)]);
        var bytes = SalesOverviewPdf.Render(report);
        var pdf = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("Costo productos: 40", pdf);
        Assert.Contains("Ganancia estimada: 40", pdf);
        Assert.Contains("MEDIOS DE COBRO INMEDIATO", pdf);
        Assert.Contains("2026-11-05", pdf);
        var xrefStart = long.Parse(pdf[(pdf.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..]
            .Split('\n')[0]);
        Assert.Equal("xref", pdf.Substring((int)xrefStart, 4));
        Assert.EndsWith("%%EOF\n", pdf);
    }

    private static SalesOverviewData EmptyData() => new([], [], new(0, 0, 0),
        new(0, 0, 0), new(0, 0, 0), []);
    private static ActorContext Actor() => new(Guid.NewGuid(), RoleLevel.Administrador);

    private sealed class Checker(params string[] revoked) : IPermissionChecker
    {
        public List<string> Checked { get; } = [];
        public Task<bool> HasPermissionAsync(Guid userId, string permissionCode,
            CancellationToken cancellationToken = default)
        {
            Checked.Add(permissionCode);
            return Task.FromResult(!revoked.Contains(permissionCode));
        }
    }

    private sealed class Repository : ISalesOverviewRepository
    {
        public Guid InstallationId { get; } = Guid.NewGuid();
        public Guid? RequestedInstallationId { get; private set; }
        public DateOnly? RequestedPreviousFrom { get; private set; }
        public SalesOverviewData Data { get; init; } = EmptyData();
        public Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(InstallationId);
        public Task<SalesOverviewData> GetAsync(Guid installationId, DateOnly previousFrom,
            DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            RequestedInstallationId = installationId;
            RequestedPreviousFrom = previousFrom;
            return Task.FromResult(Data);
        }
    }
}
