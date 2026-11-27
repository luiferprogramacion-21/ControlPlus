using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task SaleCorrection_RemovesUnitsWithoutReplacement_MixedRefundHistoryAndReplayPreserveOriginal()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "correction-mixed-origin",
            new ConfirmSaleRequest(null, [new(setup.Products.Standard, 2, "MINORISTA")], null, null,
                [new("EFECTIVO", 800m, 800m, null), new("TRANSFERENCIA", 1200m, null, "ORIGIN")])), HttpStatusCode.Created);
        var receiptBefore = await ReadAsync<SaleReceiptDto>(await client.GetAsync($"/api/sales/{sale.Id}/receipt"), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Admin.AccessToken, setup.Admin.User.Id);
        SetBearer(client, setup.Admin.AccessToken);
        var request = new ConfirmSaleChangeRequest(sale.Version, null,
            [new(Assert.Single(sale.Lines).Id, 1, true)], [], "AUTO",
            [new("EFECTIVO", 400m, null, null), new("TRANSFERENCIA", 600m, null, "REFUND")], null, "Cantidad cobrada de más");
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id, "correction-mixed", request), HttpStatusCode.Created);
        Assert.Equal(0m, change.TotalDelivered); Assert.Equal(1000m, change.TotalReturned);
        Assert.Equal(-1000m, change.Difference); Assert.Equal("DEVOLUCION", change.Treatment);
        Assert.Equal("DEVUELTO", Assert.Single(change.Details).Type);
        Assert.Equal(Assert.Single(sale.Lines).ProductName, Assert.Single(change.Details).ProductName);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var state = await CaptureChangeStateAsync(db, sale.Id, setup.Products.Standard, setup.Products.ResidueA);
        Assert.Equal(99, state.ReturnedStock); Assert.Equal(1, state.ChangeInventoryMovements);
        var replay = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id, "correction-mixed", request), HttpStatusCode.Created);
        Assert.Equal(JsonSerializer.Serialize(change), JsonSerializer.Serialize(replay));
        Assert.Equal(state, await CaptureChangeStateAsync(db, sale.Id, setup.Products.Standard, setup.Products.ResidueA));
        var prep = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync($"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
        Assert.Equal(1, Assert.Single(prep.Lines).RemainingQuantity);
        var receiptAfter = await ReadAsync<SaleReceiptDto>(await client.GetAsync($"/api/sales/{sale.Id}/receipt"), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(receiptBefore), JsonSerializer.Serialize(receiptAfter));
        var history = await ReadAsync<SaleChangeDto[]>(await client.GetAsync($"/api/sales/{sale.Id}/changes"), HttpStatusCode.OK);
        Assert.Equal(change.Id, Assert.Single(history).Id);

        async Task<HttpResponseMessage> Receipt(string key)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{sale.Id}/changes/{change.Id}/receipt-requests");
            message.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(message);
        }
        var document = await ReadAsync<SaleCorrectionReceiptDto>(await Receipt("correction-receipt"), HttpStatusCode.Created);
        var documentReplay = await ReadAsync<SaleCorrectionReceiptDto>(await Receipt("correction-receipt"), HttpStatusCode.Created);
        Assert.Equal(JsonSerializer.Serialize(document), JsonSerializer.Serialize(documentReplay));
        Assert.Equal(change.VisibleNumber, document.Correction.VisibleNumber);
        Assert.Equal(1, await db.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "COMPROBANTE_CAMBIO"));
        // Pruebas del usuario del 26/11/2026: al aprobar la corrección el comprobante se manda a la impresora de tickets;
        // sin impresora configurada responde SIN_PERFIL y la pantalla lo imprime en el navegador.
        using (var print = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{sale.Id}/changes/{change.Id}/receipt-requests/print"))
        {
            print.Headers.Add("Idempotency-Key", "correction-receipt-print");
            var printed = await ReadAsync<SaleCorrectionPrintDto>(await client.SendAsync(print), HttpStatusCode.OK);
            Assert.Equal("SIN_PERFIL", printed.Status);
            Assert.Equal(change.VisibleNumber, printed.Receipt.Correction.VisibleNumber);
        }
        Assert.Equal(state, await CaptureChangeStateAsync(db, sale.Id, setup.Products.Standard, setup.Products.ResidueA));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleCorrection_CreditWithPayment_ReducesDebtThenRefundsOnlyExcess_AndBlocksClosedCredit()
    {
        using var client = CreateClient(); var setup = await BootstrapAsync(client);
        var customer = await CreateCustomerAsync(setup.Admin.User.Id, true, "CORRECTION-CREDIT");
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "correction-credit-origin",
            new ConfirmSaleRequest(customer, [new(setup.Products.Standard, 2, "MINORISTA")], null, null, [],
                "CREDITO", state.CurrentShift!.OperatingDate.AddDays(10), new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        using var payment = new HttpRequestMessage(HttpMethod.Post, $"/api/credits/{sale.CreditId}/payments")
            { Content = JsonContent.Create(new ApplyCreditPaymentRequest(1, [new("EFECTIVO", 1500m, 1500m, null)])) };
        payment.Headers.Add("Idempotency-Key", "correction-credit-payment");
        var abono = await ReadAsync<CreditPaymentOperationDto>(await client.SendAsync(payment), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Admin.AccessToken, setup.Admin.User.Id);
        SetBearer(client, setup.Admin.AccessToken);
        var request = new ConfirmSaleChangeRequest(sale.Version, abono.Version,
            [new(Assert.Single(sale.Lines).Id, 1, true)], [], "AUTO", [new("EFECTIVO", 500m, null, null)], null, "Producto cobrado por error");
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id, "correction-credit", request), HttpStatusCode.Created);
        Assert.Equal("REDUCIR_CREDITO_Y_DEVOLVER", change.Treatment);
        Assert.Equal(500m, change.CreditAdjustment); Assert.Equal(500m, change.PaidOrRefunded);
        await using var scope = _factory!.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var credit = await db.Credito.AsNoTracking().SingleAsync(x => x.Id == sale.CreditId);
        Assert.Equal(0m, credit.SaldoPendiente); Assert.Equal("PAGADO", credit.Estado);
        Assert.Equal(1, await db.PagoCredito.CountAsync(x => x.CreditoId == credit.Id));
        var prep = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync($"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
        Assert.False(prep.CanStartChange); Assert.Contains("pagado", prep.BlockedReason!);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleCorrection_RequiresReasonAndResellableOriginalUnits_AndCannotOverreturn()
    {
        using var client = CreateClient(); var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken); await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "correction-guard-origin",
            new ConfirmSaleRequest(null, [new(setup.Products.Standard, 1, "MINORISTA")], null, null, [new("EFECTIVO", 1000m, 1000m, null)])), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Admin.AccessToken, setup.Admin.User.Id); SetBearer(client, setup.Admin.AccessToken);
        var line = Assert.Single(sale.Lines).Id;
        var request = new ConfirmSaleChangeRequest(sale.Version, null, [new(line, 1, true)], [], "AUTO", [new("EFECTIVO", 1000m, null, null)], null, "Producto cobrado por error");
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id, "correction-no-reason", request with { Reason = null }), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id, "correction-damaged", request with { Returned = [new(line, 1, false)] }), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id, "correction-excess", request with { Returned = [new(line, 2, true)] }), HttpStatusCode.Conflict);
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id, "correction-full", request), HttpStatusCode.Created);
        Assert.Equal(-sale.Total, change.Difference);
        var prep = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync($"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
        Assert.False(prep.CanStartChange); Assert.Equal(0, Assert.Single(prep.Lines).RemainingQuantity);
        Assert.Contains("originales ya se devolvieron", prep.BlockedReason!);
    }
}
