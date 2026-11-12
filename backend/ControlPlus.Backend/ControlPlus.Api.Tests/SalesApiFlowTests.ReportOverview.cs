using System.Net;
using System.Net.Http.Json;
using ControlPlus.Api.Controllers;
using System.Text;
using ControlPlus.Application.Reports;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SalesOverview_ChangeReconcilesOriginalReturnedAndDeliveredHistoricalCosts()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, scenario.Admin.AccessToken);
        SetBearer(client, scenario.Admin.AccessToken);
        _ = await ReadAsync<VatSettingDto>(await client.PutAsJsonAsync(
            "/api/settings/vat", new UpdateVatSettingRequest(true, 19m, 0)), HttpStatusCode.OK);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "overview-change-origin",
            new ConfirmSaleRequest(null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 1000m, 1000m, null)])), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        SetBearer(client, scenario.Admin.AccessToken);
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "overview-change-confirm", new ConfirmSaleChangeRequest(sale.Version, null,
                [new(Assert.Single(sale.Lines).Id, 1, true)],
                [new(scenario.Products.ResidueA, 1, "MINORISTA")], "AUTO",
                [new("EFECTIVO", 899m, null, null)], null, "Cambio reporte")), HttpStatusCode.Created);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var original = await db.DetalleVenta.AsNoTracking().SingleAsync(x => x.VentaId == sale.Id);
        var delivered = await db.MovimientoInventario.AsNoTracking().SingleAsync(x =>
            x.CambioVentaId == change.Id && x.TipoMovimiento == "CAMBIO_ENTREGA");
        var persistedChange = await db.CambioVenta.AsNoTracking().SingleAsync(x => x.Id == change.Id);
        var changeLines = await db.DetalleCambioVenta.AsNoTracking()
            .Where(x => x.CambioVentaId == change.Id).ToArrayAsync();
        var shift = await db.TurnoCaja.AsNoTracking().SingleAsync(x => x.Id == persistedChange.TurnoCajaId);
        var report = await ReadAsync<SalesOverviewReport>(await client.GetAsync(
            $"/api/reports/sales/overview?from={shift.FechaOperativa:yyyy-MM-dd}&to={shift.FechaOperativa:yyyy-MM-dd}"), HttpStatusCode.OK);
        Assert.Equal(change.Difference, report.Changes.NetTotal);
        Assert.Equal(1, report.Changes.Transactions);
        Assert.Equal(0, report.MissingMovementLines);
        Assert.Equal("COMPLETE", report.CostCompleteness);
        var expectedCost = original.CostoUnitario!.Value * original.Cantidad -
                           original.CostoUnitario.Value + delivered.CostoPromedioAnterior!.Value;
        var includedChangeVat = changeLines.Sum(x => x.TipoDetalle == "ENTREGADO"
            ? x.ValorIva!.Value : -x.ValorIva!.Value);
        var expectedCommercialBase = report.NetSalesTotal - original.ValorIva!.Value - includedChangeVat;
        Assert.All(changeLines, x => Assert.Equal(19m, x.PorcentajeIva));
        Assert.Equal(expectedCost, report.EstimatedCost);
        Assert.Equal(expectedCommercialBase - expectedCost, report.EstimatedMargin);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SalesOverview_UsesPersistedCostsAndAppliedMethods_PdfMatches_AndMissingCostIsNotZero()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        _ = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "overview-mixed-sale",
            new ConfirmSaleRequest(null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 400m, 500m, null),
                 new("TRANSFERENCIA", 600m, null, "OVERVIEW-TRANSFER")])), HttpStatusCode.Created);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<ISalesOverviewRepository>();
        var persisted = await db.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id);
        var line = await db.DetalleVenta.SingleAsync(x => x.VentaId == sale.Id);
        var cost = line.CostoUnitario!.Value * line.Cantidad;
        var day = persisted.FechaOperativa;
        var otherInstallation = await repository.GetAsync(Guid.NewGuid(), day.AddDays(-1), day, day, default);
        Assert.Empty(otherInstallation.Sales);
        Assert.Empty(otherInstallation.Payments);

        var url = $"/api/reports/sales/overview?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}";
        SetBearer(client, scenario.Supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        var report = await ReadAsync<SalesOverviewReport>(await client.GetAsync(url), HttpStatusCode.OK);
        Assert.Equal(new OverviewSales(1, persisted.Total, 0m, persisted.Total), report.Sales);
        Assert.Equal(cost, report.EstimatedCost);
        Assert.Equal(persisted.Total - cost, report.EstimatedMargin);
        Assert.Equal("COMPLETE", report.CostCompleteness);
        Assert.Equal(1000m, report.PaymentMethodsTotal);
        Assert.Equal(400m, Assert.Single(report.PaymentMethods, x => x.Code == "EFECTIVO").Total);
        Assert.Equal(600m, Assert.Single(report.PaymentMethods, x => x.Code == "TRANSFERENCIA").Total);

        using (var pdfResponse = await client.GetAsync($"/api/reports/sales/overview.pdf?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}"))
        {
            Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
            Assert.Equal("application/pdf", pdfResponse.Content.Headers.ContentType?.MediaType);
            var pdf = Encoding.Latin1.GetString(await pdfResponse.Content.ReadAsByteArrayAsync());
            Assert.Contains("Ventas del período", pdf);
            Assert.Contains(SalesOverviewPdf.FormatMoney(persisted.Total), pdf);
            Assert.Contains("Costo de los productos", pdf);
            Assert.Contains(SalesOverviewPdf.FormatMoney(cost), pdf);
            Assert.DoesNotContain("COMPLETE", pdf);
            Assert.DoesNotContain("EFECTIVO", pdf);
        }

        // A sale line without cost is a legacy case: the immutability trigger is disabled only inside this disposable database.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE ventas.detalle_venta DISABLE TRIGGER USER");
        try
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE ventas.detalle_venta SET costo_unitario = NULL WHERE id = {0}", line.Id);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE ventas.detalle_venta ENABLE TRIGGER USER");
        }
        var incomplete = await ReadAsync<SalesOverviewReport>(await client.GetAsync(url), HttpStatusCode.OK);
        Assert.Equal("INCOMPLETE", incomplete.CostCompleteness);
        Assert.Equal(1, incomplete.MissingCostLines);
        Assert.Null(incomplete.EstimatedCost);
        Assert.Null(incomplete.EstimatedMargin);
    }
}
