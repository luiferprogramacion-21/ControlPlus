using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class LayawayCreateApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task LaterMixedPayment_UsesOneCashMovement_PreservesReservation_AndReplaysWithoutSession()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-later-create", Request(scenario, [new("EFECTIVO", 201m, 201m, null)])),
            HttpStatusCode.Created);
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var request = new ApplyLayawayPaymentRequest(created.Version,
            [new("EFECTIVO", 300m, 350m, null), new("NEQUI", 500m, null, "LATER-NEQUI")]);
        var before = await CaptureAsync(scenario, session.Id);
        var result = await ReadAsync<LayawayPaymentOperationDto>(await PostPaymentAsync(
            client, created.Id, "layaway-later-mixed", request), HttpStatusCode.OK);
        Assert.Equal("PAGADO", result.State);
        Assert.Equal(0m, result.Balance);
        Assert.Equal(created.Version + 1, result.Version);
        Assert.Equal(50m, result.Change);
        Assert.Equal(new[] { "PAP-LAYAWAY1-2", "PAP-LAYAWAY1-3" },
            result.Payments.Select(x => x.VisibleNumber).ToArray());
        var after = await CaptureAsync(scenario, session.Id);
        Assert.Equal(before.Payments + 2, after.Payments);
        Assert.Equal(before.CashMovements + 1, after.CashMovements);
        Assert.Equal(before.PaymentSequence + 2, after.PaymentSequence);
        Assert.Equal(before.ActualStock, after.ActualStock);
        Assert.Equal(before.ReservedStock, after.ReservedStock);
        Assert.Equal(before.InventoryMovements, after.InventoryMovements);
        Assert.Equal("CERRADA", after.SessionState);
        var replay = await ReadAsync<LayawayPaymentOperationDto>(await PostPaymentAsync(
            client, created.Id, "layaway-later-mixed", request), HttpStatusCode.OK);
        Assert.Equal(result.CorrelationId, replay.CorrelationId);
        Assert.Equal(result.Version, replay.Version);
        Assert.Equal(result.Balance, replay.Balance);
        Assert.Equal(result.Payments.Select(x => x.VisibleNumber),
            replay.Payments.Select(x => x.VisibleNumber));
        Assert.Equal(after, await CaptureAsync(scenario, session.Id));
        using (var anonymous = CreateClient())
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostPaymentAsync(anonymous,
                created.Id, "layaway-later-mixed", request)).StatusCode);
        Assert.Equal(after, await CaptureAsync(scenario, session.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await PostPaymentAsync(client,
            created.Id, "layaway-later-mixed", request with { ExpectedVersion = result.Version })).StatusCode);
        Assert.Equal(after, await CaptureAsync(scenario, session.Id));

        var detail = await ReadAsync<LayawayDetailDto>(await client.GetAsync(
            $"/api/layaways/{created.Id}"), HttpStatusCode.OK);
        Assert.Equal(3, detail.Payments.Count);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var rows = await db.PagoApartado.AsNoTracking().Where(x => x.ApartadoId == created.Id)
                .OrderBy(x => x.Consecutivo).ToArrayAsync();
            Assert.Single(rows.Skip(1).Select(x => x.MovimientoCajaId).Distinct());
            var cashFlow = scope.ServiceProvider.GetRequiredService<ControlPlus.Application.Reports.IDailyCashFlowRepository>();
            var operatingDate = await db.TurnoCaja.Where(x => x.Id == rows[1].TurnoCajaId)
                .Select(x => x.FechaOperativa).SingleAsync();
            var entries = await cashFlow.GetEntriesAsync(rows[1].InstalacionId, operatingDate, operatingDate, default);
            Assert.Equal(1_001m, entries.Where(x => x.Origin == "PAGO_APARTADO").Sum(x => x.Amount));
            Assert.Equal(result.CorrelationId, await db.MovimientoCaja.AsNoTracking()
                .Where(x => x.Id == rows[1].MovimientoCajaId)
                .Select(x => x.CorrelacionId).SingleAsync());
            Assert.NotNull(await db.Apartado.Where(x => x.Id == created.Id)
                .Select(x => x.FechaPago).SingleAsync());
            Assert.Equal(1, await db.EventoAuditoria.CountAsync(x =>
                x.EntidadId == created.Id && x.Accion == "LAYAWAYPAYMENTAPPLIED"));
        }
        SetBearer(client, scenario.Admin.AccessToken);
        var reconciliation = await ReadAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(1001m, reconciliation.TotalExpected);
        Assert.Equal(501m, reconciliation.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        Assert.Equal(500m, reconciliation.PaymentMethods.Single(x => x.Code == "NEQUI").ExpectedAmount);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConcurrentPayments_OnlyOneWinsAndNoPartialEffectsRemain()
    {
        using var setup = CreateClient();
        var scenario = await BootstrapAsync(setup, sharedShift: false);
        using var first = CreateClient(scenario.Admin.AccessToken);
        using var second = CreateClient(scenario.Admin.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(first,
            "layaway-payment-race-create", Request(scenario, [new("EFECTIVO", 201m, 201m, null)])),
            HttpStatusCode.Created);
        var before = await CaptureAsync(scenario);
        var request = new ApplyLayawayPaymentRequest(created.Version,
            [new("EFECTIVO", 100m, 100m, null)]);
        var responses = await Task.WhenAll(
            PostPaymentAsync(first, created.Id, "layaway-payment-race-one", request),
            PostPaymentAsync(second, created.Id, "layaway-payment-race-two", request));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var after = await CaptureAsync(scenario);
        Assert.Equal(before.Payments + 1, after.Payments);
        Assert.Equal(before.CashMovements + 1, after.CashMovements);
        Assert.Equal(before.PaymentSequence + 1, after.PaymentSequence);
        Assert.Equal(before.ReservedStock, after.ReservedStock);
        Assert.Equal(before.InventoryMovements, after.InventoryMovements);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(700m, await db.Apartado.Where(x => x.Id == created.Id)
            .Select(x => x.SaldoPendiente).SingleAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ExpiredActiveWithInactiveCustomer_AcceptsPayment_ButClosedAndStaleRejectWithoutEffects()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-later-expired-create", Request(scenario, [new("EFECTIVO", 201m, 201m, null)])),
            HttpStatusCode.Created);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var layaway = await db.Apartado.SingleAsync(x => x.Id == created.Id);
            layaway.FechaLimite = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var customer = await db.Cliente.SingleAsync(x => x.Id == scenario.CustomerId);
            customer.Activo = false;
            await db.SaveChangesAsync();
        }
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var currentVersion = created.Version + 1;
        var payment = new ApplyLayawayPaymentRequest(currentVersion,
            [new("EFECTIVO", 100m, 100m, null)]);
        var before = await CaptureAsync(scenario, session.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await PostPaymentAsync(client,
            created.Id, "layaway-later-stale", payment with { ExpectedVersion = created.Version })).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario, session.Id));
        var applied = await ReadAsync<LayawayPaymentOperationDto>(await PostPaymentAsync(client,
            created.Id, "layaway-later-expired", payment), HttpStatusCode.OK);
        Assert.Equal("ACTIVO", applied.State);
        Assert.Equal(700m, applied.Balance);
        var after = await CaptureAsync(scenario, session.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await PostPaymentAsync(client,
            created.Id, "layaway-later-no-session", payment with { ExpectedVersion = applied.Version })).StatusCode);
        Assert.Equal(after, await CaptureAsync(scenario, session.Id));
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var final = await ReadAsync<LayawayPaymentOperationDto>(await PostPaymentAsync(client,
            created.Id, "layaway-later-final", new ApplyLayawayPaymentRequest(applied.Version,
                [new("EFECTIVO", 700m, 700m, null)])), HttpStatusCode.OK);
        Assert.Equal("PAGADO", final.State);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var closedBefore = await CaptureAsync(scenario);
        Assert.Equal(HttpStatusCode.Conflict, (await PostPaymentAsync(client,
            created.Id, "layaway-later-closed", payment with { ExpectedVersion = final.Version })).StatusCode);
        Assert.Equal(closedBefore, await CaptureAsync(scenario));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task LaterPaymentReplay_RevokedManagePermission_IsRejectedWithoutWrites()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-replay-permission-create", Request(scenario, [new("EFECTIVO", 201m, 201m, null)])),
            HttpStatusCode.Created);
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var request = new ApplyLayawayPaymentRequest(created.Version,
            [new("EFECTIVO", 100m, 100m, null)]);
        var applied = await ReadAsync<LayawayPaymentOperationDto>(await PostPaymentAsync(client,
            created.Id, "layaway-replay-permission", request), HttpStatusCode.OK);
        Assert.Equal(700m, applied.Balance);

        SetBearer(client, scenario.Admin.AccessToken);
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.LayawayManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Cashier.User.Id}/permissions/{permissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        var cashierWithoutPermission = await LoginAsync(client, CashierName, CashierPassword);
        SetBearer(client, cashierWithoutPermission.AccessToken);
        var beforeReplay = await CapturePaymentStateAsync(scenario, created.Id, session.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostPaymentAsync(client,
            created.Id, "layaway-replay-permission", request)).StatusCode);
        Assert.Equal(beforeReplay, await CapturePaymentStateAsync(scenario, created.Id, session.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task LaterPayment_InvalidMoneyAndLimits_ReturnValidationWithoutPartialEffects()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-invalid-later-create", Request(scenario, [new("EFECTIVO", 201m, 201m, null)])),
            HttpStatusCode.Created);
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var before = await CapturePaymentStateAsync(scenario, created.Id, session.Id);
        CreateLayawayPaymentRequest[][] invalidPayments =
        [
            [new("EFECTIVO", 100.5m, 101m, null)],
            [new("EFECTIVO", 100m, 100.5m, null)],
            [new("EFECTIVO", 100m, CashMoney.Maximum + 1m, null)],
            [new("EFECTIVO", CashMoney.Maximum + 1m, CashMoney.Maximum + 1m, null)],
            [new("EFECTIVO", 500000000000000000m, 500000000000000000m, null),
             new("NEQUI", 500000000000000000m, null, "LATER-OVERFLOW")],
            [new("EFECTIVO", 801m, 801m, null)]
        ];
        for (var index = 0; index < invalidPayments.Length; index++)
        {
            using var response = await PostPaymentAsync(client, created.Id,
                $"layaway-invalid-later-{index}",
                new ApplyLayawayPaymentRequest(created.Version, invalidPayments[index]));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("validation.failed", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(before, await CapturePaymentStateAsync(scenario, created.Id, session.Id));
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task LaterPayment_DeferredCommitFailure_RollsBackBalanceVersionPaymentsNumberingCashSessionIdempotencyAndAudit()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-later-commit-create", Request(scenario, [new("EFECTIVO", 201m, 201m, null)])),
            HttpStatusCode.Created);
        var session = await StartSessionAsync(client, scenario);
        await InstallDeferredLaterPaymentFailureAsync();
        var before = await CapturePaymentStateAsync(scenario, created.Id, session.Id);
        SetBearer(client, scenario.Cashier.AccessToken);

        using var response = await PostPaymentAsync(client, created.Id,
            "layaway-later-commit-failure", new ApplyLayawayPaymentRequest(created.Version,
                [new("EFECTIVO", 100m, 100m, null), new("NEQUI", 100m, null, "LATER-COMMIT") ]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await DeferredLaterPaymentFailureObservedAsync());
        Assert.Equal(before, await CapturePaymentStateAsync(scenario, created.Id, session.Id));
    }

    private async Task<LaterPaymentState> CapturePaymentStateAsync(
        Scenario scenario, Guid layawayId, Guid sessionId)
    {
        var common = await CaptureAsync(scenario, sessionId);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == layawayId);
        return new LaterPaymentState(common, layaway.SaldoPendiente, layaway.Version,
            layaway.Estado, layaway.FechaPago);
    }

    private async Task InstallDeferredLaterPaymentFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE ventas.test_later_payment_commit_observed;
            CREATE FUNCTION ventas.test_fail_later_payment_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF EXISTS (SELECT 1 FROM ventas.apartado
                         WHERE id = NEW.apartado_id AND saldo_pendiente = 600 AND version > 1)
                 AND EXISTS (SELECT 1 FROM caja.movimiento_caja
                             WHERE id = NEW.movimiento_caja_id AND valor = 200)
                 AND EXISTS (SELECT 1 FROM auditoria.operacion_idempotente
                             WHERE entidad_id = NEW.apartado_id AND tipo_operacion = 'ABONAR_APARTADO')
                 AND EXISTS (SELECT 1 FROM auditoria.evento_auditoria
                             WHERE entidad_id = NEW.apartado_id AND accion = 'LAYAWAYPAYMENTAPPLIED')
                 AND EXISTS (SELECT 1 FROM caja.sesion_operador
                             WHERE id = NEW.sesion_operador_id AND estado = 'CERRADA')
              THEN
                PERFORM nextval('ventas.test_later_payment_commit_observed');
              END IF;
              RAISE EXCEPTION 'Forced isolated later payment commit failure' USING ERRCODE = '23514';
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_later_payment_commit
              AFTER INSERT ON ventas.pago_apartado DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION ventas.test_fail_later_payment_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredLaterPaymentFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT is_called FROM ventas.test_later_payment_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private sealed record LaterPaymentState(LayawayState Common, decimal Balance,
        long Version, string State, DateTime? PaidAt);

    private static async Task<HttpResponseMessage> PostPaymentAsync(HttpClient client,
        Guid layawayId, string key, ApplyLayawayPaymentRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"/api/layaways/{layawayId}/payments") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }
}
