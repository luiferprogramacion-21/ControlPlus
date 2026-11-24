using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_CashRefundUsesChosenOriginalMethodsAndReplayHasNoAdditionalEffects()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "sale-change-cash-origin",
            new ConfirmSaleRequest(null, [new(setup.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 400m, 400m, null),
                    new("TRANSFERENCIA", 600m, null, "ORIGINAL-TRANSFER")])), HttpStatusCode.Created);
        var receiptBefore = await ReadAsync<SaleReceiptDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/receipt"), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Admin.AccessToken, setup.Admin.User.Id);
        var request = new ConfirmSaleChangeRequest(sale.Version, null,
            [new(Assert.Single(sale.Lines).Id, 1, true)],
            [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
            [new("EFECTIVO", 400m, null, null),
                new("TRANSFERENCIA", 499m, null, "CHANGE-REFUND")],
            null, "Cambio de prueba");
        SetBearer(client, setup.Admin.AccessToken);
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "cash-change-replay", request), HttpStatusCode.Created);
        Assert.Equal("CAM-SALESTEST1-1", change.VisibleNumber);
        Assert.Equal(-899m, change.Difference);
        Assert.Equal("DEVOLUCION", change.Treatment);
        Assert.Equal(899m, change.PaidOrRefunded);
        Assert.Equal(new[] { "EFECTIVO", "TRANSFERENCIA" },
            change.Payments.Select(x => x.MethodCode).Order().ToArray());
        Assert.Equal("CHANGE-REFUND", change.Payments.Single(x => x.MethodCode == "TRANSFERENCIA").Reference);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var before = await CaptureChangeStateAsync(db, sale.Id, setup.Products.Standard,
            setup.Products.ResidueA);
        Assert.Equal(1, before.Changes);
        Assert.Equal(2, before.ChangePayments);
        Assert.Equal(2, before.ChangeInventoryMovements);
        var ownAuthorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.TipoOperacion == "CAMBIO_VENTA");
        Assert.Equal(ownAuthorization.UsuarioSolicitanteId, ownAuthorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", ownAuthorization.MetodoAutenticacion);
        Assert.Null(ownAuthorization.CredencialAutorizadorId);
        Assert.Contains("sin segundo autorizador", ownAuthorization.DescripcionOperacion);
        Assert.Equal(100, before.ReturnedStock);
        Assert.Equal(99, before.DeliveredStock);
        Assert.Equal(1, before.Sales);
        Assert.Equal(2, before.SalePayments);
        var auditRows = await db.EventoAuditoria.AsNoTracking()
            .Select(x => x.DatosNuevos).ToArrayAsync();
        var auditText = string.Join('|', auditRows.Select(x => x ?? string.Empty));
        var idempotentResult = await db.OperacionIdempotente.AsNoTracking()
            .Where(x => x.TipoOperacion == "CAMBIAR_VENTA").Select(x => x.Resultado).SingleAsync();
        Assert.DoesNotContain(SupervisorPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(SupervisorPassword, idempotentResult, StringComparison.Ordinal);
        var receiptAfter = await ReadAsync<SaleReceiptDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/receipt"), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(receiptBefore), JsonSerializer.Serialize(receiptAfter));

        var replay = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "cash-change-replay", request with { Authorization = null }), HttpStatusCode.Created);
        Assert.Equal(JsonSerializer.Serialize(change), JsonSerializer.Serialize(replay));
        Assert.Equal(before, await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA));
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Admin.AccessToken, setup.Admin.User.Id);
        SetBearer(client, setup.Admin.AccessToken);
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id, "cash-change-stale",
            request), HttpStatusCode.Conflict);
        Assert.Equal(before, await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA));

        using var cancellation = new HttpRequestMessage(HttpMethod.Post,
            $"/api/sales/{sale.Id}/cancel")
        { Content = JsonContent.Create(new CancelSaleRequest(before.SaleVersion,
            Guid.CreateVersion7(), null, new(SupervisorUser, SupervisorPassword))) };
        cancellation.Headers.Add("Idempotency-Key", "cancel-after-change-rejected");
        await AssertProblemAsync(await client.SendAsync(cancellation), HttpStatusCode.Conflict);
        Assert.Equal(before, await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA));

        var history = await ReadAsync<IReadOnlyCollection<SaleChangeDto>>(
            await client.GetAsync($"/api/sales/{sale.Id}/changes"), HttpStatusCode.OK);
        Assert.Equal(change.Id, Assert.Single(history).Id);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_ReturnedResellableUnitCanSupplySameProductWhenCurrentStockIsReserved()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await using (var setupScope = _factory!.Services.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var product = await setupDb.Producto.SingleAsync(x => x.Id == setup.Products.Standard);
            product.StockActual = 2;
            product.StockReservado = 1;
            await setupDb.SaveChangesAsync();
        }
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-same-product-origin",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        var request = new ConfirmSaleChangeRequest(sale.Version, null,
            [new(Assert.Single(sale.Lines).Id, 1, true)],
            [new(setup.Products.Standard, 1, "MINORISTA")], "AUTO", [],
            new(SupervisorUser, SupervisorPassword));
        using var firstClient = CreateClient(setup.Cashier.AccessToken);
        using var secondClient = CreateClient(setup.Cashier.AccessToken);
        var responses = await Task.WhenAll(
            PostChangeAsync(firstClient, sale.Id, "change-reserved-race-a", request),
            PostChangeAsync(secondClient, sale.Id, "change-reserved-race-b", request));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var change = await ReadAsync<SaleChangeDto>(
            responses.Single(x => x.StatusCode == HttpStatusCode.Created), HttpStatusCode.Created);
        Assert.Equal("SIN_DIFERENCIA", change.Treatment);
        Assert.Empty(change.Payments);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var stock = await db.Producto.AsNoTracking().SingleAsync(x => x.Id == setup.Products.Standard);
        Assert.Equal(1, stock.StockActual);
        Assert.Equal(1, stock.StockReservado);
        var movements = await db.MovimientoInventario.AsNoTracking()
            .Where(x => x.CambioVentaId == change.Id).OrderBy(x => x.TipoMovimiento).ToArrayAsync();
        Assert.Equal(2, movements.Length);
        var returned = Assert.Single(movements, x => x.TipoMovimiento == "CAMBIO_DEVOLUCION");
        var delivered = Assert.Single(movements, x => x.TipoMovimiento == "CAMBIO_ENTREGA");
        Assert.Equal((1, 2, 1, 1),
            (returned.StockAnterior, returned.StockPosterior, returned.ReservadoAnterior, returned.ReservadoPosterior));
        Assert.Equal((2, 1, 1, 1),
            (delivered.StockAnterior, delivered.StockPosterior, delivered.ReservadoAnterior, delivered.ReservadoPosterior));
        Assert.Equal(0, await db.PagoCambioVenta.CountAsync(x => x.CambioVentaId == change.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_ConcurrentSameKeyCreatesOneChangeAndOneFinancialSettlement()
    {
        using var setupClient = CreateClient();
        var setup = await BootstrapAsync(setupClient);
        await ConfigureChangeNumberingAsync(setupClient, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(setupClient, setup.Admin.AccessToken);
        _ = await StartSessionAsync(setupClient, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(setupClient, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(setupClient,
            "change-concurrent-origin", CashRequest(setup.Products.Standard, 1, 1000m)),
            HttpStatusCode.Created);
        _ = await StartSessionAsync(setupClient, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        var request = new ConfirmSaleChangeRequest(sale.Version, null,
            [new(Assert.Single(sale.Lines).Id, 1, true)],
            [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
            [new("EFECTIVO", 899m, null, null)],
            new(SupervisorUser, SupervisorPassword));
        using var firstClient = CreateClient(setup.Cashier.AccessToken);
        using var secondClient = CreateClient(setup.Cashier.AccessToken);
        var responses = await Task.WhenAll(
            PostChangeAsync(firstClient, sale.Id, "change-concurrent-same", request),
            PostChangeAsync(secondClient, sale.Id, "change-concurrent-same", request));
        var results = await Task.WhenAll(responses.Select(x => ReadAsync<SaleChangeDto>(x,
            HttpStatusCode.Created)));
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.CambioVenta.CountAsync(x => x.VentaId == sale.Id));
        Assert.Equal(1, await db.PagoCambioVenta.CountAsync(x => x.CambioVenta.VentaId == sale.Id));
        Assert.Equal(1, await db.MovimientoCaja.CountAsync(x => x.CategoriaMovimiento == "CAMBIO_VENTA"));
        Assert.Equal(2, await db.MovimientoInventario.CountAsync(x => x.CambioVentaId == results[0].Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_RefundLimitUsesOriginalAndLaterCollectionsNetOfPriorRefunds()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await using (var setupScope = _factory!.Services.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var cheaper = await setupDb.Producto.SingleAsync(x => x.Id == setup.Products.ResidueB);
            cheaper.PrecioMinorista = 50m;
            await setupDb.SaveChangesAsync();
        }
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-net-medium-origin",
            new ConfirmSaleRequest(null,
                [new(setup.Products.Standard, 1, "MINORISTA"),
                    new(setup.Products.ResidueA, 1, "MINORISTA"),
                    new(setup.Products.ResidueC, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 1m, 1m, null),
                    new("TRANSFERENCIA", 1217m, null, "ORIGINAL-NET")])),
            HttpStatusCode.Created);
        var standardLine = sale.Lines.Single(x => x.ProductId == setup.Products.Standard);
        var smallLine = sale.Lines.Single(x => x.ProductId == setup.Products.ResidueA);
        var thirdLine = sale.Lines.Single(x => x.ProductId == setup.Products.ResidueC);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var collection = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "change-net-medium-collect", new ConfirmSaleChangeRequest(sale.Version, null,
                [new(smallLine.Id, 1, true)],
                [new(setup.Products.Standard, 1, "MINORISTA")], "AUTO",
                [new("EFECTIVO", 899m, 899m, null)],
                new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        Assert.Equal("PAGO_INMEDIATO", collection.Treatment);
        var preparation = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var baseRequest = new ConfirmSaleChangeRequest(preparation.SaleVersion, null,
            [new(standardLine.Id, 1, true)],
            [new(setup.Products.ResidueB, 1, "MINORISTA")], "AUTO",
            [new("EFECTIVO", 950m, null, null)],
            new(SupervisorUser, SupervisorPassword));
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id,
            "change-net-medium-over-refund", baseRequest), HttpStatusCode.BadRequest);
        // Decisión D8 del 23/11/2026: la devolución por transferencia sin referencia se acepta y se guarda nula.
        var refund = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "change-net-medium-refund", baseRequest with
            { Payments = [new("EFECTIVO", 900m, null, null),
                new("TRANSFERENCIA", 50m, null, null)] }), HttpStatusCode.Created);
        Assert.Equal(-950m, refund.Difference);
        Assert.Equal(900m, refund.Payments.Single(x => x.MethodCode == "EFECTIVO").Amount);
        Assert.Null(refund.Payments.Single(x => x.MethodCode == "TRANSFERENCIA").Reference);
        var thirdPreparation = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var thirdRequest = new ConfirmSaleChangeRequest(thirdPreparation.SaleVersion, null,
            [new(thirdLine.Id, 1, true)],
            [new(setup.Products.ResidueB, 1, "MINORISTA")], "AUTO",
            [new("EFECTIVO", 67m, null, null)],
            new(SupervisorUser, SupervisorPassword));
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id,
            "change-net-medium-refunded-cash", thirdRequest), HttpStatusCode.BadRequest);
        var third = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "change-net-medium-third", thirdRequest with
            { Payments = [new("TRANSFERENCIA", 67m, null, "AFTER-CASH-REFUND")] }),
            HttpStatusCode.Created);
        Assert.Equal(-67m, third.Difference);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(3, await db.CambioVenta.CountAsync(x => x.VentaId == sale.Id));
        Assert.Equal(4, await db.PagoCambioVenta.CountAsync(x => x.CambioVenta.VentaId == sale.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_SuccessivePartialReturnsAllocateExactHistoricalNetWithoutOverreturn()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-partial-origin",
            new ConfirmSaleRequest(null,
                [new(setup.Products.Standard, 3, "MINORISTA", new("VALOR_FIJO", 2m))],
                null, "Descuento histórico", [new("EFECTIVO", 2998m, 2998m, null)],
                Authorization: new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword))),
            HttpStatusCode.Created);
        var originalLine = Assert.Single(sale.Lines);
        Assert.Equal(2998m, originalLine.NetAmount);
        var expectedNet = new[] { 999m, 999m, 1000m };
        for (var index = 0; index < expectedNet.Length; index++)
        {
            _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
            SetBearer(client, setup.Cashier.AccessToken);
            var preparation = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync(
                $"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
            Assert.Equal(3 - index, Assert.Single(preparation.Lines).RemainingQuantity);
            var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
                $"change-partial-{index}", new ConfirmSaleChangeRequest(preparation.SaleVersion,
                    null, [new(originalLine.Id, 1, true)],
                    [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
                    [new("EFECTIVO", expectedNet[index] - 101m, null, null)],
                    new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
            Assert.Equal(expectedNet[index], change.TotalReturned);
            Assert.Equal(expectedNet[index], change.Details.Single(x => x.Type == "DEVUELTO").NetAmount);
        }

        SetBearer(client, setup.Cashier.AccessToken);
        var finalPreparation = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
        Assert.False(finalPreparation.CanStartChange);
        Assert.Equal(0, Assert.Single(finalPreparation.Lines).RemainingQuantity);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var returns = await db.DetalleCambioVenta.AsNoTracking()
            .Where(x => x.DetalleVentaOrigenId == originalLine.Id && x.TipoDetalle == "DEVUELTO")
            .ToArrayAsync();
        Assert.Equal(3, returns.Sum(x => x.Cantidad));
        Assert.Equal(originalLine.NetAmount, returns.Sum(x => x.SubtotalNeto));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_ReturnedProductCanBeReceivedForLessThanPaidButNeverMore()
    {
        // Auditoría del 23/11/2026: al devolver se puede indicar en cuánto se recibe cada unidad; vacío es «mismo precio».
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-received-origin",
            new ConfirmSaleRequest(null, [new(setup.Products.Standard, 1, "MINORISTA", null)], null, null,
                [new("EFECTIVO", 1000m, 1000m, null)])), HttpStatusCode.Created);
        var line = Assert.Single(sale.Lines);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var preparation = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
        ConfirmSaleChangeRequest Request(decimal received, decimal refund) => new(preparation.SaleVersion, null,
            [new(line.Id, 1, true, received)], [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
            [new("EFECTIVO", refund, null, null)], new(SupervisorUser, SupervisorPassword));

        var tooMuch = await PostChangeAsync(client, sale.Id, "change-received-too-much", Request(1200m, 1099m));
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Contains("más de lo que pagó", await tooMuch.Content.ReadAsStringAsync());

        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id, "change-received-less",
            Request(600m, 499m)), HttpStatusCode.Created);
        Assert.Equal(600m, change.TotalReturned);
        var returned = change.Details.Single(x => x.Type == "DEVUELTO");
        Assert.Equal(600m, returned.NetAmount);
        Assert.Equal(1, Assert.Single((await ReadAsync<SaleChangePreparationDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK)).Lines).ReturnedQuantity);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_RejectedAuthorizationAndInvalidSettlementLeaveSaleAndFinancesIntact()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "sale-change-reject-origin",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var request = new ConfirmSaleChangeRequest(sale.Version, null,
            [new(Assert.Single(sale.Lines).Id, 1, true)],
            [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
            [new("EFECTIVO", 899m, null, null)],
            new(SupervisorUser, SupervisorPassword));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var before = await CaptureChangeStateAsync(db, sale.Id, setup.Products.Standard,
            setup.Products.ResidueA);
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id, "change-bad-refund",
            request with { Payments = [new("TRANSFERENCIA", 899m, null, "NOT-ORIGINAL")] }),
            HttpStatusCode.BadRequest);
        Assert.Equal(before, await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA));
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id, "change-bad-authorizer",
            request with { Authorization = new(SupervisorUser, "incorrect-password") }),
            HttpStatusCode.Unauthorized);
        Assert.Equal(before, await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_PendingCreditReductionUpdatesBalanceWithoutCreatingPayment()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var customerId = await CreateCustomerAsync(setup.Admin.User.Id, true, "CHANGE-CREDIT");
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        var cashState = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"),
            HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "sale-change-credit-origin",
            new ConfirmSaleRequest(customerId,
                [new(setup.Products.Standard, 1, "MINORISTA")], null, null, [],
                "CREDITO", cashState.CurrentShift!.OperatingDate.AddDays(10),
                new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        Assert.NotNull(sale.CreditId);
        var preparation = await ReadAsync<SaleChangePreparationDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/change-preparation"), HttpStatusCode.OK);
        Assert.Equal(sale.CreditId, preparation.CreditId);
        Assert.Equal(1L, preparation.CreditVersion);
        Assert.Equal(1000m, preparation.CreditBalance);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "sale-change-credit-reduction", new ConfirmSaleChangeRequest(sale.Version,
                preparation.CreditVersion,
                [new(Assert.Single(sale.Lines).Id, 1, true)],
                [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO", [],
                new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        Assert.Equal("REDUCIR_CREDITO", change.Treatment);
        Assert.Equal(899m, change.CreditAdjustment);
        Assert.Equal(0m, change.PaidOrRefunded);
        Assert.Empty(change.Payments);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var credit = await db.Credito.AsNoTracking().SingleAsync(x => x.Id == sale.CreditId);
        Assert.Equal(101m, credit.MontoAjustado);
        Assert.Equal(101m, credit.SaldoPendiente);
        Assert.Equal("PENDIENTE", credit.Estado);
        Assert.Equal(1, await db.AjusteCreditoCambio.CountAsync(x => x.CambioVentaId == change.Id));
        Assert.Equal(0, await db.PagoCambioVenta.CountAsync(x => x.CambioVentaId == change.Id));
        Assert.Equal(0, await db.PagoVenta.CountAsync(x => x.VentaId == sale.Id));
        Assert.Equal(0, await db.MovimientoCaja.CountAsync(x => x.CategoriaMovimiento == "CAMBIO_VENTA"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_CreditIncreaseUsesCurrentWholesalePriceAndNoCashMovement()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        // Esta prueba trata del precio mayorista vigente en un aumento de crédito, no del mínimo de unidades.
        await SetWholesaleMinimumAsync(1);
        var customerId = await CreateCustomerAsync(setup.Admin.User.Id, true, "CHANGE-INCREASE");
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-credit-increase-origin",
            new ConfirmSaleRequest(customerId,
                [new(setup.Products.ResidueA, 1, "MINORISTA")], null, null, [],
                "CREDITO", state.CurrentShift!.OperatingDate.AddDays(10),
                new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "change-credit-increase", new ConfirmSaleChangeRequest(sale.Version, 1,
                [new(Assert.Single(sale.Lines).Id, 1, true)],
                [new(setup.Products.Standard, 1, " mayorista ")], "AGREGAR_CREDITO", [],
                new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        Assert.Equal(101m, change.TotalReturned);
        Assert.Equal(900m, change.TotalDelivered);
        Assert.Equal(799m, change.Difference);
        Assert.Equal("AGREGAR_CREDITO", change.Treatment);
        Assert.Equal(799m, change.CreditAdjustment);
        Assert.Empty(change.Payments);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(900m, await db.Credito.Where(x => x.Id == sale.CreditId)
            .Select(x => x.SaldoPendiente).SingleAsync());
        Assert.Equal(0, await db.MovimientoCaja.CountAsync(x => x.CategoriaMovimiento == "CAMBIO_VENTA"));
        // El Cajero entregó a precio mayorista: queda la autorización del Supervisor con la correlación del cambio.
        var changeAuthorization = await db.AutorizacionOperacion.AsNoTracking().SingleAsync(x => x.TipoOperacion == "CAMBIO_VENTA");
        var wholesaleAuthorization = await db.AutorizacionOperacion.AsNoTracking().SingleAsync(x =>
            x.TipoOperacion == "PRECIO_MAYORISTA" && x.CorrelacionId == changeAuthorization.CorrelacionId);
        Assert.Equal(setup.Users.Supervisor.Id, wholesaleAuthorization.UsuarioAutorizadorId);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_CashierWholesaleDelivery_NeedsTheConfiguredMinimumAndADiscountAuthorizer()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var customerId = await CreateCustomerAsync(setup.Admin.User.Id, true, "CHANGE-WHOLESALE");
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-wholesale-origin",
            new ConfirmSaleRequest(customerId, [new(setup.Products.ResidueA, 1, "MINORISTA")], null, null, [],
                "CREDITO", state.CurrentShift!.OperatingDate.AddDays(10),
                new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        ConfirmSaleChangeRequest Deliver(int quantity) => new(sale.Version, 1,
            [new(Assert.Single(sale.Lines).Id, 1, true)],
            [new(setup.Products.Standard, quantity, "MAYORISTA")], "AGREGAR_CREDITO", [],
            new(SupervisorUser, SupervisorPassword));

        // Con el mínimo inicial (12), entregar 2 unidades a precio mayorista se rechaza.
        var belowMinimum = await PostChangeAsync(client, sale.Id, "change-wholesale-two", Deliver(2));
        await AssertProblemAsync(belowMinimum, HttpStatusCode.BadRequest);
        Assert.Contains("12", await belowMinimum.Content.ReadAsStringAsync());

        // Con mínimo 2, un autorizador sin permiso para autorizar descuentos no basta.
        await SetWholesaleMinimumAsync(2);
        SetBearer(client, setup.Admin.AccessToken);
        var discountPermissionId = await GetPermissionIdAsync(client, PermissionCodes.SalesDiscountAuthorize);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{setup.Users.Supervisor.Id}/permissions/{discountPermissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        SetBearer(client, setup.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostChangeAsync(client, sale.Id, "change-wholesale-no-permission", Deliver(2))).StatusCode);

        SetBearer(client, setup.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{setup.Users.Supervisor.Id}/permissions/{discountPermissionId}",
            new SetUserPermissionRequest(true))).StatusCode);
        SetBearer(client, setup.Cashier.AccessToken);
        var change = await ReadAsync<SaleChangeDto>(
            await PostChangeAsync(client, sale.Id, "change-wholesale-authorized", Deliver(2)), HttpStatusCode.Created);
        Assert.Equal(1_800m, change.TotalDelivered);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "PRECIO_MAYORISTA" &&
            x.UsuarioAutorizadorId == setup.Users.Supervisor.Id && x.UsuarioSolicitanteId == setup.Users.Cashier.Id));
    }

    private async Task SetWholesaleMinimumAsync(int minimum)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        (await db.Establecimiento.SingleAsync()).MinimoUnidadesMayorista = minimum;
        await db.SaveChangesAsync();
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_CreditWithAbonoReducesBalanceAndRefundsExcessByChosenMethod()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var customerId = await CreateCustomerAsync(setup.Admin.User.Id, true, "CHANGE-EXCESS");
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "sale-change-excess-origin",
            new ConfirmSaleRequest(customerId,
                [new(setup.Products.Standard, 1, "MINORISTA")], null, null, [],
                "CREDITO", state.CurrentShift!.OperatingDate.AddDays(10),
                new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        using var payment = new HttpRequestMessage(HttpMethod.Post,
            $"/api/credits/{sale.CreditId}/payments")
        { Content = JsonContent.Create(new ApplyCreditPaymentRequest(1,
            [new("EFECTIVO", 950m, 950m, null)])) };
        payment.Headers.Add("Idempotency-Key", "sale-change-excess-abono");
        var abono = await ReadAsync<CreditPaymentOperationDto>(await client.SendAsync(payment),
            HttpStatusCode.Created);
        Assert.Equal(50m, abono.Balance);
        Assert.Equal("PENDIENTE", abono.State);
        Assert.Equal(2L, abono.Version);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var change = await ReadAsync<SaleChangeDto>(await PostChangeAsync(client, sale.Id,
            "sale-change-credit-excess", new ConfirmSaleChangeRequest(sale.Version, abono.Version,
                [new(Assert.Single(sale.Lines).Id, 1, true)],
                [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
                [new("NEQUI", 849m, null, "EXCESS-REFUND")],
                new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        Assert.Equal("REDUCIR_CREDITO_Y_DEVOLVER", change.Treatment);
        Assert.Equal(50m, change.CreditAdjustment);
        Assert.Equal(849m, change.PaidOrRefunded);
        Assert.Equal("NEQUI", Assert.Single(change.Payments).MethodCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var credit = await db.Credito.AsNoTracking().SingleAsync(x => x.Id == sale.CreditId);
        Assert.Equal(0m, credit.SaldoPendiente);
        Assert.Equal("PAGADO", credit.Estado);
        Assert.Equal(1, await db.AjusteCreditoCambio.CountAsync(x => x.CambioVentaId == change.Id));
        Assert.Equal(1, await db.PagoCambioVenta.CountAsync(x => x.CambioVentaId == change.Id));
        Assert.Equal(1, await db.MovimientoCaja.CountAsync(x => x.CategoriaMovimiento == "CAMBIO_VENTA"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_RevokedAuthorizerAndExecutorPermissionsRejectWithoutFinancialEffects()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-permission-origin",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        var request = new ConfirmSaleChangeRequest(sale.Version, null,
            [new(Assert.Single(sale.Lines).Id, 1, true)],
            [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
            [new("EFECTIVO", 899m, null, null)],
            new(SupervisorUser, SupervisorPassword));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var before = await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA);

        // The Administrator revokes the authorizer's permission: the Cajero's change is rejected.
        SetBearer(client, setup.Admin.AccessToken);
        var authPermissionId = await GetPermissionIdAsync(client, PermissionCodes.SalesChangeAuthorize);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{setup.Users.Supervisor.Id}/permissions/{authPermissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        SetBearer(client, setup.Cashier.AccessToken);
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id, "change-revoked-authorizer",
            request), HttpStatusCode.Forbidden);
        Assert.Equal(before, await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA));

        // Then the executor's own permission is revoked: the request is rejected before any effect.
        SetBearer(client, setup.Admin.AccessToken);
        var executionPermissionId = await GetPermissionIdAsync(client, PermissionCodes.SalesCreate);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{setup.Users.Cashier.Id}/permissions/{executionPermissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        var refreshed = await LoginAsync(client, CashierUser, CashierPassword);
        SetBearer(client, refreshed.AccessToken);
        await AssertProblemAsync(await PostChangeAsync(client, sale.Id, "change-revoked-executor",
            request), HttpStatusCode.Forbidden);
        Assert.Equal(before, await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_DeferredCommitFailureRollsBackInventoryCashNumberingAndSession()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-deferred-origin",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        var session = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var before = await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA);
        var authorizationBefore = await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "CAMBIO_VENTA");
        var auditBefore = await db.EventoAuditoria.CountAsync(x => x.Accion == "SALECHANGED");
        var idempotencyBefore = await db.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "CAMBIAR_VENTA");
        var sequenceBefore = await db.ConsecutivoDocumento.Where(x => x.TipoDocumento == "CAMBIO_VENTA")
            .Select(x => x.UltimoNumero).SingleAsync();
        await InstallDeferredChangeFailureAsync();
        SetBearer(client, setup.Cashier.AccessToken);
        var response = await PostChangeAsync(client, sale.Id, "change-forced-commit-failure",
            new ConfirmSaleChangeRequest(sale.Version, null,
                [new(Assert.Single(sale.Lines).Id, 1, true)],
                [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
                [new("EFECTIVO", 899m, null, null)],
                new(SupervisorUser, SupervisorPassword)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await DeferredChangeFailureObservedAsync());
        Assert.Equal(before, await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA));
        Assert.Equal(authorizationBefore,
            await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "CAMBIO_VENTA"));
        Assert.Equal(auditBefore, await db.EventoAuditoria.CountAsync(x => x.Accion == "SALECHANGED"));
        Assert.Equal(idempotencyBefore,
            await db.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "CAMBIAR_VENTA"));
        Assert.Equal(sequenceBefore, await db.ConsecutivoDocumento.Where(x => x.TipoDocumento == "CAMBIO_VENTA")
            .Select(x => x.UltimoNumero).SingleAsync());
        Assert.Equal("ACTIVA", await db.SesionOperador.Where(x => x.Id == session.Id)
            .Select(x => x.Estado).SingleAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleChange_CreditAndRefundDeferredFailureRollsBackAllChangeEffects()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var customerId = await CreateCustomerAsync(setup.Admin.User.Id, true, "CHANGE-ROLLBACK");
        await ConfigureChangeNumberingAsync(client, setup.Admin.AccessToken);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "change-credit-rollback-origin",
            new ConfirmSaleRequest(customerId,
                [new(setup.Products.Standard, 1, "MINORISTA")], null, null, [],
                "CREDITO", state.CurrentShift!.OperatingDate.AddDays(10),
                new(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        using var payment = new HttpRequestMessage(HttpMethod.Post,
            $"/api/credits/{sale.CreditId}/payments")
        { Content = JsonContent.Create(new ApplyCreditPaymentRequest(1,
            [new("EFECTIVO", 950m, 950m, null)])) };
        payment.Headers.Add("Idempotency-Key", "change-credit-rollback-abono");
        var abono = await ReadAsync<CreditPaymentOperationDto>(await client.SendAsync(payment),
            HttpStatusCode.Created);
        Assert.Equal(50m, abono.Balance);
        Assert.Equal("PENDIENTE", abono.State);
        Assert.Equal(2L, abono.Version);
        var session = await StartSessionAsync(client, setup.Admin.AccessToken, setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var before = await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA);
        var creditBefore = await db.Credito.AsNoTracking().Where(x => x.Id == sale.CreditId)
            .Select(x => new { x.MontoAjustado, x.SaldoPendiente, x.Estado, x.Version }).SingleAsync();
        var adjustmentsBefore = await db.AjusteCreditoCambio.CountAsync();
        var authorizationBefore = await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "CAMBIO_VENTA");
        var auditBefore = await db.EventoAuditoria.CountAsync(x => x.Accion == "SALECHANGED");
        var idempotencyBefore = await db.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "CAMBIAR_VENTA");
        var sequenceBefore = await db.ConsecutivoDocumento.Where(x => x.TipoDocumento == "CAMBIO_VENTA")
            .Select(x => x.UltimoNumero).SingleAsync();
        await InstallDeferredChangeFailureAsync();
        SetBearer(client, setup.Cashier.AccessToken);
        var response = await PostChangeAsync(client, sale.Id, "change-credit-forced-failure",
            new ConfirmSaleChangeRequest(sale.Version, abono.Version,
                [new(Assert.Single(sale.Lines).Id, 1, true)],
                [new(setup.Products.ResidueA, 1, "MINORISTA")], "AUTO",
                [new("NEQUI", 849m, null, "ROLLBACK-NEQUI")],
                new(SupervisorUser, SupervisorPassword)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await DeferredChangeFailureObservedAsync());
        Assert.Equal(before, await CaptureChangeStateAsync(db, sale.Id,
            setup.Products.Standard, setup.Products.ResidueA));
        Assert.Equal(creditBefore, await db.Credito.AsNoTracking().Where(x => x.Id == sale.CreditId)
            .Select(x => new { x.MontoAjustado, x.SaldoPendiente, x.Estado, x.Version }).SingleAsync());
        Assert.Equal(adjustmentsBefore, await db.AjusteCreditoCambio.CountAsync());
        Assert.Equal(authorizationBefore,
            await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "CAMBIO_VENTA"));
        Assert.Equal(auditBefore, await db.EventoAuditoria.CountAsync(x => x.Accion == "SALECHANGED"));
        Assert.Equal(idempotencyBefore,
            await db.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "CAMBIAR_VENTA"));
        Assert.Equal(sequenceBefore, await db.ConsecutivoDocumento.Where(x => x.TipoDocumento == "CAMBIO_VENTA")
            .Select(x => x.UltimoNumero).SingleAsync());
        Assert.Equal("ACTIVA", await db.SesionOperador.Where(x => x.Id == session.Id)
            .Select(x => x.Estado).SingleAsync());
    }

    private async Task InstallDeferredChangeFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE ventas.test_change_commit_observed;
            CREATE FUNCTION ventas.test_fail_change_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF (SELECT count(*) FROM ventas.detalle_cambio_venta WHERE cambio_venta_id = NEW.id) = 2
                AND (SELECT count(*) FROM ventas.pago_cambio_venta WHERE cambio_venta_id = NEW.id) = 1
                AND (SELECT count(*) FROM compras.movimiento_inventario WHERE cambio_venta_id = NEW.id) = 2
                AND EXISTS (SELECT 1 FROM caja.movimiento_caja WHERE categoria_movimiento = 'CAMBIO_VENTA'
                  AND correlacion_id = NEW.correlacion_id)
                AND EXISTS (SELECT 1 FROM auditoria.operacion_idempotente
                  WHERE entidad_id = NEW.venta_id AND tipo_operacion = 'CAMBIAR_VENTA')
                AND EXISTS (SELECT 1 FROM seguridad.autorizacion_operacion
                  WHERE id = NEW.autorizacion_operacion_id AND estado = 'UTILIZADA')
                AND (NEW.credito_id IS NULL OR EXISTS (SELECT 1 FROM ventas.ajuste_credito_cambio
                  WHERE cambio_venta_id = NEW.id))
                AND EXISTS (SELECT 1 FROM caja.sesion_operador
                  WHERE id = NEW.sesion_operador_id AND estado = 'CERRADA')
              THEN
                PERFORM nextval('ventas.test_change_commit_observed');
              END IF;
              RAISE EXCEPTION 'Forced isolated change commit failure' USING ERRCODE = '23514';
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_change_commit
              AFTER INSERT ON ventas.cambio_venta DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION ventas.test_fail_change_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredChangeFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT is_called FROM ventas.test_change_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// The CAM consecutive is created by the installation itself: this only selects the Administrator and checks
    /// that it is there, ready to number the first change.
    /// </summary>
    private static async Task ConfigureChangeNumberingAsync(HttpClient client, string adminToken)
    {
        SetBearer(client, adminToken);
        var numbering = await ReadAsync<SalesDocumentNumberingDto>(
            await client.GetAsync("/api/document-numbering/sale-changes"), HttpStatusCode.OK);
        Assert.Equal("CAM", numbering.Prefix);
        Assert.Equal(0, numbering.LastNumber);
    }

    private static async Task<HttpResponseMessage> PostChangeAsync(HttpClient client,
        Guid saleId, string key, ConfirmSaleChangeRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/changes")
        { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private static async Task<ChangeState> CaptureChangeStateAsync(OfficialControlPlusDbContext db,
        Guid saleId, Guid returnedProductId, Guid deliveredProductId)
    {
        db.ChangeTracker.Clear();
        return new ChangeState(
            await db.Venta.CountAsync(x => x.Id == saleId),
            await db.PagoVenta.CountAsync(x => x.VentaId == saleId),
            await db.CambioVenta.CountAsync(x => x.VentaId == saleId),
            await db.PagoCambioVenta.CountAsync(x => x.CambioVenta.VentaId == saleId),
            await db.MovimientoInventario.CountAsync(x => x.CambioVentaId != null &&
                x.CambioVenta!.VentaId == saleId),
            await db.MovimientoCaja.CountAsync(x => x.CategoriaMovimiento == "CAMBIO_VENTA"),
            await db.Producto.Where(x => x.Id == returnedProductId).Select(x => x.StockActual).SingleAsync(),
            await db.Producto.Where(x => x.Id == deliveredProductId).Select(x => x.StockActual).SingleAsync(),
            await db.Venta.Where(x => x.Id == saleId).Select(x => x.Version).SingleAsync());
    }

    private sealed record ChangeState(int Sales, int SalePayments, int Changes,
        int ChangePayments, int ChangeInventoryMovements, int ChangeCashMovements,
        int ReturnedStock, int DeliveredStock, long SaleVersion);
}
