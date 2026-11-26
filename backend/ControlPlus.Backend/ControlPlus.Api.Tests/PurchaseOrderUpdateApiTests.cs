using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class PurchasesApiFlowTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task PurchaseOrder_UpdateDraft_ReplacesLinesAndRejectsStaleOrSentEdits()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor de edición de pedido");
        var retainedProduct = await ProductAsync(client, supplier.Id, "ORD-EDIT-KEEP", null);
        var removedProduct = await ProductAsync(client, supplier.Id, "ORD-EDIT-REMOVE", null);
        var addedProduct = await ProductAsync(client, supplier.Id, "ORD-EDIT-ADD", null);
        var beforeStock = new[]
        {
            await StateAsync(retainedProduct.Id),
            await StateAsync(removedProduct.Id),
            await StateAsync(addedProduct.Id)
        };

        var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = supplier.Id,
            lines = new[]
            {
                new { productId = retainedProduct.Id, requestedQuantity = 2, estimatedUnitCost = 10m },
                new { productId = removedProduct.Id, requestedQuantity = 3, estimatedUnitCost = 12m }
            }
        }), HttpStatusCode.Created);
        var orderId = created.GetProperty("id").GetGuid();
        var originalLineId = created.GetProperty("lines").EnumerateArray()
            .Single(x => x.GetProperty("productId").GetGuid() == retainedProduct.Id)
            .GetProperty("id").GetGuid();
        var originalNumber = created.GetProperty("visibleNumber").GetString();
        var update = new
        {
            expectedVersion = 1,
            lines = new[]
            {
                new { productId = retainedProduct.Id, requestedQuantity = 5,
                    estimatedUnitCost = 11.25m, observation = "  entregar primero  " },
                new { productId = addedProduct.Id, requestedQuantity = 4,
                    estimatedUnitCost = 13m, observation = "nuevo" }
            }
        };

        var edited = await ReadJsonAsync(await client.PutAsJsonAsync($"/api/purchase-orders/{orderId}", update),
            HttpStatusCode.OK);
        Assert.Equal("BORRADOR", edited.GetProperty("status").GetString());
        Assert.Equal(2, edited.GetProperty("version").GetInt64());
        Assert.Equal(originalNumber, edited.GetProperty("visibleNumber").GetString());
        var lines = edited.GetProperty("lines").EnumerateArray()
            .ToDictionary(x => x.GetProperty("productId").GetGuid());
        Assert.Equal(2, lines.Count);
        Assert.Equal(originalLineId, lines[retainedProduct.Id].GetProperty("id").GetGuid());
        Assert.Equal(5, lines[retainedProduct.Id].GetProperty("requestedQuantity").GetInt32());
        Assert.Equal(11.25m, lines[retainedProduct.Id].GetProperty("estimatedUnitCost").GetDecimal());
        Assert.Equal("entregar primero", lines[retainedProduct.Id].GetProperty("observation").GetString());
        Assert.Equal(4, lines[addedProduct.Id].GetProperty("requestedQuantity").GetInt32());
        Assert.DoesNotContain(removedProduct.Id, lines.Keys);

        await ProblemAsync(await client.PutAsJsonAsync($"/api/purchase-orders/{orderId}", update),
            HttpStatusCode.Conflict);
        var afterStale = await ReadJsonAsync(await client.GetAsync($"/api/purchase-orders/{orderId}"),
            HttpStatusCode.OK);
        AssertSameOrderBusinessState(edited, afterStale);

        var sent = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{orderId}/send",
            new { expectedVersion = 2 }), HttpStatusCode.OK);
        Assert.Equal("ENVIADO", sent.GetProperty("status").GetString());
        Assert.Equal(3, sent.GetProperty("version").GetInt64());
        await ProblemAsync(await client.PutAsJsonAsync($"/api/purchase-orders/{orderId}",
            new { expectedVersion = 3, update.lines }), HttpStatusCode.Conflict);
        var afterSentEdit = await ReadJsonAsync(await client.GetAsync($"/api/purchase-orders/{orderId}"),
            HttpStatusCode.OK);
        AssertSameOrderBusinessState(sent, afterSentEdit);

        Assert.Equal(beforeStock[0], await StateAsync(retainedProduct.Id));
        Assert.Equal(beforeStock[1], await StateAsync(removedProduct.Id));
        Assert.Equal(beforeStock[2], await StateAsync(addedProduct.Id));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(2, await db.DetallePedidoCompra.CountAsync(x => x.PedidoCompraId == orderId));
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.EntidadId == orderId &&
            x.Accion == "PURCHASEORDERUPDATED"));
    }

    private static void AssertSameOrderBusinessState(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.GetProperty("id").GetGuid(), actual.GetProperty("id").GetGuid());
        Assert.Equal(expected.GetProperty("status").GetString(), actual.GetProperty("status").GetString());
        Assert.Equal(expected.GetProperty("version").GetInt64(), actual.GetProperty("version").GetInt64());
        Assert.Equal(expected.GetProperty("visibleNumber").GetString(),
            actual.GetProperty("visibleNumber").GetString());
        var expectedLines = expected.GetProperty("lines").EnumerateArray()
            .ToDictionary(x => x.GetProperty("productId").GetGuid());
        var actualLines = actual.GetProperty("lines").EnumerateArray()
            .ToDictionary(x => x.GetProperty("productId").GetGuid());
        Assert.Equal(expectedLines.Keys.Order(), actualLines.Keys.Order());
        foreach (var (productId, expectedLine) in expectedLines)
        {
            var actualLine = actualLines[productId];
            Assert.Equal(expectedLine.GetProperty("id").GetGuid(), actualLine.GetProperty("id").GetGuid());
            Assert.Equal(expectedLine.GetProperty("requestedQuantity").GetInt32(),
                actualLine.GetProperty("requestedQuantity").GetInt32());
            Assert.Equal(expectedLine.GetProperty("receivedQuantity").GetInt32(),
                actualLine.GetProperty("receivedQuantity").GetInt32());
            Assert.Equal(expectedLine.GetProperty("estimatedUnitCost").GetDecimal(),
                actualLine.GetProperty("estimatedUnitCost").GetDecimal());
            Assert.Equal(expectedLine.GetProperty("observation").GetString(),
                actualLine.GetProperty("observation").GetString());
        }
    }
}
