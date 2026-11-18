using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Printing;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
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
