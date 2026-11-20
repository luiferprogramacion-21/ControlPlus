using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class LayawayCreateApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task CancelLayawayPayment_ReopensBalanceAndPreservesCorrectionTrail()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        SetBearer(client, scenario.Admin.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-correction-payment-origin",
            Request(scenario, [new("EFECTIVO", 201m, 201m, null)]) with { Authorization = null }),
            HttpStatusCode.Created);
        var detail = await ReadAsync<LayawayDetailDto>(await client.GetAsync($"/api/layaways/{created.Id}"),
            HttpStatusCode.OK);
        var payment = Assert.Single(detail.Payments);
        var reasons = await ReadAsync<IReadOnlyCollection<LayawayPaymentCancellationReasonDto>>(
            await client.GetAsync("/api/layaways/payment-cancellation-reasons"), HttpStatusCode.OK);
        var reason = reasons.First(x => x.Code != "OTRO");
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"/api/layaways/{created.Id}/payments/{payment.Id}/cancel")
        {
            Content = JsonContent.Create(new CancelLayawayPaymentRequest(created.Version, reason.Id,
                "Abono aplicado al apartado equivocado", null))
        };
        message.Headers.Add("Idempotency-Key", "layaway-correction-payment-cancel");
        var result = await ReadAsync<CancelLayawayPaymentDto>(await client.SendAsync(message), HttpStatusCode.OK);

        Assert.Equal(created.Total, result.Balance);
        Assert.Equal("ACTIVO", result.State);
        Assert.Equal(created.Version + 1, result.Version);
        Assert.Equal(201m, result.ReversedAmount);
        Assert.Contains(payment.Id, result.CancelledPaymentIds);
        var after = await ReadAsync<LayawayDetailDto>(await client.GetAsync($"/api/layaways/{created.Id}"),
            HttpStatusCode.OK);
        Assert.Equal("ANULADO", Assert.Single(after.Payments).State);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.MovimientoCaja.CountAsync(x => x.Tipo == "REVERSO" && x.CategoriaMovimiento == "REVERSO_PAGO"));
        Assert.Equal(1, await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "ANULACION_PAGO_APARTADO"));
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.Accion == "LAYAWAYPAYMENTCANCELLED"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CorrectLayawayProducts_ReplacesActiveLineAndUpdatesReservationAndBalance()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false, stock: 5);
        SetBearer(client, scenario.Admin.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-correction-products-origin",
            Request(scenario, [new("EFECTIVO", 201m, 201m, null)]) with { Authorization = null }),
            HttpStatusCode.Created);
        var request = new CorrectLayawayProductsRequest(created.Version,
            "La cantidad registrada no correspondía a la separada",
            [new(scenario.ProductId, 2)], [], null);
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"/api/layaways/{created.Id}/product-corrections") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", "layaway-correction-products-apply");
        var correction = await ReadAsync<LayawayCorrectionDto>(await client.SendAsync(message), HttpStatusCode.OK);

        Assert.Equal(1001m, correction.PreviousTotal);
        Assert.Equal(2002m, correction.Total);
        Assert.Equal(800m, correction.PreviousBalance);
        Assert.Equal(1801m, correction.Balance);
        Assert.Equal(0m, correction.Refunded);
        var detail = await ReadAsync<LayawayDetailDto>(await client.GetAsync($"/api/layaways/{created.Id}"),
            HttpStatusCode.OK);
        Assert.Equal(2, Assert.Single(detail.Lines).Quantity);
        Assert.Equal(1801m, detail.Layaway.Balance);
        var history = await ReadAsync<LayawayCorrectionDto[]>(await client.GetAsync(
            $"/api/layaways/{created.Id}/product-corrections"), HttpStatusCode.OK);
        Assert.Equal(correction.Id, Assert.Single(history).Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(2, await db.Producto.Where(x => x.Id == scenario.ProductId).Select(x => x.StockReservado).SingleAsync());
        Assert.Equal(2, await db.DetalleApartado.CountAsync(x => x.ApartadoId == created.Id));
        Assert.Single(await db.CorreccionApartado.AsNoTracking().ToArrayAsync());
    }
}
