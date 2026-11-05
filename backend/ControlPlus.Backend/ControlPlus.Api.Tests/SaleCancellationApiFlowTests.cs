using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task SaleCancellation_CreditWithPayment_IsRejectedWithoutReversingPayment()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var customerId = await CreateCustomerAsync(scenario.Admin.User.Id, true, "VOID-PAID-CREDIT");
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var dueDate = (await db.TurnoCaja.AsNoTracking().SingleAsync()).FechaOperativa.AddDays(1);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "void-paid-credit-sale",
            new ConfirmSaleRequest(customerId, [new(scenario.Products.Standard, 1, "MINORISTA")],
                null, null, [], "CREDITO", dueDate,
                new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword))),
            HttpStatusCode.Created);
        SetBearer(client, scenario.Admin.AccessToken);
        await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/credit-payments",
            new CreateSalesDocumentNumberingRequest("ABO", 1)), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        using (var paymentRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/credits/{sale.CreditId}/payments")
        {
            Content = JsonContent.Create(new ApplyCreditPaymentRequest(1,
                [new CreditPaymentMethodRequest("EFECTIVO", 100m, 100m, null)]))
        })
        {
            paymentRequest.Headers.Add("Idempotency-Key", "void-credit-abono");
            await ReadAsync<CreditPaymentOperationDto>(await client.SendAsync(paymentRequest), HttpStatusCode.Created);
        }
        var reasons = await ReadAsync<IReadOnlyCollection<SaleCancellationReasonDto>>(
            await client.PostAsync("/api/sales/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        var stock = (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual;
        var cashCount = await db.MovimientoCaja.CountAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await PostSaleCancellationAsync(client,
            sale.Id, "void-credit-with-abono-key", new CancelSaleRequest(sale.Version,
                reasons.Single(x => x.Code == "ERROR_REGISTRO").Id, null,
                new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword)))).StatusCode);
        Assert.Equal("CONFIRMADA", (await db.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id)).Estado);
        Assert.Equal("PENDIENTE", (await db.Credito.AsNoTracking().SingleAsync(x => x.Id == sale.CreditId)).Estado);
        Assert.Equal(stock, (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual);
        Assert.Equal(cashCount, await db.MovimientoCaja.CountAsync());
        Assert.False(await db.AnulacionVenta.AnyAsync(x => x.VentaId == sale.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleCancellation_BadAuthorizerPassword_KeepsOnlyFailureCountAndAudit()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "void-bad-auth-sale",
            CashRequest(scenario.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        SetBearer(client, scenario.Admin.AccessToken);
        var reasons = await ReadAsync<IReadOnlyCollection<SaleCancellationReasonDto>>(
            await client.PostAsync("/api/sales/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var before = (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual;
        var cashBefore = await db.MovimientoCaja.CountAsync();
        var request = new CancelSaleRequest(sale.Version,
            reasons.Single(x => x.Code == "ERROR_REGISTRO").Id, null,
            new SaleCreditAuthorizationRequest(SupervisorUser, "invalid-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostSaleCancellationAsync(client,
            sale.Id, "void-bad-auth-key", request)).StatusCode);
        Assert.False(await db.AnulacionVenta.AnyAsync(x => x.VentaId == sale.Id));
        Assert.Equal(before, (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual);
        Assert.Equal(cashBefore, await db.MovimientoCaja.CountAsync());
        Assert.False(await db.OperacionIdempotente.AnyAsync(x => x.ClaveIdempotencia == "void-bad-auth-key"));
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.Accion == "SALECANCELLATIONAUTHORIZATIONREJECTED"));
        Assert.Equal(1, (await db.Usuario.AsNoTracking().SingleAsync(x => x.Id == scenario.Users.Supervisor.Id)).IntentosFallidos);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostSaleCancellationAsync(client,
            sale.Id, "void-self-auth-key", request with
            {
                Authorization = new SaleCreditAuthorizationRequest(AdminUser, AdminPassword)
            })).StatusCode);
        Assert.False(await db.AnulacionVenta.AnyAsync(x => x.VentaId == sale.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleCancellation_DeferredCommitFailure_RollsBackAllEffects()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "void-rollback-sale",
            CashRequest(scenario.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        SetBearer(client, scenario.Admin.AccessToken);
        var reasons = await ReadAsync<IReadOnlyCollection<SaleCancellationReasonDto>>(
            await client.PostAsync("/api/sales/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var beforeStock = (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual;
        var beforeMovements = await db.MovimientoCaja.CountAsync();
        var beforeInventory = await db.MovimientoInventario.CountAsync();
        var beforeAudit = await db.EventoAuditoria.CountAsync();
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                CREATE FUNCTION ventas.test_void_fail_commit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.estado = 'ANULADA' THEN
                        RAISE EXCEPTION 'forced cancellation failure' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE CONSTRAINT TRIGGER zz_test_void_fail_commit
                    AFTER UPDATE ON ventas.venta DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION ventas.test_void_fail_commit();
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        var request = new CancelSaleRequest(sale.Version,
            reasons.Single(x => x.Code == "ERROR_REGISTRO").Id, null,
            new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword));
        Assert.Equal(HttpStatusCode.Conflict, (await PostSaleCancellationAsync(client,
            sale.Id, "void-rollback-key", request)).StatusCode);
        Assert.Equal("CONFIRMADA", (await db.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id)).Estado);
        Assert.Equal(beforeStock, (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual);
        Assert.Equal(beforeMovements, await db.MovimientoCaja.CountAsync());
        Assert.Equal(beforeInventory, await db.MovimientoInventario.CountAsync());
        Assert.Equal(beforeAudit, await db.EventoAuditoria.CountAsync());
        Assert.False(await db.AnulacionVenta.AnyAsync(x => x.VentaId == sale.Id));
        Assert.Equal(1, await db.PagoVenta.CountAsync(x => x.VentaId == sale.Id && x.Estado == "APLICADO"));
        Assert.False(await db.OperacionIdempotente.AnyAsync(x => x.ClaveIdempotencia == "void-rollback-key"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleCancellation_CreditWithoutPayments_ClearsBalanceWithoutCashMovement()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var customerId = await CreateCustomerAsync(scenario.Admin.User.Id, true, "VOID-CREDIT");
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var dueDate = (await db.TurnoCaja.AsNoTracking().SingleAsync()).FechaOperativa.AddDays(1);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "void-credit-sale",
            new ConfirmSaleRequest(customerId, [new(scenario.Products.Standard, 1, "MINORISTA")],
                null, null, [], "CREDITO", dueDate,
                new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword))),
            HttpStatusCode.Created);
        var cashBefore = await db.MovimientoCaja.CountAsync();
        SetBearer(client, scenario.Admin.AccessToken);
        var reasons = await ReadAsync<IReadOnlyCollection<SaleCancellationReasonDto>>(
            await client.PostAsync("/api/sales/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        var result = await ReadAsync<CancelSaleDto>(await PostSaleCancellationAsync(client,
            sale.Id, "void-credit-key", new CancelSaleRequest(sale.Version,
                reasons.Single(x => x.Code == "ERROR_REGISTRO").Id, null,
                new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword))), HttpStatusCode.OK);
        Assert.Equal("CREDITO", result.SaleType);
        Assert.Equal(0m, result.ReversedAmount);
        Assert.Equal(cashBefore, await db.MovimientoCaja.CountAsync());
        var credit = await db.Credito.AsNoTracking().SingleAsync(x => x.Id == sale.CreditId);
        Assert.Equal("ANULADO", credit.Estado);
        Assert.Equal(0m, credit.MontoAjustado);
        Assert.Equal(0m, credit.SaldoPendiente);
        Assert.Equal(100, (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleCancellation_CashMixedPayment_ReversesSameMethodsRestoresStockAndReplaysOnce()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "void-cash-sale",
            new ConfirmSaleRequest(null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 400m, 400m, null),
                 new("TRANSFERENCIA", 600m, null, "VOID-SALE-TRANSFER")])), HttpStatusCode.Created);

        SetBearer(client, scenario.Supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostSaleCancellationAsync(client,
            sale.Id, "void-supervisor-without-auth", new CancelSaleRequest(
                sale.Version, Guid.CreateVersion7(), null, null))).StatusCode);

        SetBearer(client, scenario.Admin.AccessToken);
        var reasons = await ReadAsync<IReadOnlyCollection<SaleCancellationReasonDto>>(
            await client.PostAsync("/api/sales/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        Assert.Equal(3, reasons.Count);
        var repeatedReasons = await ReadAsync<IReadOnlyCollection<SaleCancellationReasonDto>>(
            await client.PostAsync("/api/sales/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        Assert.Equal(reasons.Select(x => x.Id), repeatedReasons.Select(x => x.Id));
        var reason = reasons.Single(x => x.Code == "ERROR_REGISTRO");
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var before = await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard);
        var request = new CancelSaleRequest(sale.Version, reason.Id, null, null);
        var result = await ReadAsync<CancelSaleDto>(await PostSaleCancellationAsync(client,
            sale.Id, "void-cash-key", request), HttpStatusCode.OK);
        Assert.Equal("ANULADA", result.State);
        Assert.Equal(1000m, result.ReversedAmount);
        Assert.Equal(before.StockActual + 1,
            (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.Products.Standard)).StockActual);
        Assert.Equal(2, await db.PagoVenta.CountAsync(x => x.VentaId == sale.Id && x.Estado == "ANULADO"));
        var original = await db.MovimientoCaja.AsNoTracking().SingleAsync(x => x.PagoVenta.Any(p => p.VentaId == sale.Id));
        var reversed = await db.MovimientoCaja.AsNoTracking().SingleAsync(x => x.MovimientoRevertidoId == original.Id);
        Assert.Equal(sale.Total, reversed.Valor);
        Assert.Equal("REVERSO", reversed.Tipo);
        var cash = scope.ServiceProvider.GetRequiredService<ICashRepository>();
        var expectedByMethod = await cash.GetExpectedAmountsAsync(reversed.TurnoCajaId);
        Assert.Equal(0m, expectedByMethod.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        Assert.Equal(0m, expectedByMethod.Single(x => x.Code == "TRANSFERENCIA").ExpectedAmount);
        Assert.Single(await db.AnulacionVenta.Where(x => x.VentaId == sale.Id).ToArrayAsync());
        var cancellationAuthorizationId = (await db.AnulacionVenta.AsNoTracking()
            .SingleAsync(c => c.VentaId == sale.Id)).AutorizacionOperacionId;
        var selfAuthorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == cancellationAuthorizationId);
        Assert.Equal(scenario.Admin.User.Id, selfAuthorization.UsuarioSolicitanteId);
        Assert.Equal(scenario.Admin.User.Id, selfAuthorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", selfAuthorization.MetodoAutenticacion);
        Assert.Null(selfAuthorization.CredencialAutorizadorId);
        Assert.Contains("sin segundo autorizador", selfAuthorization.DescripcionOperacion);
        var selfAudit = await db.EventoAuditoria.AsNoTracking()
            .SingleAsync(x => x.Accion == "SALECANCELLED" && x.EntidadId == sale.Id);
        Assert.Contains("ADMINISTRATOR_SELF", selfAudit.DatosNuevos);
        Assert.Single(await db.MovimientoInventario.Where(x => x.AnulacionVentaId == result.CancellationId).ToArrayAsync());
        var operationCount = await db.OperacionIdempotente.CountAsync();
        var movementCount = await db.MovimientoCaja.CountAsync();
        var inventoryCount = await db.MovimientoInventario.CountAsync();
        var replay = await ReadAsync<CancelSaleDto>(await PostSaleCancellationAsync(client,
            sale.Id, "void-cash-key", request with { Authorization = null }), HttpStatusCode.OK);
        Assert.Equal(result, replay);
        Assert.Equal(operationCount, await db.OperacionIdempotente.CountAsync());
        Assert.Equal(movementCount, await db.MovimientoCaja.CountAsync());
        Assert.Equal(inventoryCount, await db.MovimientoInventario.CountAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await PostSaleCancellationAsync(client,
            sale.Id, "void-cash-different", request)).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SaleCancellation_CashierOrStaleVersion_IsRejectedWithoutEffects()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "void-denied-sale",
            CashRequest(scenario.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        var denied = new CancelSaleRequest(sale.Version, Guid.CreateVersion7(), null,
            new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostSaleCancellationAsync(client,
            sale.Id, "void-denied-key", denied)).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        var reasons = await ReadAsync<IReadOnlyCollection<SaleCancellationReasonDto>>(
            await client.PostAsync("/api/sales/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        var stale = denied with { ExpectedVersion = sale.Version + 1,
            ReasonId = reasons.Single(x => x.Code == "ERROR_REGISTRO").Id };
        Assert.Equal(HttpStatusCode.Conflict, (await PostSaleCancellationAsync(client,
            sale.Id, "void-stale-key", stale)).StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal("CONFIRMADA", (await db.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id)).Estado);
        Assert.False(await db.AnulacionVenta.AnyAsync(x => x.VentaId == sale.Id));
        Assert.False(await db.MovimientoCaja.AnyAsync(x => x.MovimientoRevertidoId != null));
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.SalesVoid);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Admin.User.Id}/permissions/{permissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        var revokedAdmin = await LoginAsync(client, AdminUser, AdminPassword);
        SetBearer(client, revokedAdmin.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostSaleCancellationAsync(client,
            sale.Id, "void-revoked-key", stale with { ExpectedVersion = sale.Version })).StatusCode);
        Assert.False(await db.AnulacionVenta.AnyAsync(x => x.VentaId == sale.Id));
    }

    private static async Task<HttpResponseMessage> PostSaleCancellationAsync(HttpClient client,
        Guid saleId, string key, CancelSaleRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/cancel")
        { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }
}
