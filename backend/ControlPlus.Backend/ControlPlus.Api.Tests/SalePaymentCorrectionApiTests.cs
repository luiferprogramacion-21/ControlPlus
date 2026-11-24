using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Corrección del medio de pago (20 de noviembre de 2026): misma venta y total, otro medio, caja cuadrada.</summary>
public sealed partial class SalesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task PaymentCorrection_MovesCashToNequi_KeepsTotal_ReplaysAndStillAllowsCancellation()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "payment-correction-origin",
            new ConfirmSaleRequest(null, [new(setup.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 1000m, 1000m, null)])), HttpStatusCode.Created);

        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Admin.AccessToken, setup.Admin.User.Id);
        SetBearer(client, setup.Admin.AccessToken);
        var reasons = await ReadAsync<SaleCancellationReasonDto[]>(
            await client.GetAsync("/api/sales/cancellation-reasons"), HttpStatusCode.OK);
        var reason = reasons.First(x => x.Code != "OTRO");

        async Task<HttpResponseMessage> Correct(string key, CorrectSalePaymentsRequest body)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{sale.Id}/payment-correction")
            { Content = JsonContent.Create(body) };
            message.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(message);
        }
        var request = new CorrectSalePaymentsRequest(sale.Version, reason.Id, null, [new("NEQUI", 1000m, null, "NQ-123")]);
        var corrected = await ReadAsync<CorrectSalePaymentsDto>(await Correct("payment-correction", request), HttpStatusCode.OK);
        Assert.Equal(sale.Version + 1, corrected.Version);
        Assert.Equal(-1000m, corrected.CashDifference);
        var nequi = Assert.Single(corrected.Payments);
        Assert.Equal(("NEQUI", 1000m, "NQ-123"), (nequi.MethodCode, nequi.AppliedAmount, nequi.Reference));

        // Repetir la misma solicitud devuelve lo mismo y no corrige dos veces.
        var replay = await ReadAsync<CorrectSalePaymentsDto>(await Correct("payment-correction", request), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(corrected), JsonSerializer.Serialize(replay));

        // La venta conserva su total; solo muestra el medio vigente.
        var detail = await ReadAsync<SaleDto>(await client.GetAsync($"/api/sales/{sale.Id}"), HttpStatusCode.OK);
        Assert.Equal(sale.Total, detail.Total);
        Assert.Equal("NEQUI", Assert.Single(detail.Payments).MethodCode);

        // La caja espera Nequi, no efectivo.
        var reconciliation = await ReadAsync<CashReconciliationDto>(await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(1_000m, reconciliation.TotalExpected);
        Assert.Equal(1_000m, reconciliation.PaymentMethods.Single(x => x.Code == "NEQUI").ExpectedAmount);
        Assert.Equal(0m, reconciliation.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var payments = await db.PagoVenta.AsNoTracking().Where(x => x.VentaId == sale.Id).ToArrayAsync();
            Assert.Equal(["ANULADO", "APLICADO"], payments.Select(x => x.Estado).Order().ToArray());
            Assert.Equal(1, await db.MovimientoCaja.CountAsync(x => x.Tipo == "REVERSO" && x.CategoriaMovimiento == "REVERSO_PAGO"));
            Assert.Equal(1, await db.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "CORREGIR_PAGO_VENTA"));
        }

        // Los mismos medios no son una corrección.
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Admin.AccessToken, setup.Admin.User.Id);
        SetBearer(client, setup.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await Correct("payment-correction-same",
            new CorrectSalePaymentsRequest(corrected.Version, reason.Id, null, [new("NEQUI", 1000m, null, "OTRA")]))).StatusCode);

        // Anular la venta corregida sigue funcionando y deja la caja como al abrir.
        using (var cancel = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{sale.Id}/cancel")
               { Content = JsonContent.Create(new CancelSaleRequest(corrected.Version, reason.Id, null)) })
        {
            cancel.Headers.Add("Idempotency-Key", "payment-correction-cancel");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(cancel)).StatusCode);
        }
        var after = await ReadAsync<CashReconciliationDto>(await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(0m, after.TotalExpected);
        Assert.Equal(0m, after.PaymentMethods.Single(x => x.Code == "NEQUI").ExpectedAmount);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task PaymentCorrection_OfAClosedShiftSale_MovesMoneyInTodaysCashAndKeepsTheClosure()
    {
        // Decisión D3 del 23/11/2026: la venta de ayer pagada por Nequi quedó en efectivo y la caja de ayer ya se cerró.
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "closed-correction-origin",
            new ConfirmSaleRequest(null, [new(setup.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 1000m, 1000m, null)])), HttpStatusCode.Created);
        await CloseCurrentShiftAsync(client, setup.Admin.AccessToken);
        Guid closedShiftId;
        decimal closedCashExpected;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var closed = await db.TurnoCaja.AsNoTracking().SingleAsync(x => x.Estado == "CERRADA");
            (closedShiftId, closedCashExpected) = (closed.Id, closed.EfectivoEsperado!.Value);
        }

        SetBearer(client, setup.Admin.AccessToken);
        _ = await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(5_000m, "Turno de hoy")), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Admin.AccessToken, setup.Admin.User.Id);
        SetBearer(client, setup.Admin.AccessToken);
        var reason = (await ReadAsync<SaleCancellationReasonDto[]>(
            await client.GetAsync("/api/sales/cancellation-reasons"), HttpStatusCode.OK)).First(x => x.Code != "OTRO");
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{sale.Id}/payment-correction")
        { Content = JsonContent.Create(new CorrectSalePaymentsRequest(sale.Version, reason.Id, null, [new("NEQUI", 1000m, null, null)])) };
        message.Headers.Add("Idempotency-Key", "closed-shift-payment-correction");
        var corrected = await ReadAsync<CorrectSalePaymentsDto>(await client.SendAsync(message), HttpStatusCode.OK);
        Assert.Equal(-1000m, corrected.CashDifference);
        Assert.Equal("NEQUI", Assert.Single(corrected.Payments).MethodCode);

        // El dinero se mueve en la caja de hoy: espera 1.000 menos de efectivo y 1.000 por Nequi.
        var today = await ReadAsync<CashReconciliationDto>(await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(4_000m, today.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        Assert.Equal(1_000m, today.PaymentMethods.Single(x => x.Code == "NEQUI").ExpectedAmount);

        // El cierre de ayer no cambia, y el pago correcto vive en el turno de hoy, unido a la venta por la correlación.
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var closed = await db.TurnoCaja.AsNoTracking().SingleAsync(x => x.Id == closedShiftId);
            Assert.Equal(("CERRADA", closedCashExpected), (closed.Estado, closed.EfectivoEsperado!.Value));
            var applied = await db.PagoVenta.AsNoTracking().Include(x => x.MovimientoCaja)
                .SingleAsync(x => x.VentaId == sale.Id && x.Estado == "APLICADO");
            Assert.NotEqual(closedShiftId, applied.MovimientoCaja!.TurnoCajaId);
            var reversal = await db.MovimientoCaja.AsNoTracking().SingleAsync(x => x.CategoriaMovimiento == "REVERSO_PAGO");
            Assert.Equal(applied.MovimientoCaja.CorrelacionId, reversal.CorrelacionId);
            Assert.Contains("caja ya cerrada", reversal.Concepto);
        }
    }
}
