using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Inventory.Contracts;
using ControlPlus.Application.Purchases.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class PurchasesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task CorrectReceipt_ReversesInventoryAndRestoresOrderPendingQuantity()
    {
        using var client = Client();
        var setup = await CreateReceivedPurchaseAsync(client, "CORRECTION-REVERSIBLE", 2);
        var preparation = await ReadAsync<PurchaseCorrectionPreparationDto>(await client.GetAsync(
            $"/api/purchases/{setup.PurchaseId}/correction-preparation"), HttpStatusCode.OK);
        Assert.True(preparation.CanReverse);
        Assert.Null(preparation.BlockReason);
        Assert.Equal(2, preparation.UnitsToRemove);
        var reason = await CorrectionReasonAsync(client);
        var corrected = await ReadAsync<PurchaseDto>(await PostPurchaseCorrectionAsync(client,
            setup.PurchaseId, "purchase-receipt-correction", new CancelPurchaseRequest(
                preparation.PurchaseVersion, reason.Id, "La recepción se registró dos veces", null,
                preparation.OrderVersion)), HttpStatusCode.OK);

        Assert.Equal("ANULADA", corrected.Status);
        var order = await ReadJsonAsync(await client.GetAsync($"/api/purchase-orders/{setup.OrderId}"),
            HttpStatusCode.OK);
        Assert.Equal("ENVIADO", order.GetProperty("status").GetString());
        Assert.Equal(0, Assert.Single(order.GetProperty("lines").EnumerateArray())
            .GetProperty("receivedQuantity").GetInt32());
        Assert.Equal(0, (await StateAsync(setup.Product.Id)).Stock);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var evidence = await db.Set<CorreccionRecepcion>().AsNoTracking().SingleAsync();
        Assert.Equal(setup.PurchaseId, evidence.CompraId);
        Assert.Equal(setup.OrderId, evidence.PedidoCompraId);
        Assert.Equal(preparation.OrderVersion!.Value + 1, evidence.VersionPedidoPosterior);
        var originalMovementId = await db.MovimientoInventario.Where(x => x.CompraId == setup.PurchaseId)
            .Select(x => x.Id).SingleAsync();
        Assert.Equal(1, await db.MovimientoInventario.CountAsync(x =>
            x.MovimientoRevertidoId == originalMovementId && x.TipoMovimiento == "ANULACION_COMPRA"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CorrectReceipt_WithLaterInventoryMovement_ExplainsBlockAndPreservesReceipt()
    {
        using var client = Client();
        var setup = await CreateReceivedPurchaseAsync(client, "CORRECTION-LATER", 2);
        var product = await ReadAsync<ProductDto>(await client.GetAsync($"/api/products/{setup.Product.Id}"),
            HttpStatusCode.OK);
        var adjustmentReason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await client.GetAsync("/api/inventory/adjustment-reasons"), HttpStatusCode.OK))
            .Single(x => x.Code == "CONTEO_FISICO");
        using (var adjustment = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/adjustments")
               { Content = JsonContent.Create(new CreateInventoryAdjustmentRequest(product.Id, 1,
                   adjustmentReason.Id, product.Version, "Conteo posterior a la recepción", null)) })
        {
            adjustment.Headers.Add("Idempotency-Key", "purchase-correction-later-adjustment");
            _ = await ReadAsync<InventoryMovementDto>(await client.SendAsync(adjustment), HttpStatusCode.Created);
        }
        var preparation = await ReadAsync<PurchaseCorrectionPreparationDto>(await client.GetAsync(
            $"/api/purchases/{setup.PurchaseId}/correction-preparation"), HttpStatusCode.OK);
        Assert.False(preparation.CanReverse);
        Assert.Contains("movimientos posteriores", preparation.BlockReason, StringComparison.OrdinalIgnoreCase);
        var reason = await CorrectionReasonAsync(client);
        var before = await StateAsync(product.Id);
        await ProblemAsync(await PostPurchaseCorrectionAsync(client, setup.PurchaseId,
            "purchase-receipt-correction-blocked", new CancelPurchaseRequest(preparation.PurchaseVersion,
                reason.Id, "Intento bloqueado por movimiento posterior", null, preparation.OrderVersion)),
            HttpStatusCode.Conflict);
        Assert.Equal(before, await StateAsync(product.Id));
        var purchase = await ReadJsonAsync(await client.GetAsync($"/api/purchases/{setup.PurchaseId}"),
            HttpStatusCode.OK);
        Assert.Equal("CONFIRMADA", purchase.GetProperty("status").GetString());
    }

    private async Task<ReceivedPurchaseSetup> CreateReceivedPurchaseAsync(HttpClient client,
        string code, int quantity)
    {
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, $"Proveedor {code}");
        var product = await ProductAsync(client, supplier.Id, code, null);
        var order = await ReadJsonAsync(await client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = supplier.Id,
            lines = new[] { new { productId = product.Id, requestedQuantity = quantity, estimatedUnitCost = 10m } }
        }), HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();
        var sent = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{orderId}/send",
            new { expectedVersion = 1 }), HttpStatusCode.OK);
        var lineId = Assert.Single(sent.GetProperty("lines").EnumerateArray()).GetProperty("id").GetGuid();
        var receipt = await ReadJsonAsync(await ReceiveOrderAsync(client, orderId,
            $"receipt-{code.ToLowerInvariant()}", new
            {
                expectedOrderVersion = 2, invoiceDate = "2026-11-20", vatMode = "NO_DISCRIMINADO",
                lines = new[] { new { orderLineId = lineId, productId = product.Id, quantity, unitCost = 10m } }
            }), HttpStatusCode.Created);
        return new ReceivedPurchaseSetup(orderId,
            receipt.GetProperty("purchase").GetProperty("id").GetGuid(), product);
    }

    private static async Task<HttpResponseMessage> PostPurchaseCorrectionAsync(HttpClient client,
        Guid purchaseId, string key, CancelPurchaseRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"/api/purchases/{purchaseId}/correct-receipt") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private static async Task<PurchaseCancellationReasonDto> CorrectionReasonAsync(HttpClient client) =>
        (await ReadAsync<PurchaseCancellationReasonDto[]>(
            await client.GetAsync("/api/purchases/cancellation-reasons"), HttpStatusCode.OK))
        .First(x => x.Code != "OTRO");

    private sealed record ReceivedPurchaseSetup(Guid OrderId, Guid PurchaseId, ProductDto Product);
}
