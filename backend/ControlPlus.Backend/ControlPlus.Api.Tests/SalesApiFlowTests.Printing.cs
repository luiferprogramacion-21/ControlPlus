using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConfiguredWindowsQueue_OriginalAndReprintAreIdempotentAndDoNotChangeMoney()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var profile = await ReadAsync<PrintProfileDto>(await client.PostAsJsonAsync(
            "/api/printing/profiles", new CreatePrintProfileRequest("Recibos de prueba", "COLA_SIMULADA", 32)),
            HttpStatusCode.Created);
        Assert.Equal("COLA_SIMULADA", profile.SystemQueueName);
        Assert.True(profile.IsDefault);
        var statusResponse = await client.GetAsync("/api/printing/status");
        var status = await ReadAsync<PrintStatusDto>(statusResponse, HttpStatusCode.OK);
        Assert.True(status.HasActiveProfile);
        Assert.Equal("Recibos de prueba", status.ProfileName);
        Assert.Equal(32, status.Columns);
        Assert.DoesNotContain("COLA_SIMULADA", await statusResponse.Content.ReadAsStringAsync());
        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.True((await ReadAsync<PrintStatusDto>(await client.GetAsync("/api/printing/status"),
            HttpStatusCode.OK)).HasActiveProfile);
        SetBearer(client, scenario.Admin.AccessToken);

        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        var request = CashRequest(scenario.Products.Standard, 1, 1_000m);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "printing-sale", request),
            HttpStatusCode.Created);
        var replay = await ReadAsync<SaleDto>(await PostSaleAsync(client, "printing-sale", request),
            HttpStatusCode.Created);
        Assert.Equal(sale.Id, replay.Id);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var original = Assert.Single(await db.TrabajoImpresion.AsNoTracking()
            .Where(x => x.DocumentoEmitido.EntidadId == sale.Id).ToArrayAsync());
        Assert.Equal("ORIGINAL", original.TipoCopia);
        Assert.Equal(0, original.NumeroReimpresion);
        Assert.Equal(64, original.HashContenido.Length);

        var beforeSales = await db.Venta.CountAsync();
        var beforePayments = await db.PagoVenta.CountAsync();
        var beforeCash = await db.MovimientoCaja.CountAsync();
        using var first = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{sale.Id}/reprint-requests");
        first.Headers.Add("Idempotency-Key", "printing-reprint");
        var requested = await ReadAsync<SaleReprintRequestDto>(await client.SendAsync(first), HttpStatusCode.OK);
        using var second = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{sale.Id}/reprint-requests");
        second.Headers.Add("Idempotency-Key", "printing-reprint");
        var rechecked = await ReadAsync<SaleReprintRequestDto>(await client.SendAsync(second), HttpStatusCode.OK);
        Assert.Equal(requested, rechecked);
        var jobs = await db.TrabajoImpresion.AsNoTracking()
            .Where(x => x.DocumentoEmitido.EntidadId == sale.Id).OrderBy(x => x.NumeroReimpresion).ToArrayAsync();
        Assert.Equal(2, jobs.Length);
        Assert.Equal("REIMPRESION", jobs[1].TipoCopia);
        Assert.Equal(1, jobs[1].NumeroReimpresion);
        Assert.Contains("REIMPRESIÓN", System.Text.Encoding.UTF8.GetString(jobs[1].ContenidoRenderizado));
        Assert.Equal(beforeSales, await db.Venta.CountAsync());
        Assert.Equal(beforePayments, await db.PagoVenta.CountAsync());
        Assert.Equal(beforeCash, await db.MovimientoCaja.CountAsync());

        var spooler = (FakePrintSpooler)scope.ServiceProvider.GetRequiredService<IPrintSpooler>();
        for (var attempt = 0; attempt < 100 && spooler.Submissions.Count < 2; attempt++)
            await Task.Delay(50);
        Assert.Equal(2, spooler.Submissions.Count);
        Assert.All(spooler.Submissions, x => Assert.Equal("COLA_SIMULADA", x));
        for (var attempt = 0; attempt < 100 && await db.TrabajoImpresion.AsNoTracking()
                 .CountAsync(x => x.DocumentoEmitido.EntidadId == sale.Id && x.Estado == "IMPRESO") < 2; attempt++)
            await Task.Delay(50);
        Assert.Equal(2, await db.TrabajoImpresion.AsNoTracking()
            .CountAsync(x => x.DocumentoEmitido.EntidadId == sale.Id && x.Estado == "IMPRESO"));
        Assert.Equal(2, await db.EventoAuditoria.AsNoTracking()
            .CountAsync(x => x.Accion == "PRINTJOBACCEPTED"));
        var visible = await ReadAsync<PagedResult<PrintJobDto>>(await client.GetAsync(
            $"/api/sales/{sale.Id}/print-jobs"), HttpStatusCode.OK);
        Assert.Equal(2, visible.TotalCount);
        Assert.All(visible.Items, x =>
        {
            Assert.Equal("IMPRESO", x.Status);
            Assert.NotNull(x.QueueAcceptedAtUtc);
        });
        SetBearer(client, scenario.SecondCashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/sales/{sale.Id}/print-jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsync($"/api/sales/{sale.Id}/print-jobs/{jobs[0].Id}/retry", null)).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PrinterFailure_DoesNotReverseSale_AndSaleRetryIsIdempotent()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        await ReadAsync<PrintProfileDto>(await client.PostAsJsonAsync("/api/printing/profiles",
            new CreatePrintProfileRequest("Recibos", "COLA_SIMULADA", 32)), HttpStatusCode.Created);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var spooler = (FakePrintSpooler)scope.ServiceProvider.GetRequiredService<IPrintSpooler>();
        spooler.Fail = true;
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "printing-failure",
            CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Created);
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        for (var attempt = 0; attempt < 100 && !await db.TrabajoImpresion.AsNoTracking()
                 .AnyAsync(x => x.DocumentoEmitido.EntidadId == sale.Id && x.Estado == "FALLIDO"); attempt++)
            await Task.Delay(50);
        var job = Assert.Single(await db.TrabajoImpresion.AsNoTracking()
            .Where(x => x.DocumentoEmitido.EntidadId == sale.Id).ToArrayAsync());
        Assert.Equal("FALLIDO", job.Estado);
        Assert.Equal("CONFIRMADA", (await db.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id)).Estado);
        var visible = await ReadAsync<PagedResult<PrintJobDto>>(await client.GetAsync(
            $"/api/sales/{sale.Id}/print-jobs"), HttpStatusCode.OK);
        Assert.Equal("ERROR", Assert.Single(visible.Items).Status);
        spooler.Fail = false;
        var retryUrl = $"/api/sales/{sale.Id}/print-jobs/{job.Id}/retry";
        await ReadAsync<PrintJobDto>(await client.PostAsync(retryUrl, null), HttpStatusCode.OK);
        await ReadAsync<PrintJobDto>(await client.PostAsync(retryUrl, null), HttpStatusCode.OK);
        Assert.Equal(1, await db.EventoAuditoria.AsNoTracking()
            .CountAsync(x => x.Accion == "PRINTJOBRETRIED" && x.EntidadId == job.Id));
        Assert.Single(await db.TrabajoImpresion.AsNoTracking()
            .Where(x => x.DocumentoEmitido.EntidadId == sale.Id).ToArrayAsync());
        Assert.Equal("CONFIRMADA", (await db.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id)).Estado);
    }

    private sealed class FakePrintSpooler : IPrintSpooler
    {
        public FakePrintSpooler() { }

        public bool Fail { get; set; }

        public System.Collections.Concurrent.ConcurrentBag<string> Submissions { get; } = [];

        public Task SubmitAsync(string queueName, byte[] utf8Text, string documentName,
            CancellationToken cancellationToken = default)
        {
            Submissions.Add(queueName);
            if (Fail) throw new PrintSubmissionException(false, new InvalidOperationException("Falla simulada de cola"));
            return Task.CompletedTask;
        }
    }
}
