using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class LayawayCreateApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task Delivery_PaidLayawayWithoutOpenShiftOrSession_DecreasesStockAndReservationOnceWithoutFinancialEffects()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "delivery-normal-create", Request(scenario, [new("EFECTIVO", 2002m, 2002m, null)]) with
            { Lines = [new(scenario.ProductId, 1, "MINORISTA"), new(scenario.ProductId, 1, "MINORISTA")] }),
            HttpStatusCode.Created);
        await CloseDeliveryShiftAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var before = await CaptureDeliveryStateAsync(scenario, created.Id);

        var delivered = await ReadAsync<LayawayDeliveryDto>(await PostDeliveryAsync(client,
            created.Id, "delivery-normal", created.Version), HttpStatusCode.OK);

        Assert.Equal(created.Id, delivered.LayawayId);
        Assert.Equal(created.VisibleNumber, delivered.Number);
        Assert.Equal("ENTREGADO", delivered.State);
        Assert.Equal(created.Version + 1, delivered.Version);
        Assert.Equal(scenario.Cashier.User.Id, delivered.DeliveredByUserId);
        Assert.NotEqual(default, delivered.DeliveredAtUtc);
        Assert.NotEqual(Guid.Empty, delivered.CorrelationId);
        var deliveredProduct = Assert.Single(delivered.Products);
        Assert.Equal(scenario.ProductId, deliveredProduct.ProductId);
        Assert.Equal("LAYAWAY-CREATE-ITEM", deliveredProduct.ProductCode);
        Assert.Equal("Producto ficticio de apartado", deliveredProduct.ProductName);
        Assert.Equal(Assert.Single(created.Lines).MeasurementUnit, deliveredProduct.Unit);
        Assert.Equal(2, deliveredProduct.Quantity);
        var after = await CaptureDeliveryStateAsync(scenario, created.Id);
        Assert.Equal(before.Common.ActualStock - 2, after.Common.ActualStock);
        Assert.Equal(before.Common.ReservedStock - 2, after.Common.ReservedStock);
        Assert.Equal(before.Common.InventoryMovements + 1, after.Common.InventoryMovements);
        Assert.Equal(before.Common.IdempotencyOperations + 1, after.Common.IdempotencyOperations);
        Assert.Equal(before.Common.Audits + 1, after.Common.Audits);
        Assert.Equal(before.ProductVersion + 1, after.ProductVersion);
        AssertFinancialStateUnchanged(before, after);
        Assert.Null(after.SaleId);
        Assert.Equal(delivered.DeliveredAtUtc.UtcDateTime, after.DeliveredAt);
        Assert.Equal(delivered.DeliveredByUserId, after.DeliveredBy);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var movement = await db.MovimientoInventario.AsNoTracking().SingleAsync(x =>
            x.ApartadoId == created.Id && x.TipoMovimiento == "ENTREGA_APARTADO");
        Assert.Equal(-2, movement.CantidadStock);
        Assert.Equal(-2, movement.CantidadReservada);
        Assert.Equal(before.Common.ActualStock, movement.StockAnterior);
        Assert.Equal(after.Common.ActualStock, movement.StockPosterior);
        Assert.Equal(before.Common.ReservedStock, movement.ReservadoAnterior);
        Assert.Equal(after.Common.ReservedStock, movement.ReservadoPosterior);
        Assert.Equal(before.AverageCost, movement.CostoPromedioAnterior);
        Assert.Equal(before.AverageCost, movement.CostoPromedioPosterior);
        Assert.Equal(delivered.CorrelationId, movement.CorrelacionId);
        Assert.Equal(scenario.Cashier.User.Id, movement.UsuarioId);
        Assert.Null(movement.SesionOperadorId);
        Assert.Null(movement.VentaId);
        var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x =>
            x.EntidadId == created.Id && x.Accion == "LAYAWAYDELIVERED");
        Assert.Equal(delivered.CorrelationId, audit.CorrelacionId);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Delivery_ExpiredPaidLayaway_IsDeliveredAndDetailShowsDeliveryWithoutCosts()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var created = await CreatePaidForDeliveryAsync(client, scenario, "delivery-expired-create");
        long version;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var layaway = await db.Apartado.SingleAsync(x => x.Id == created.Id);
            layaway.FechaLimite = scenario.Shift.OperatingDate.AddDays(-1);
            await db.SaveChangesAsync();
            version = layaway.Version;
        }
        var before = await CaptureDeliveryStateAsync(scenario, created.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await PostDeliveryAsync(client,
            created.Id, "delivery-expired-stale", created.Version)).StatusCode);
        Assert.Equal(before, await CaptureDeliveryStateAsync(scenario, created.Id));
        var delivered = await ReadAsync<LayawayDeliveryDto>(await PostDeliveryAsync(client,
            created.Id, "delivery-expired", version), HttpStatusCode.OK);
        var beforeRead = await CaptureDeliveryStateAsync(scenario, created.Id);
        using var response = await client.GetAsync($"/api/layaways/{created.Id}");
        var body = await response.Content.ReadAsStringAsync();
        var detail = await ReadAsync<LayawayDetailDto>(response, HttpStatusCode.OK);
        Assert.Equal("ENTREGADO", detail.Layaway.State);
        Assert.NotNull(detail.Delivery);
        Assert.Equal(delivered.LayawayId, detail.Delivery.LayawayId);
        Assert.Equal(delivered.Number, detail.Delivery.Number);
        Assert.Equal(delivered.State, detail.Delivery.State);
        Assert.Equal(delivered.Version, detail.Delivery.Version);
        Assert.Equal(delivered.DeliveredAtUtc, detail.Delivery.DeliveredAtUtc);
        Assert.Equal(delivered.DeliveredByUserId, detail.Delivery.DeliveredByUserId);
        Assert.Equal(delivered.CorrelationId, detail.Delivery.CorrelationId);
        Assert.Equal(delivered.Products, detail.Delivery.Products);
        Assert.DoesNotContain("cost", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("costo", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeRead, await CaptureDeliveryStateAsync(scenario, created.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Delivery_ExactReplayReturnsPersistedResponse_AndIncompatibleKeyOrStaleVersionConflictWithoutWrites()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var created = await CreatePaidForDeliveryAsync(client, scenario, "delivery-replay-create");
        var result = await ReadAsync<LayawayDeliveryDto>(await PostDeliveryAsync(client,
            created.Id, "delivery-replay", created.Version), HttpStatusCode.OK);
        var after = await CaptureDeliveryStateAsync(scenario, created.Id);

        var replay = await ReadAsync<LayawayDeliveryDto>(await PostDeliveryAsync(client,
            created.Id, "delivery-replay", created.Version), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
        Assert.Equal(after, await CaptureDeliveryStateAsync(scenario, created.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await PostDeliveryAsync(client,
            created.Id, "delivery-replay", result.Version)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostDeliveryAsync(client,
            created.Id, "delivery-stale-new-key", created.Version)).StatusCode);
        Assert.Equal(after, await CaptureDeliveryStateAsync(scenario, created.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task DeliveryReplay_RevokedPermissionRejectsFreshJwtAndInvalidatedJwtWithoutWrites()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var created = await CreatePaidForDeliveryAsync(client, scenario, "delivery-permission-create");
        _ = await ReadAsync<LayawayDeliveryDto>(await PostDeliveryAsync(client,
            created.Id, "delivery-permission", created.Version), HttpStatusCode.OK);

        SetBearer(client, scenario.Admin.AccessToken);
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.LayawayManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Cashier.User.Id}/permissions/{permissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        var cashierWithoutPermission = await LoginAsync(client, CashierName, CashierPassword);
        var before = await CaptureDeliveryStateAsync(scenario, created.Id);

        using var invalidated = CreateClient(scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostDeliveryAsync(invalidated,
            created.Id, "delivery-permission", created.Version)).StatusCode);
        SetBearer(client, cashierWithoutPermission.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostDeliveryAsync(client,
            created.Id, "delivery-permission", created.Version)).StatusCode);
        using var anonymous = CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostDeliveryAsync(anonymous,
            created.Id, "delivery-permission", created.Version)).StatusCode);
        Assert.Equal(before, await CaptureDeliveryStateAsync(scenario, created.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Delivery_ConcurrentRequestsDeliverOnlyOnceWithoutPartialEffects()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var created = await CreatePaidForDeliveryAsync(client, scenario, "delivery-race-create");
        using var other = CreateClient(scenario.Cashier.AccessToken);
        var before = await CaptureDeliveryStateAsync(scenario, created.Id);
        var responses = await Task.WhenAll(
            PostDeliveryAsync(client, created.Id, "delivery-race-one", created.Version),
            PostDeliveryAsync(other, created.Id, "delivery-race-two", created.Version));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var after = await CaptureDeliveryStateAsync(scenario, created.Id);
        Assert.Equal("ENTREGADO", after.State);
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal(before.Common.ActualStock - 1, after.Common.ActualStock);
        Assert.Equal(before.Common.ReservedStock - 1, after.Common.ReservedStock);
        Assert.Equal(before.Common.InventoryMovements + 1, after.Common.InventoryMovements);
        Assert.Equal(before.Common.IdempotencyOperations + 1, after.Common.IdempotencyOperations);
        Assert.Equal(before.Common.Audits + 1, after.Common.Audits);
        AssertFinancialStateUnchanged(before, after);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Delivery_RacingFinalPayment_SerializesWithPaymentAndDeliversOnlyAfterBalanceReachesZero()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        SetBearer(client, scenario.Admin.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "delivery-payment-race-create", Request(scenario, [new("EFECTIVO", 201m, 201m, null)])),
            HttpStatusCode.Created);
        using var deliveryClient = CreateClient(scenario.Admin.AccessToken);
        var before = await CaptureDeliveryStateAsync(scenario, created.Id);
        var paymentTask = PostPaymentAsync(client, created.Id, "delivery-payment-race-payment",
            new ApplyLayawayPaymentRequest(created.Version, [new("EFECTIVO", 800m, 800m, null)]));
        var deliveryTask = PostDeliveryAsync(deliveryClient, created.Id,
            "delivery-payment-race-delivery", created.Version + 1);
        await Task.WhenAll(paymentTask, deliveryTask);
        var payment = await ReadAsync<LayawayPaymentOperationDto>(await paymentTask, HttpStatusCode.OK);
        Assert.Equal("PAGADO", payment.State);
        var deliveryResponse = await deliveryTask;
        Assert.True(deliveryResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        if (deliveryResponse.StatusCode == HttpStatusCode.Conflict)
            deliveryResponse = await PostDeliveryAsync(deliveryClient, created.Id,
                "delivery-payment-race-delivery", payment.Version);
        _ = await ReadAsync<LayawayDeliveryDto>(deliveryResponse, HttpStatusCode.OK);

        var after = await CaptureDeliveryStateAsync(scenario, created.Id);
        Assert.Equal("ENTREGADO", after.State);
        Assert.Equal(before.Version + 2, after.Version);
        Assert.Equal(before.Common.Payments + 1, after.Common.Payments);
        Assert.Equal(before.Common.CashMovements + 1, after.Common.CashMovements);
        Assert.Equal(before.Common.PaymentSequence + 1, after.Common.PaymentSequence);
        Assert.Equal(before.Common.InventoryMovements + 1, after.Common.InventoryMovements);
        Assert.Equal(before.Common.ActualStock - 1, after.Common.ActualStock);
        Assert.Equal(before.Common.ReservedStock - 1, after.Common.ReservedStock);
        Assert.Equal(before.Common.IdempotencyOperations + 2, after.Common.IdempotencyOperations);
        Assert.Equal(before.Common.Audits + 2, after.Common.Audits);
        Assert.Null(after.SaleId);
        Assert.Equal(before.Sales, after.Sales);
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    public async Task Delivery_InsufficientStockOrReservation_ConflictsWithoutWrites(int actual, int reserved)
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var created = await CreatePaidForDeliveryAsync(client, scenario, "delivery-insufficient-create");
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var product = await db.Producto.SingleAsync(x => x.Id == scenario.ProductId);
            product.StockActual = actual;
            product.StockReservado = reserved;
            await db.SaveChangesAsync();
        }
        var before = await CaptureDeliveryStateAsync(scenario, created.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await PostDeliveryAsync(client,
            created.Id, "delivery-insufficient", created.Version)).StatusCode);
        Assert.Equal(before, await CaptureDeliveryStateAsync(scenario, created.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Delivery_ActiveAndAlreadyDeliveredLayaways_ConflictWithoutWrites()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var active = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "delivery-active-create", Request(scenario, [new("EFECTIVO", 201m, 201m, null)])),
            HttpStatusCode.Created);
        var before = await CaptureDeliveryStateAsync(scenario, active.Id);
        using var detailResponse = await client.GetAsync($"/api/layaways/{active.Id}");
        var activeDetail = await ReadAsync<LayawayDetailDto>(detailResponse, HttpStatusCode.OK);
        Assert.Null(activeDetail.Delivery);
        Assert.Equal(HttpStatusCode.Conflict, (await PostDeliveryAsync(client,
            active.Id, "delivery-active", active.Version)).StatusCode);
        Assert.Equal(before, await CaptureDeliveryStateAsync(scenario, active.Id));

        var paid = await CreatePaidForDeliveryAsync(client, scenario, "delivery-closed-create");
        var delivered = await ReadAsync<LayawayDeliveryDto>(await PostDeliveryAsync(client,
            paid.Id, "delivery-first", paid.Version), HttpStatusCode.OK);
        var deliveredBefore = await CaptureDeliveryStateAsync(scenario, paid.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await PostDeliveryAsync(client,
            paid.Id, "delivery-second", delivered.Version)).StatusCode);
        Assert.Equal(deliveredBefore, await CaptureDeliveryStateAsync(scenario, paid.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Delivery_DeferredCommitFailure_RollsBackStockReservationStateVersionIdempotencyAndAudit()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var created = await CreatePaidForDeliveryAsync(client, scenario, "delivery-commit-create");
        await InstallDeferredDeliveryFailureAsync();
        var before = await CaptureDeliveryStateAsync(scenario, created.Id);

        using var response = await PostDeliveryAsync(client, created.Id,
            "delivery-commit-failure", created.Version);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await DeferredDeliveryFailureObservedAsync());
        Assert.Equal(before, await CaptureDeliveryStateAsync(scenario, created.Id));
    }

    private async Task<CreatedLayawayDto> CreatePaidForDeliveryAsync(
        HttpClient client, Scenario scenario, string key)
    {
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        return await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            key, Request(scenario, [new("EFECTIVO", 1001m, 1001m, null)])), HttpStatusCode.Created);
    }

    private async Task CloseDeliveryShiftAsync(HttpClient client, Scenario scenario)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        var reconciliation = await ReadAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        _ = await ReadAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", new CloseCashShiftRequest(
                reconciliation.ShiftVersion,
                reconciliation.PaymentMethods.Select(x =>
                    new CountedPaymentMethodRequest(x.PaymentMethodId, x.ExpectedAmount)).ToArray(),
                null, null, null)), HttpStatusCode.OK);
    }

    private async Task<DeliveryState> CaptureDeliveryStateAsync(Scenario scenario, Guid layawayId)
    {
        var common = await CaptureAsync(scenario);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == layawayId);
        var product = await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.ProductId);
        var payments = JsonSerializer.Serialize(await db.PagoApartado.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ApartadoId, x.Estado, x.ValorAplicado, x.ValorRecibido,
                x.Cambio, x.MovimientoCajaId, x.SesionOperadorId }).ToArrayAsync());
        var cash = JsonSerializer.Serialize(await db.MovimientoCaja.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.TurnoCajaId, x.Tipo, x.CategoriaMovimiento, x.Valor,
                x.UsuarioId, x.SesionOperadorId, x.CorrelacionId }).ToArrayAsync());
        var shifts = JsonSerializer.Serialize(await db.TurnoCaja.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Estado, x.Version, x.FechaHoraCierre,
                x.EfectivoEsperado, x.EfectivoContado, x.Diferencia }).ToArrayAsync());
        var sessions = JsonSerializer.Serialize(await db.SesionOperador.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Estado, x.FechaUltimoUso, x.FechaFin }).ToArrayAsync());
        return new DeliveryState(common, layaway.Estado, layaway.Version,
            layaway.FechaEntrega, layaway.UsuarioEntregaId, layaway.VentaEntregaId,
            await db.Venta.CountAsync(), await db.ReembolsoApartado.CountAsync(),
            product.CostoPromedio, product.Version, payments, cash, shifts, sessions);
    }

    private static void AssertFinancialStateUnchanged(DeliveryState before, DeliveryState after)
    {
        Assert.Equal(before.Common.Payments, after.Common.Payments);
        Assert.Equal(before.Common.CashMovements, after.Common.CashMovements);
        Assert.Equal(before.Common.PaymentSequence, after.Common.PaymentSequence);
        Assert.Equal(before.Common.LayawaySequence, after.Common.LayawaySequence);
        Assert.Equal(before.Common.Authorizations, after.Common.Authorizations);
        Assert.Equal(before.Sales, after.Sales);
        Assert.Equal(before.Refunds, after.Refunds);
        Assert.Equal(before.AverageCost, after.AverageCost);
        Assert.Equal(before.PaymentsSnapshot, after.PaymentsSnapshot);
        Assert.Equal(before.CashSnapshot, after.CashSnapshot);
        Assert.Equal(before.ShiftsSnapshot, after.ShiftsSnapshot);
        Assert.Equal(before.SessionsSnapshot, after.SessionsSnapshot);
    }

    private async Task InstallDeferredDeliveryFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE ventas.test_delivery_commit_observed;
            CREATE FUNCTION ventas.test_fail_delivery_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.estado = 'ENTREGADO'
                 AND NEW.fecha_entrega IS NOT NULL AND NEW.usuario_entrega_id IS NOT NULL
                 AND NEW.venta_entrega_id IS NULL AND NEW.version > OLD.version
                 AND EXISTS (SELECT 1 FROM compras.movimiento_inventario m
                             JOIN catalogo.producto p ON p.id = m.producto_id
                             WHERE m.apartado_id = NEW.id AND m.tipo_movimiento = 'ENTREGA_APARTADO'
                               AND m.cantidad_stock = -1 AND m.cantidad_reservada = -1
                               AND p.stock_actual = m.stock_posterior
                               AND p.stock_reservado = m.reservado_posterior)
                 AND EXISTS (SELECT 1 FROM auditoria.operacion_idempotente
                             WHERE entidad_id = NEW.id AND tipo_operacion = 'ENTREGAR_APARTADO')
                 AND EXISTS (SELECT 1 FROM auditoria.evento_auditoria
                             WHERE entidad_id = NEW.id AND accion = 'LAYAWAYDELIVERED')
              THEN
                PERFORM nextval('ventas.test_delivery_commit_observed');
              END IF;
              RAISE EXCEPTION 'Forced isolated layaway delivery commit failure' USING ERRCODE = '23514';
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_delivery_commit
              AFTER UPDATE ON ventas.apartado DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION ventas.test_fail_delivery_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredDeliveryFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT is_called FROM ventas.test_delivery_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<HttpResponseMessage> PostDeliveryAsync(
        HttpClient client, Guid layawayId, string key, long expectedVersion)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"/api/layaways/{layawayId}/delivery")
        { Content = JsonContent.Create(new { expectedVersion }) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private sealed record DeliveryState(LayawayState Common, string State, long Version,
        DateTime? DeliveredAt, Guid? DeliveredBy, Guid? SaleId, int Sales, int Refunds,
        decimal? AverageCost, long ProductVersion, string PaymentsSnapshot,
        string CashSnapshot, string ShiftsSnapshot, string SessionsSnapshot);
}
