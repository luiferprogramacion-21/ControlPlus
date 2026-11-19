using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Purchases.Contracts;
using ControlPlus.Application.Suppliers.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task ProductLabels_ReceiptQuantitiesAreDurableBoundedAndRetriedWithoutDoubleAllocation()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var supplier = await ReadAsync<SupplierDto>(await client.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest(null, null, "Proveedor etiquetas", null, null, null, null)), HttpStatusCode.Created);
        var draft = await ReadAsync<PurchaseDto>(await client.PostAsJsonAsync("/api/purchases",
            new CreatePurchaseRequest(supplier.Id, "ETQ-001", DateOnly.FromDateTime(DateTime.UtcNow),
                "NO_DISCRIMINADO", [new PurchaseLineRequest(scenario.Products.Standard, 4, 1m)])), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/product-labels/receipts/{draft.Id}")).StatusCode);
        using var confirmation = new HttpRequestMessage(HttpMethod.Post, $"/api/purchases/{draft.Id}/confirm")
        { Content = JsonContent.Create(new ConfirmPurchaseRequest(draft.Version)) };
        confirmation.Headers.Add("Idempotency-Key", "confirm-label-receipt");
        await ReadAsync<PurchaseDto>(await client.SendAsync(confirmation), HttpStatusCode.OK);
        var profile = await ReadAsync<PrintProfileDto>(await client.PostAsJsonAsync("/api/printing/profiles",
            new CreatePrintProfileRequest("Etiquetas recibidas", "SIMULADA", 32, false, "ETIQUETAS")), HttpStatusCode.Created);
        var pending = await ReadAsync<PagedResult<ProductLabelReceiptDto>>(
            await client.GetAsync("/api/product-labels/receipts"), HttpStatusCode.OK);
        Assert.Equal(4, Assert.Single(pending.Items).PendingLabelCount);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var stock = await db.Producto.Where(x => x.Id == scenario.Products.Standard).Select(x => x.StockActual).SingleAsync();
        var spooler = (FakePrintSpooler)scope.ServiceProvider.GetRequiredService<IPrintSpooler>();
        spooler.Fail = true;

        async Task<HttpResponseMessage> Queue(string key, int count) {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/product-labels/jobs")
            { Content = JsonContent.Create(new CreateProductLabelJobRequest(profile.Id,
                [new ProductLabelLineRequest(scenario.Products.Standard, count)], draft.Id)) };
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }
        var simultaneousReplay = await Task.WhenAll(Queue("receipt-labels-1", 2), Queue("receipt-labels-1", 2));
        var queued = await ReadAsync<ProductLabelJobDto>(simultaneousReplay[0], HttpStatusCode.Accepted);
        var replay = await ReadAsync<ProductLabelJobDto>(simultaneousReplay[1], HttpStatusCode.Accepted);
        Assert.Equal(queued.JobId, replay.JobId);
        Assert.Equal(HttpStatusCode.Conflict, (await Queue("receipt-labels-too-many", 3)).StatusCode);
        var remaining = await ReadAsync<ProductLabelReceiptDto>(await client.GetAsync(
            $"/api/product-labels/receipts/{draft.Id}"), HttpStatusCode.OK);
        Assert.Equal(2, remaining.PendingLabelCount);
        var document = await db.DocumentoEmitido.AsNoTracking().SingleAsync(x => x.Id == queued.BatchId);
        Assert.Contains("establishmentName", document.DatosSnapshot);
        // A failed printer job still reserves its receipt quantities. Retry that same batch.
        for (var attempt = 0; attempt < 100 && !await db.TrabajoImpresion.AsNoTracking()
                 .AnyAsync(x => x.Id == queued.JobId && x.Estado == "FALLIDO"); attempt++)
            await Task.Delay(50);
        Assert.Equal("FALLIDO", await db.TrabajoImpresion.AsNoTracking().Where(x => x.Id == queued.JobId)
            .Select(x => x.Estado).SingleAsync());
        spooler.Fail = false;
        var retry = await ReadAsync<ProductLabelQueueItemDto>(await client.PostAsync(
            $"/api/product-labels/jobs/{queued.JobId}/retry", null), HttpStatusCode.OK);
        Assert.Equal(queued.JobId, retry.JobId);
        var competing = await Task.WhenAll(Queue("receipt-labels-2", 2), Queue("receipt-labels-3", 2));
        Assert.Single(competing, response => response.StatusCode == HttpStatusCode.Accepted);
        Assert.Single(competing, response => response.StatusCode == HttpStatusCode.Conflict);
        pending = await ReadAsync<PagedResult<ProductLabelReceiptDto>>(
            await client.GetAsync("/api/product-labels/receipts"), HttpStatusCode.OK);
        Assert.Empty(pending.Items);
        Assert.Equal(2, await db.DocumentoEmitido.CountAsync(x => x.EntidadId == draft.Id && x.TipoDocumento == "ETIQUETA_PRODUCTO"));
        var jobs = await ReadAsync<PagedResult<ProductLabelQueueItemDto>>(
            await client.GetAsync("/api/product-labels/jobs"), HttpStatusCode.OK);
        Assert.Equal(2, jobs.TotalCount);
        Assert.All(jobs.Items, job => Assert.Equal(draft.Id, job.SourcePurchaseId));
        Assert.Equal(stock, await db.Producto.Where(x => x.Id == scenario.Products.Standard).Select(x => x.StockActual).SingleAsync());
        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/product-labels/receipts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/product-labels/jobs/{queued.JobId}/retry", null)).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProductLabels_SelectProductsQueueCode128BatchAndReplayIdempotently()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var profile = await ReadAsync<PrintProfileDto>(await client.PostAsJsonAsync(
            "/api/printing/profiles", new CreatePrintProfileRequest(
                "Térmica híbrida 01", "COLA_ETIQUETAS_SIMULADA", 32, true,
                "HIBRIDA", 50m, 30m, 203)), HttpStatusCode.Created);
        Assert.Equal("HIBRIDA", profile.PrinterType);
        Assert.Equal(50m, profile.LabelWidthMm);

        SetBearer(client, scenario.Supervisor.AccessToken);
        var profiles = await ReadAsync<IReadOnlyCollection<ProductLabelProfileDto>>(
            await client.GetAsync("/api/product-labels/profiles"), HttpStatusCode.OK);
        Assert.Contains(profiles, candidate => candidate.Id == profile.Id && candidate.IsDefault);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await db.Producto.AsNoTracking().SingleAsync(candidate =>
            candidate.Id == scenario.Products.Standard);
        var found = await ReadAsync<ControlPlus.Application.Common.PagedResult<ProductLabelProductDto>>(
            await client.GetAsync($"/api/product-labels/products?search={Uri.EscapeDataString(product.CodigoBarras!)}&page=1&pageSize=20"),
            HttpStatusCode.OK);
        Assert.Equal(product.Id, Assert.Single(found.Items).Id);

        var input = new CreateProductLabelJobRequest(profile.Id,
            [new ProductLabelLineRequest(product.Id, 4)]);
        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/product-labels/jobs")
        {
            Content = JsonContent.Create(input)
        };
        first.Headers.Add("Idempotency-Key", "labels-batch-1");
        var queued = await ReadAsync<ProductLabelJobDto>(await client.SendAsync(first), HttpStatusCode.Accepted);
        Assert.Equal(4, queued.LabelCount);
        await db.Producto.Where(candidate => candidate.Id == product.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.PrecioMinorista,
                candidate => candidate.PrecioMinorista + 100m));

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/product-labels/jobs")
        {
            Content = JsonContent.Create(input)
        };
        replayRequest.Headers.Add("Idempotency-Key", "labels-batch-1");
        var replay = await ReadAsync<ProductLabelJobDto>(await client.SendAsync(replayRequest), HttpStatusCode.Accepted);
        Assert.Equal(queued.JobId, replay.JobId);

        using var conflictRequest = new HttpRequestMessage(HttpMethod.Post, "/api/product-labels/jobs")
        {
            Content = JsonContent.Create(new CreateProductLabelJobRequest(profile.Id,
                [new ProductLabelLineRequest(product.Id, 5)]))
        };
        conflictRequest.Headers.Add("Idempotency-Key", "labels-batch-1");
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(conflictRequest)).StatusCode);

        Assert.Equal(1, await db.DocumentoEmitido.CountAsync(document =>
            document.TipoDocumento == "ETIQUETA_PRODUCTO" && document.EntidadId == queued.BatchId));
        Assert.Equal(1, await db.TrabajoImpresion.CountAsync(job => job.Id == queued.JobId));
        var spooler = (FakePrintSpooler)scope.ServiceProvider.GetRequiredService<IPrintSpooler>();
        for (var attempt = 0; attempt < 100 && spooler.LabelSubmissions.IsEmpty; attempt++)
            await Task.Delay(50);
        var submission = Assert.Single(spooler.LabelSubmissions);
        Assert.Equal(("COLA_ETIQUETAS_SIMULADA", 4), submission);

        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/product-labels/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/product-labels/products?page=1&pageSize=20")).StatusCode);
    }
}
