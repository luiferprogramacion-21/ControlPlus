using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_UsesOriginalNineteenPercentVatForReturnedAndDeliveredLines()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await SetProductVatAsync(setup.Products.ResidueA, 7m);
        SetBearer(client, setup.Admin.AccessToken);
        var originalVat = await ReadAsync<VatSettingDto>(await client.PutAsJsonAsync(
            "/api/settings/vat", new UpdateVatSettingRequest(true, 19m, 0)), HttpStatusCode.OK);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-vat-origin-19",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);

        SetBearer(client, setup.Admin.AccessToken);
        _ = await ReadAsync<VatSettingDto>(await client.PutAsJsonAsync(
            "/api/settings/vat", new UpdateVatSettingRequest(true, 5m, originalVat.Version)),
            HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Admin.AccessToken, setup.Admin.User.Id);
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "change-vat-original-19", new ConfirmSaleChangeRequest(sale.Version, null,
                [new(Assert.Single(sale.Lines).Id, 1, true)],
                [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
                [new("EFECTIVO", 899m, null, null)], null)), HttpStatusCode.Created);

        Assert.Equal(1000m, change.TotalReturned);
        Assert.Equal(101m, change.TotalDelivered);
        Assert.Equal(-899m, change.Difference);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var rows = await db.DetalleCambioVenta.AsNoTracking()
            .Where(x => x.CambioVentaId == change.Id).OrderBy(x => x.TipoDetalle).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        foreach (var row in rows)
        {
            Assert.Equal(19m, row.PorcentajeIva);
            Assert.Equal(decimal.Round(row.SubtotalNeto / 1.19m, 0,
                MidpointRounding.AwayFromZero), row.BaseGravable);
            Assert.Equal(row.SubtotalNeto - row.BaseGravable, row.ValorIva);
            Assert.Equal(row.SubtotalNeto, row.BaseGravable + row.ValorIva);
            Assert.Equal(0m, row.BaseGravable!.Value % 1m);
        }
        Assert.Equal(7m, await db.Producto.Where(x => x.Id == setup.Products.ResidueA)
            .Select(x => x.PorcentajeIva).SingleAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_OriginalSaleWithoutVatStaysWithoutBreakdownAfterVatIsEnabled()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await SetProductVatAsync(setup.Products.ResidueA, 7m);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-vat-origin-off",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);

        SetBearer(client, setup.Admin.AccessToken);
        _ = await ReadAsync<VatSettingDto>(await client.PutAsJsonAsync(
            "/api/settings/vat", new UpdateVatSettingRequest(true, 19m, 0)), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Admin.AccessToken, setup.Admin.User.Id);
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "change-vat-origin-off-confirm", new ConfirmSaleChangeRequest(sale.Version, null,
                [new(Assert.Single(sale.Lines).Id, 1, true)],
                [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
                [new("EFECTIVO", 899m, null, null)], null)), HttpStatusCode.Created);

        Assert.Equal((1000m, 101m, -899m),
            (change.TotalReturned, change.TotalDelivered, change.Difference));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var rows = await db.DetalleCambioVenta.AsNoTracking()
            .Where(x => x.CambioVentaId == change.Id).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Null(row.PorcentajeIva);
            Assert.Null(row.BaseGravable);
            Assert.Null(row.ValorIva);
        });
    }

    private async Task SetProductVatAsync(Guid productId, decimal percentage)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await db.Producto.SingleAsync(x => x.Id == productId);
        product.PorcentajeIva = percentage;
        await db.SaveChangesAsync();
    }
}
