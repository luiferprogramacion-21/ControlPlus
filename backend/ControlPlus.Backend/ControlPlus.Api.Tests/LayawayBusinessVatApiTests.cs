using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class LayawayCreateApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task Layaway_CreatedWithVatOffKeepsZeroVatWhenSettingIsEnabledAndItIsCancelled()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await SetLayawayProductVatAsync(scenario.ProductId, 7m);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-vat-off-create", Request(scenario,
                [new("EFECTIVO", 201m, 201m, null)])), HttpStatusCode.Created);
        var saved = await ReadLayawayVatAsync(created.Id);
        Assert.Equal(new LayawayVatState(true, 0m, 1001m, 0m, 1001m, 0m), saved);

        SetBearer(client, scenario.Admin.AccessToken);
        _ = await ReadAsync<VatSettingDto>(await client.PutAsJsonAsync(
            "/api/settings/vat", new UpdateVatSettingRequest(true, 19m, 0)), HttpStatusCode.OK);
        var cancelled = await ReadAsync<LayawayCancellationDto>(await PostCancellationAsync(client,
            created.Id, "layaway-vat-off-cancel",
            CancellationRequest(created.Version, 0m, 0m, 0m) with { Authorization = null }),
            HttpStatusCode.OK);
        Assert.Equal("ANULADO", cancelled.State);
        Assert.Equal(saved, await ReadLayawayVatAsync(created.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Layaway_CreatedWithVatOnKeepsSavedVatThroughLaterPaymentAndDelivery()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await SetLayawayProductVatAsync(scenario.ProductId, 7m);
        SetBearer(client, scenario.Admin.AccessToken);
        var enabled = await ReadAsync<VatSettingDto>(await client.PutAsJsonAsync(
            "/api/settings/vat", new UpdateVatSettingRequest(true, 19m, 0)), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-vat-on-create", Request(scenario,
                [new("EFECTIVO", 201m, 201m, null)])), HttpStatusCode.Created);
        var saved = await ReadLayawayVatAsync(created.Id);
        Assert.Equal(new LayawayVatState(true, 19m, 841m, 160m, 841m, 160m), saved);

        SetBearer(client, scenario.Admin.AccessToken);
        _ = await ReadAsync<VatSettingDto>(await client.PutAsJsonAsync(
            "/api/settings/vat", new UpdateVatSettingRequest(false, null, enabled.Version)),
            HttpStatusCode.OK);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var paid = await ReadAsync<LayawayPaymentOperationDto>(await PostPaymentAsync(client,
            created.Id, "layaway-vat-on-payment", new ApplyLayawayPaymentRequest(created.Version,
                [new("EFECTIVO", 800m, 800m, null)])), HttpStatusCode.OK);
        Assert.Equal("PAGADO", paid.State);
        Assert.Equal(saved, await ReadLayawayVatAsync(created.Id));

        var delivered = await ReadAsync<LayawayDeliveryDto>(await PostDeliveryAsync(client,
            created.Id, "layaway-vat-on-delivery", paid.Version), HttpStatusCode.OK);
        Assert.Equal("ENTREGADO", delivered.State);
        Assert.Equal(saved, await ReadLayawayVatAsync(created.Id));
    }

    private async Task SetLayawayProductVatAsync(Guid productId, decimal percentage)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await db.Producto.SingleAsync(x => x.Id == productId);
        product.PorcentajeIva = percentage;
        await db.SaveChangesAsync();
    }

    private async Task<LayawayVatState> ReadLayawayVatAsync(Guid layawayId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == layawayId);
        var line = await db.DetalleApartado.AsNoTracking().SingleAsync(x => x.ApartadoId == layawayId);
        return new(layaway.IvaDiscriminadoCompleto, line.PorcentajeIva,
            line.BaseGravable, line.ValorIva, layaway.BaseGravableTotal,
            layaway.ImpuestoIncluidoTotal);
    }

    private sealed record LayawayVatState(bool Complete, decimal? Percentage,
        decimal? LineBase, decimal? LineVat, decimal? TotalBase, decimal? TotalVat);
}
