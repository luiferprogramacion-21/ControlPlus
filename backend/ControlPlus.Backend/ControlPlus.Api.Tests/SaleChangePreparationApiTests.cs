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
    public async Task SaleChangePolicy_AffectsFutureSalesAndPreservesHistoricalDeadlines()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var prior = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-policy-old-sale",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        Assert.Equal(prior.OperatingDate.AddDays(15), prior.ChangeDeadline);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/sales/change-policy")).StatusCode);

        SetBearer(client, setup.Admin.AccessToken);
        var policy = await ReadAsync<SaleChangePolicyDto>(
            await client.GetAsync("/api/sales/change-policy"), HttpStatusCode.OK);
        Assert.Equal(15, policy.Days);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/sales/change-policy",
            new UpdateSaleChangePolicyRequest(0, policy.Version)), HttpStatusCode.BadRequest);
        var updated = await ReadAsync<SaleChangePolicyDto>(await client.PutAsJsonAsync(
            "/api/sales/change-policy", new UpdateSaleChangePolicyRequest(20, policy.Version)),
            HttpStatusCode.OK);
        Assert.Equal(20, updated.Days);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/sales/change-policy",
            new UpdateSaleChangePolicyRequest(21, policy.Version)), HttpStatusCode.Conflict);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var next = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-policy-new-sale",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        Assert.Equal(next.OperatingDate.AddDays(20), next.ChangeDeadline);
        Assert.Equal(prior.OperatingDate.AddDays(15), (await ReadAsync<SaleChangePreparationDto>(
            await client.GetAsync($"/api/sales/{prior.Id}/change-preparation"), HttpStatusCode.OK)).ChangeDeadline);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChangePreparation_FindsVisibleSaleChecksReadScopeAndLeavesFinancialRowsIntact()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-read-sale",
            CashRequest(scenario.Products.Standard, 2, 2000m)), HttpStatusCode.Created);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var payments = await db.PagoVenta.CountAsync();
        var cash = await db.MovimientoCaja.CountAsync();
        var stock = (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual;
        var inventory = await db.MovimientoInventario.CountAsync();

        var byNumber = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync(
            $"/api/sales/change-search?visibleNumber={Uri.EscapeDataString(sale.VisibleNumber)}"), HttpStatusCode.OK);
        var byId = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
        Assert.Equal(byNumber.SaleId, byId.SaleId);
        Assert.Equal(byNumber.VisibleNumber, byId.VisibleNumber);
        Assert.Equal(byNumber.Lines.Select(x => x.RemainingQuantity),
            byId.Lines.Select(x => x.RemainingQuantity));
        Assert.Equal(sale.ChangeDeadline, byId.ChangeDeadline);
        Assert.True(byId.WithinDeadline);
        Assert.True(byId.CanStartChange);
        Assert.Equal(2, Assert.Single(byId.Lines).RemainingQuantity);
        Assert.Equal(payments, await db.PagoVenta.CountAsync());
        Assert.Equal(cash, await db.MovimientoCaja.CountAsync());
        Assert.Equal(stock, (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual);
        Assert.Equal(inventory, await db.MovimientoInventario.CountAsync());

        SetBearer(client, scenario.SecondCashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/sales/{sale.Id}/change-preparation")).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(
            $"/api/sales/{sale.Id}/change-preparation")).StatusCode);
    }
}
