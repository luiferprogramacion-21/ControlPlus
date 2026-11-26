using System.Net;
using System.Net.Http.Json;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class PurchasesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task PurchaseOrder_InvalidDraftEdits_PreserveLinesNumberingAndStock()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor del pedido que se conserva");
        var otherSupplier = await SupplierAsync(client, "Proveedor de producto ajeno al pedido");
        var ownProduct = await ProductAsync(client, supplier.Id, "ORD-INVALID-OWN", null);
        var foreignProduct = await ProductAsync(client, otherSupplier.Id, "ORD-INVALID-FOREIGN", null);

        var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = supplier.Id,
            lines = new[] { new { productId = ownProduct.Id, requestedQuantity = 2, estimatedUnitCost = 10m } }
        }), HttpStatusCode.Created);
        var orderId = created.GetProperty("id").GetGuid();
        var path = $"/api/purchase-orders/{orderId}";
        var numbering = await ReadJsonAsync(await client.GetAsync("/api/purchase-orders/numbering"), HttpStatusCode.OK);
        var ownStock = await StateAsync(ownProduct.Id);
        var foreignStock = await StateAsync(foreignProduct.Id);

        await ProblemAsync(await client.PutAsJsonAsync(path, new
        {
            expectedVersion = 1,
            lines = new[]
            {
                new { productId = ownProduct.Id, requestedQuantity = 3, estimatedUnitCost = 11m },
                new { productId = ownProduct.Id, requestedQuantity = 4, estimatedUnitCost = 12m }
            }
        }), HttpStatusCode.BadRequest);
        AssertSameOrderBusinessState(created,
            await ReadJsonAsync(await client.GetAsync(path), HttpStatusCode.OK));

        await ProblemAsync(await client.PutAsJsonAsync(path, new
        {
            expectedVersion = 1,
            lines = new[] { new { productId = foreignProduct.Id, requestedQuantity = 5, estimatedUnitCost = 13m } }
        }), HttpStatusCode.Conflict);
        AssertSameOrderBusinessState(created,
            await ReadJsonAsync(await client.GetAsync(path), HttpStatusCode.OK));
        Assert.Equal(numbering.GetRawText(),
            (await ReadJsonAsync(await client.GetAsync("/api/purchase-orders/numbering"), HttpStatusCode.OK)).GetRawText());
        Assert.Equal(ownStock, await StateAsync(ownProduct.Id));
        Assert.Equal(foreignStock, await StateAsync(foreignProduct.Id));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.DetallePedidoCompra.CountAsync(x => x.PedidoCompraId == orderId));
        Assert.Equal(0, await db.EventoAuditoria.CountAsync(x => x.EntidadId == orderId &&
            x.Accion == "PURCHASEORDERUPDATED"));
    }
}
