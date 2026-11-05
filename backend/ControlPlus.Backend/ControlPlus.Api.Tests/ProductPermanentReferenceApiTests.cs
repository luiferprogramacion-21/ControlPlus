using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CatalogAutomaticInternalCodeApiTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task GeneratedBarcode_ConcurrentSameKeyCreatesOneProductMovementAndReference()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var request = ProductRequest(scenario, null) with
        { GenerateBarcode = true, InitialQuantity = 2, InitialUnitCost = 15m };
        var responses = await Task.WhenAll(PostInitialAsync(client, request, "generated-concurrent"),
            PostInitialAsync(client, request, "generated-concurrent"));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var first = await ReadProductAsync(firstResponse);
        Assert.Equal(first, await ReadProductAsync(secondResponse));
        Assert.Equal("CAT-000001", first.InternalCode);
        Assert.Equal("CP000001", first.Barcode);
        Assert.Equal("CODE128", first.BarcodeFormat);
        using var replay = await PostInitialAsync(client, request, "generated-concurrent");
        Assert.Equal(first, await ReadProductAsync(replay));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.Producto.CountAsync());
        Assert.Equal(2, await db.ProductoCodigo.CountAsync(x => x.ProductoId == first.Id));
        Assert.Equal(1, await db.MovimientoInventario.CountAsync(x => x.ProductoId == first.Id && x.TipoMovimiento == "INVENTARIO_INICIAL"));
        Assert.Equal(1, await db.OperacionIdempotente.CountAsync(x => x.EntidadId == first.Id));
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.EntidadId == first.Id && x.Accion == "PRODUCTCREATED"));
        var next = await ReadProductAsync(await client.PostAsJsonAsync("/api/products",
            request with { InitialQuantity = 0, InitialUnitCost = null }));
        Assert.Equal("CAT-000002", next.InternalCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task PermanentReference_GeneratedBarcodeReplaySearchAndEditingRemainConsistent()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var request = ProductRequest(scenario, "PRD-LEGACY-REQUEST") with
        { GenerateBarcode = true, InitialQuantity = 3, InitialUnitCost = 25m };
        var first = await ReadProductAsync(await PostInitialAsync(client, request, "permanent-key"));
        Assert.Equal("CAT-000001", first.InternalCode);
        Assert.Equal("CP000001", first.Barcode);
        Assert.Equal("CODE128", first.BarcodeFormat);
        Assert.Equal(first, await ReadProductAsync(await PostInitialAsync(client, request, "permanent-key")));
        var aliasSearch = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?search=PRD-LEGACY-REQUEST");
        Assert.Equal(first.Id, Assert.Single(aliasSearch!.Items).Id);
        var update = new UpdateProductRequest(scenario.CategoryId, scenario.UnitId, null,
            first.InternalCode, first.Barcode, first.BarcodeFormat, true, "Nombre editado", null,
            1000, null, null, 0, first.Version);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/products/{first.Id}", update with { InternalCode = "P-999999" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/products/{first.Id}", update with { Barcode = "otro" })).StatusCode);
        using var updatedResponse = await client.PutAsJsonAsync($"/api/products/{first.Id}", update);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var changed = (await updatedResponse.Content.ReadFromJsonAsync<ProductDto>())!;
        Assert.Equal(first.InternalCode, changed.InternalCode);
        Assert.Equal(first.Barcode, changed.Barcode);
        Assert.Equal(first, await ReadProductAsync(await PostInitialAsync(client, request, "permanent-key")));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ExternalBarcode_KeepsLeadingZerosAndUnknownFormatAndCannotShadowReference()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var request = ProductRequest(scenario, null) with { Barcode = "0000123456789", BarcodeFormat = null };
        var first = await ReadProductAsync(await client.PostAsJsonAsync("/api/products", request));
        Assert.Equal("0000123456789", first.Barcode);
        Assert.Null(first.BarcodeFormat);
        Assert.False(first.BarcodeGenerated);
        var found = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?search=0000123456789");
        Assert.Equal(first.Id, Assert.Single(found!.Items).Id);
        await ReadProductAsync(await client.PostAsJsonAsync("/api/products", request with { Barcode = null, Name = first.InternalCode }));
        var referenceSearch = await client.GetFromJsonAsync<PagedResult<ProductDto>>($"/api/products?search={first.InternalCode}");
        Assert.Equal(first.Id, Assert.Single(referenceSearch!.Items).Id);
        var collision = await client.PostAsJsonAsync("/api/products", request with { Barcode = first.InternalCode });
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/products", request)).StatusCode);
        var empty = await ReadProductAsync(await client.PostAsJsonAsync("/api/products", request with { Barcode = null }));
        Assert.Null(empty.Barcode);
        Assert.NotEqual(first.InternalCode, empty.InternalCode);
    }
}
