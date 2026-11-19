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
    public async Task Overview_DeductsSavedChangeVatFromEstimatedGain()
    {
        var repo = new Repository
        {
            Data = new SalesOverviewData(
                [new(From, "CONTADO", 1, 119m)], [new(To, 1, 24m)],
                new(60m, 0, 0), new(10m, 0, 0), new(20m, 0, 0), [], 19m, 4m)
        };
        var result = await new SalesOverviewService(new Checker(), repo).GetAsync(Actor(), From, To, default);
        var report = Assert.IsType<SalesOverviewReport>(result.Value);
        Assert.Equal(143m, report.NetSalesTotal);
        Assert.Equal(50m, report.EstimatedMargin);
        Assert.Equal(41.67m, report.EstimatedMarginPercentage);
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

    private static string RenderPdf(SalesOverviewReport report) =>
        Encoding.Latin1.GetString(SalesOverviewPdf.Render(report));

    private static void AssertValidStructure(string pdf)
    {
        Assert.StartsWith("%PDF-1.4", pdf);
        var xrefStart = long.Parse(pdf[(pdf.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..]
            .Split('\n')[0]);
        Assert.Equal("xref", pdf.Substring((int)xrefStart, 4));
        Assert.EndsWith("%%EOF\n", pdf);
    }

    [Fact]
    public void Pdf_UsesPlainLanguageAndHidesSystemTerms()
    {
        var report = new SalesOverviewReport(From, To, "COP", new(1, 100m, 0, 100m),
            new(1, -20m), 80m, 0m, null, 100m, 40m, 40m, 50m,
            "COMPLETE", 0, 0, [new(From, 100m, 0, 100m), new(To, 0, -20m, -20m)],
            100m, [new("EFECTIVO", "Efectivo", 100m, 100m)]);
        var pdf = RenderPdf(report);
        AssertValidStructure(pdf);

        foreach (var text in new[]
                 {
                     "Reporte de ventas", "Del 04/11/2026 al 05/11/2026", "Generado el", "Ventas del período",
                     "Cambios de productos", "Total vendido", "Venta promedio", "Costo de los productos",
                     "Ganancia estimada", "50 % de las ventas sin IVA", "Ventas por día", "Total del día",
                     "05/11/2026", "Cómo pagaron los clientes", "Medio de pago", "Efectivo",
                     "$ 100", "-$ 20", "$ 80", "$ 40", "100 %",
                     "No hubo ventas en el período anterior para comparar."
                 })
            Assert.Contains(text, pdf);

        foreach (var system in new[]
                 {
                     "COMPLETE", "INCOMPLETE", "EFECTIVO", "(UTC)", "operativ", "2026-11-05", "Ventas originales",
                     "Ventas netas", "Ajuste neto", "Transacciones", "Sin referencia", "MEDIOS DE COBRO"
                 })
            Assert.DoesNotContain(system, pdf);
        Assert.DoesNotContain("Courier", pdf);
        Assert.Contains("/BaseFont /Helvetica-Bold", pdf);
    }

    [Fact]
    public void Pdf_WithMissingCostsExplainsItInsteadOfShowingCodes()
    {
        var report = new SalesOverviewReport(From, To, "COP", new(2, 100m, 0, 100m),
            new(0, 0m), 100m, 50m, 100m, 50m, null, null, null,
            "INCOMPLETE", 2, 1, [new(From, 100m, 0, 100m), new(To, 0, 0, 0)],
            0m, []);
        var pdf = RenderPdf(report);
        AssertValidStructure(pdf);
        Assert.Contains("Atención: no se pudo calcular el costo ni la ganancia.", pdf);
        Assert.Contains("Faltan los costos de 3 productos vendidos.", pdf);
        Assert.Contains("No disponible", pdf);
        Assert.Contains("las ventas subieron 100 %", pdf);
        Assert.Contains("No hubo ventas de contado con pago en este período.", pdf);
        Assert.DoesNotContain("INCOMPLETE", pdf);
        Assert.DoesNotContain("líneas sin costo", pdf);
    }

    [Fact]
    public void Pdf_GroupsCountsAndUsesCommaForDecimalPercentages()
    {
        var report = new SalesOverviewReport(From, To, "COP", new(1234, 1234567m, 0m, 1234567m),
            new(2345, 0m), 1234567m, 100m, 1234.5m, 1000m, null, null, null,
            "INCOMPLETE", 1234, 2, [new(From, 1234567m, 0m, 1234567m)],
            1234567m, [new("EFECTIVO", "Efectivo", 1234567m, 100m)]);

        var pdf = RenderPdf(report);

        AssertValidStructure(pdf);
        Assert.Contains("1.234 ventas", pdf);
        Assert.Contains("2.345 cambios", pdf);
        Assert.Contains("Faltan los costos de 1.236 productos vendidos.", pdf);
        Assert.Contains("las ventas subieron 1.234,5 %", pdf);
        Assert.Contains("$ 1.234.567", pdf);
        Assert.Contains("Página 1 de 1", pdf);
        Assert.Equal("1.234,57 %", SalesOverviewPdf.FormatPercent(1234.567m));
        Assert.Equal("-1.234,5 %", SalesOverviewPdf.FormatPercent(-1234.5m));
    }

    [Fact]
    public void Pdf_WithAFullMonthContinuesOnANewPageWithFooterAndEscapedNames()
    {
        var from = new DateOnly(2026, 10, 1);
        var days = Enumerable.Range(0, 31).Select(i => new OverviewDay(from.AddDays(i), 1000m * i, 0m, 1000m * i)).ToArray();
        var report = new SalesOverviewReport(from, from.AddDays(30), "COP", new(31, 465000m, 0, 465000m),
            new(0, 0m), 465000m, 0m, null, 15000m, 200000m, 265000m, 57m,
            "COMPLETE", 0, 0, days, 465000m,
            [new("NEQUI", "Nequi (QR) \\ prueba", 465000m, 100m)]);
        var pdf = RenderPdf(report);
        AssertValidStructure(pdf);
        Assert.Contains("Página 1 de 2", pdf);
        Assert.Contains("Página 2 de 2", pdf);
        Assert.Contains("/Count 2", pdf);
        Assert.Contains("Nequi \\(QR\\) \\\\ prueba", pdf);
        Assert.Contains("31/10/2026", pdf);
        Assert.Contains("Total del día", pdf);
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
