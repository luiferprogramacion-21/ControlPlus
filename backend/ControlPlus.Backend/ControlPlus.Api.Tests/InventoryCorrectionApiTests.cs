using System.Net;
using ControlPlus.Application.Inventory.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class InventoryApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task CorrectManualAdjustment_RestoresStockAndLinksCompensatingMovement()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, 10);
        var reason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(client, "/api/inventory/adjustment-reasons", scenario.Admin.AccessToken),
            HttpStatusCode.OK)).Single(x => x.Code == "CONTEO_FISICO");
        var original = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(client,
            scenario.Admin.AccessToken, "inventory-correction-origin",
            new CreateInventoryAdjustmentRequest(scenario.ProductId, 4, reason.Id,
                scenario.ProductVersion, "Conteo registrado de más", null)), HttpStatusCode.Created);

        long currentVersion;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            currentVersion = await db.Producto.Where(x => x.Id == scenario.ProductId)
                .Select(x => x.Version).SingleAsync();
        }

        var correctionRequest = new CreateInventoryAdjustmentRequest(scenario.ProductId, -4,
            reason.Id, currentVersion, "Corrección del conteo", null, original.Id);
        var corrected = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(client,
            scenario.Admin.AccessToken, "inventory-correction-apply", correctionRequest), HttpStatusCode.Created);

        Assert.Equal("AJUSTE_SALIDA", corrected.Type);
        Assert.Equal(-4, corrected.StockQuantity);
        Assert.Equal(10, corrected.NewStock);
        await using var finalScope = _factory!.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(10, await finalDb.Producto.Where(x => x.Id == scenario.ProductId)
            .Select(x => x.StockActual).SingleAsync());
        var link = await finalDb.Set<CorreccionMovimientoInventario>().AsNoTracking().SingleAsync();
        Assert.Equal(original.Id, link.MovimientoOriginalId);
        Assert.Equal(corrected.Id, link.MovimientoCorrectorId);
        Assert.Equal(2, await finalDb.MovimientoInventario.CountAsync(x =>
            x.ProductoId == scenario.ProductId && (x.TipoMovimiento == "AJUSTE_ENTRADA" || x.TipoMovimiento == "AJUSTE_SALIDA")));
    }
}
