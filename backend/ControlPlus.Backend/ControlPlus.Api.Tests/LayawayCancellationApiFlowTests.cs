using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
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
    public async Task AdministratorCancelsLayawayWithoutSecondAuthorizer_AndReplays()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        var created = await CreateForCancellationAsync(client, scenario, "admin-cancel-self-create");
        var request = CancellationRequest(created.Version, 0m, 0m, 0m) with { Authorization = null };
        var cancelled = await ReadAsync<LayawayCancellationDto>(await PostCancellationAsync(client,
            created.Id, "admin-cancel-self", request), HttpStatusCode.OK);
        var replay = await ReadAsync<LayawayCancellationDto>(await PostCancellationAsync(client,
            created.Id, "admin-cancel-self", request), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(cancelled), JsonSerializer.Serialize(replay));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        var authorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == layaway.AutorizacionAnulacionId);
        Assert.Equal(authorization.UsuarioSolicitanteId, authorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", authorization.MetodoAutenticacion);
        Assert.Null(authorization.CredencialAutorizadorId);
        Assert.Contains("sin segundo autorizador", authorization.DescripcionOperacion);
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.EntidadId == created.Id &&
            x.Accion == "LAYAWAYCANCELLED"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SupervisorCanCancelAndDecideRefund_WithAnotherAdministratorAuthorizing()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Admin);
        var created = await CreateForCancellationAsync(client, scenario, "supervisor-cancel-create");
        _ = await StartSessionAsync(client, scenario, scenario.Supervisor);
        SetBearer(client, scenario.Supervisor.AccessToken);
        var request = CancellationRequest(created.Version, 100m, 100m, 0m) with
        {
            Authorization = new(AdminName, AdminPassword)
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCancellationAsync(client, created.Id,
            "supervisor-cancel-no-auth", request with { Authorization = null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCancellationAsync(client, created.Id,
            "supervisor-cancel-self-auth", request with
            { Authorization = new(SupervisorName, SupervisorPassword) })).StatusCode);
        var cancelled = await ReadAsync<LayawayCancellationDto>(await PostCancellationAsync(client,
            created.Id, "supervisor-cancel-with-admin", request), HttpStatusCode.OK);
        Assert.Equal(100m, cancelled.Refunded);
        Assert.Equal(scenario.Supervisor.User.Id, cancelled.CancelledByUserId);
        Assert.Equal(scenario.Admin.User.Id, cancelled.AuthorizedByUserId);
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(0, 0, 0)]
    [InlineData(300, 300, 0)]
    [InlineData(1001, 1001, 0)]
    [InlineData(700, 300, 400)]
    public async Task Cancellation_ZeroPartialTotalAndMixedRefund_ReleaseReservationAndReconcileOnce(
        int refund, int cash, int nequi)
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        var created = await CreateForCancellationAsync(client, scenario,
            "cancellation-create-" + refund + "-" + nequi);
        if (refund == 0) await CloseDeliveryShiftAsync(client, scenario);
        var before = await CaptureAsync(scenario);
        var request = CancellationRequest(created.Version, refund, cash, nequi);
        var cancelled = await ReadAsync<LayawayCancellationDto>(await PostCancellationAsync(
            client, created.Id, "cancellation-" + refund + "-" + nequi, request), HttpStatusCode.OK);
        var after = await CaptureAsync(scenario);
        Assert.Equal("ANULADO", cancelled.State);
        Assert.Equal(1001m, cancelled.TotalPaid);
        Assert.Equal(refund, cancelled.Refunded);
        Assert.Equal(1001 - refund, cancelled.Retained);
        Assert.Equal(created.Version + 1, cancelled.Version);
        Assert.Equal(before.ActualStock, after.ActualStock);
        Assert.Equal(before.ReservedStock - 1, after.ReservedStock);
        Assert.Equal(before.InventoryMovements + 1, after.InventoryMovements);
        Assert.Equal(before.CashMovements + (refund > 0 ? 1 : 0), after.CashMovements);
        Assert.Equal(before.IdempotencyOperations + 1, after.IdempotencyOperations);
        Assert.Equal(before.Authorizations + 1, after.Authorizations);
        Assert.Equal(before.Audits + 1, after.Audits);
        Assert.Equal((cash > 0 ? 1 : 0) + (nequi > 0 ? 1 : 0), cancelled.Refunds.Count);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal("ANULADO", layaway.Estado);
        Assert.Equal(1001m, layaway.TotalAbonadoCancelacion);
        Assert.Equal(refund, layaway.ValorDevueltoCancelacion);
        Assert.Equal(1001 - refund, layaway.ValorRetenidoCancelacion);
        var movement = await db.MovimientoInventario.AsNoTracking().SingleAsync(x =>
            x.ApartadoId == created.Id && x.TipoMovimiento == "LIBERACION_APARTADO");
        Assert.Equal(0, movement.CantidadStock);
        Assert.Equal(-1, movement.CantidadReservada);
        Assert.Equal(movement.StockAnterior, movement.StockPosterior);
        Assert.Equal(cancelled.CorrelationId, movement.CorrelacionId);
        var rows = await db.ReembolsoApartado.AsNoTracking().Where(x => x.ApartadoId == created.Id)
            .ToArrayAsync();
        Assert.Equal(cancelled.Refunds.Count, rows.Length);
        Assert.Equal(refund, rows.Sum(x => x.Valor));
        var cashFlow = scope.ServiceProvider.GetRequiredService<ControlPlus.Application.Reports.IDailyCashFlowRepository>();
        var refundShift = refund > 0 ? rows[0].TurnoCajaId : (Guid?)null;
        if (refundShift.HasValue)
        {
            var operatingDate = await db.TurnoCaja.Where(x => x.Id == refundShift.Value)
                .Select(x => x.FechaOperativa).SingleAsync();
            var installationId = await db.Caja.Select(x => x.InstalacionId).SingleAsync();
            var entries = await cashFlow.GetEntriesAsync(installationId, operatingDate, operatingDate, default);
            Assert.Equal(-refund, entries.Where(x => x.Origin == "REEMBOLSO_APARTADO").Sum(x => x.Amount));
        }
        if (refund > 0)
        {
            Assert.Single(rows.Select(x => x.MovimientoCajaId).Distinct());
            var reconciliation = await ReadAsync<CashReconciliationDto>(
                await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
            Assert.Equal(1001m - cash, reconciliation.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
            if (nequi > 0)
                Assert.Equal(-nequi, reconciliation.PaymentMethods.Single(x => x.Code == "NEQUI").ExpectedAmount);
        }
        else Assert.Empty(rows);
        var detail = await ReadAsync<LayawayDetailDto>(await client.GetAsync(
            $"/api/layaways/{created.Id}"), HttpStatusCode.OK);
        Assert.NotNull(detail.Cancellation);
        Assert.Equal(cancelled.CorrelationId, detail.Cancellation.CorrelationId);
        Assert.Equal(cancelled.Refunds.Count, detail.Cancellation.Refunds.Count);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Cancellation_ReplayAndStaleVersion_DoNotWriteAgain()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        var created = await CreateForCancellationAsync(client, scenario, "cancel-replay-create");
        var request = CancellationRequest(created.Version, 100, 100, 0);
        var first = await ReadAsync<LayawayCancellationDto>(await PostCancellationAsync(client,
            created.Id, "cancel-replay", request), HttpStatusCode.OK);
        var after = await CaptureAsync(scenario);
        var replay = await ReadAsync<LayawayCancellationDto>(await PostCancellationAsync(client,
            created.Id, "cancel-replay", request with
            { Authorization = new(SupervisorName, "ignored-on-replay") }), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        Assert.Equal(after, await CaptureAsync(scenario));
        Assert.Equal(HttpStatusCode.Conflict, (await PostCancellationAsync(client, created.Id,
            "cancel-replay", CancellationRequest(created.Version, 0, 0, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostCancellationAsync(client, created.Id,
            "cancel-stale", request)).StatusCode);
        Assert.Equal(after, await CaptureAsync(scenario));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Cancellation_RejectsCashierAndSameAuthorizerWithoutEffects()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        var created = await CreateForCancellationAsync(client, scenario, "cancel-permission-create");
        var before = await CaptureAsync(scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCancellationAsync(client, created.Id,
            "cancel-cashier", CancellationRequest(created.Version, 0, 0, 0))).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCancellationAsync(client, created.Id,
            "cancel-self", CancellationRequest(created.Version, 0, 0, 0) with
            { Authorization = new(AdminName, AdminPassword) })).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Cancellation_RevokedEffectivePermissionsRejectActorAndAuthorizer()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        var created = await CreateForCancellationAsync(client, scenario, "cancel-revoke-create");
        var request = CancellationRequest(created.Version, 0, 0, 0);
        var decideId = await GetPermissionIdAsync(client, PermissionCodes.LayawayRefundDecide);
        var cancelId = await GetPermissionIdAsync(client, PermissionCodes.LayawayCancel);
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Admin.User.Id}/permissions/{decideId}",
            new SetUserPermissionRequest(false))).StatusCode);
        var admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin.AccessToken);
        var before = await CaptureAsync(scenario);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCancellationAsync(client, created.Id,
            "cancel-actor-revoked", request)).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Admin.User.Id}/permissions/{decideId}",
            new SetUserPermissionRequest(true))).StatusCode);
        admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Supervisor.User.Id}/permissions/{cancelId}",
            new SetUserPermissionRequest(false))).StatusCode);
        admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin.AccessToken);
        before = await CaptureAsync(scenario);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostCancellationAsync(client, created.Id,
            "cancel-authorizer-revoked", request)).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Cancellation_RejectsInvalidReferencesSumsAndMoneyLimits()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        var created = await CreateForCancellationAsync(client, scenario, "cancel-validation-create");
        var before = await CaptureAsync(scenario);
        var valid = CancellationRequest(created.Version, 100, 100, 0);
        var invalid = new[]
        {
            valid with { Refunds = [new("NEQUI", 100m, null)] },
            valid with { Refunds = [new("EFECTIVO", 99m, null)] },
            valid with { Refunds = [new("EFECTIVO", 50m, null), new("EFECTIVO", 50m, null)] },
            valid with { RefundAmount = 1002m, Refunds = [new("EFECTIVO", 1002m, null)] },
            valid with { RefundAmount = 100.5m },
            valid with { RefundAmount = decimal.MaxValue }
        };
        for (var i = 0; i < invalid.Length; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await PostCancellationAsync(client,
                created.Id, "cancel-invalid-" + i, invalid[i])).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Cancellation_CompetesWithDelivery_OnlyOneStateTransitionCommits()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        var created = await CreateForCancellationAsync(client, scenario, "cancel-race-create");
        var cancel = PostCancellationAsync(client, created.Id, "cancel-race",
            CancellationRequest(created.Version, 0, 0, 0));
        var delivery = PostDeliveryAsync(client, created.Id, "delivery-race", created.Version);
        var responses = await Task.WhenAll(cancel, delivery);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Contains(layaway.Estado, new[] { "ANULADO", "ENTREGADO" });
        Assert.Equal(1, await db.MovimientoInventario.CountAsync(x => x.ApartadoId == created.Id &&
            (x.TipoMovimiento == "LIBERACION_APARTADO" || x.TipoMovimiento == "ENTREGA_APARTADO")));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Cancellation_CompetesWithPayment_OnlyOneVersionCommits()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        SetBearer(client, scenario.Admin.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "cancel-payment-race-create", Request(scenario,
                [new("EFECTIVO", 201m, 201m, null)]) with { Authorization = null }), HttpStatusCode.Created);
        var cancel = PostCancellationAsync(client, created.Id, "cancel-payment-race",
            CancellationRequest(created.Version, 0, 0, 0));
        var payment = PostPaymentAsync(client, created.Id, "payment-cancel-race",
            new ApplyLayawayPaymentRequest(created.Version,
                [new("EFECTIVO", 100m, 100m, null)]));
        var responses = await Task.WhenAll(cancel, payment);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(created.Version + 1, layaway.Version);
        Assert.Equal(layaway.Estado == "ANULADO" ? 1 : 0,
            await db.MovimientoInventario.CountAsync(x => x.ApartadoId == created.Id &&
                x.TipoMovimiento == "LIBERACION_APARTADO"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Cancellation_RejectsCashRefundWhenExpectedCashIsInsufficient()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        SetBearer(client, scenario.Admin.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "cancel-no-cash-create", Request(scenario,
                [new("NEQUI", 1001m, null, "INITIAL-NEQUI")]) with { Authorization = null }),
            HttpStatusCode.Created);
        var before = await CaptureAsync(scenario);
        Assert.Equal(HttpStatusCode.Conflict, (await PostCancellationAsync(client, created.Id,
            "cancel-no-cash", CancellationRequest(created.Version, 1001, 1001, 0))).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Cancellation_DeferredCommitFailure_RollsBackAllEffects()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var created = await CreatePaidForDeliveryAsync(client, scenario, "cancel-rollback-create");
        SetBearer(client, scenario.Admin.AccessToken);
        var credential = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{scenario.Admin.User.Id}",
            new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        var session = await ReadAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(credential.CredentialToken)),
            HttpStatusCode.Created);
        var reconciliationBefore = await ReadAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(1001m, reconciliationBefore.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        var before = await CaptureCancellationRollbackStateAsync(scenario, created.Id, session.Id);
        Assert.Equal("ACTIVA", before.Common.SessionState);
        await InstallDeferredCancellationFailureAsync();
        Assert.False(await DeferredCancellationFailureObservedAsync());

        using var response = await PostCancellationAsync(client, created.Id,
            "cancel-rollback", CancellationRequest(created.Version, 300, 100, 200));

        var responseBody = await response.Content.ReadAsStringAsync();
        // A validation rejection cannot set this nontransactional witness. The deferred
        // trigger sees every financial write and the consumed session before failing.
        Assert.True(await DeferredCancellationFailureObservedAsync(),
            $"No se observó el fallo diferido con todos sus efectos. HTTP {(int)response.StatusCode}: {responseBody}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
        using var problem = JsonDocument.Parse(responseBody);
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("resource.conflict", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("https://controlplus.local/problems/resource-conflict",
            problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("La operación entra en conflicto con el estado actual del recurso.",
            problem.RootElement.GetProperty("title").GetString());
        Assert.Equal($"/api/layaways/{created.Id}/cancellation",
            problem.RootElement.GetProperty("instance").GetString());
        foreach (var detail in new[] { "23514", "Forced isolated", "test_fail_cancellation_commit",
                     AdminPassword, SupervisorPassword })
            Assert.DoesNotContain(detail, responseBody, StringComparison.Ordinal);

        Assert.Equal(before, await CaptureCancellationRollbackStateAsync(scenario, created.Id, session.Id));
        var reconciliationAfter = await ReadAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(reconciliationBefore), JsonSerializer.Serialize(reconciliationAfter));
        // Preserve the original rollback assertions, including the common counters.
        Assert.Equal(before.Common, await CaptureAsync(scenario, session.Id));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal("PAGADO", layaway.Estado);
        Assert.Equal(created.Version, layaway.Version);
        Assert.Equal(0, await db.ReembolsoApartado.CountAsync(x => x.ApartadoId == created.Id));
        Assert.Equal(0, await db.MovimientoInventario.CountAsync(x => x.ApartadoId == created.Id &&
            x.TipoMovimiento == "LIBERACION_APARTADO"));
    }

    private async Task<CancellationRollbackState> CaptureCancellationRollbackStateAsync(
        Scenario scenario, Guid layawayId, Guid sessionId)
    {
        var common = await CaptureAsync(scenario, sessionId);
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        // Compare persisted rows as well as counts; no user password/hash is selected.
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'layaway', (SELECT to_jsonb(a) FROM ventas.apartado a WHERE id = @layaway),
              'product', (SELECT to_jsonb(p) FROM catalogo.producto p WHERE id = @product),
              'shift', (SELECT to_jsonb(t) FROM caja.turno_caja t WHERE id = @shift),
              'session', (SELECT to_jsonb(s) FROM caja.sesion_operador s WHERE id = @session),
              'refunds', (SELECT jsonb_agg(to_jsonb(r) ORDER BY r.id) FROM ventas.reembolso_apartado r),
              'payments', (SELECT jsonb_agg(to_jsonb(p) ORDER BY p.id) FROM ventas.pago_apartado p),
              'inventory', (SELECT jsonb_agg(to_jsonb(m) ORDER BY m.id) FROM compras.movimiento_inventario m),
              'cash', (SELECT jsonb_agg(to_jsonb(m) ORDER BY m.id) FROM caja.movimiento_caja m),
              'authorizations', (SELECT jsonb_agg(to_jsonb(a) ORDER BY a.id) FROM seguridad.autorizacion_operacion a),
              'idempotency', (SELECT jsonb_agg(to_jsonb(i) ORDER BY i.id) FROM auditoria.operacion_idempotente i),
              'audit', (SELECT jsonb_agg(to_jsonb(a) ORDER BY a.id) FROM auditoria.evento_auditoria a)
            )::text
            """, connection);
        command.Parameters.AddWithValue("layaway", layawayId);
        command.Parameters.AddWithValue("product", scenario.ProductId);
        command.Parameters.AddWithValue("shift", scenario.Shift.Id);
        command.Parameters.AddWithValue("session", sessionId);
        return new CancellationRollbackState(common, (string)(await command.ExecuteScalarAsync())!);
    }

    private sealed record CancellationRollbackState(LayawayState Common, string PersistedRows);

    private async Task InstallDeferredCancellationFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE ventas.test_cancellation_commit_observed;
            CREATE FUNCTION ventas.test_fail_cancellation_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.estado = 'ANULADO' THEN
                IF NEW.version = OLD.version + 1
                   AND NEW.fecha_anulacion IS NOT NULL
                   AND NEW.usuario_anulacion_id IS NOT NULL
                   AND NEW.total_abonado_cancelacion = 1001
                   AND NEW.valor_devuelto_cancelacion = 300
                   AND NEW.valor_retenido_cancelacion = 701
                   AND EXISTS (SELECT 1 FROM compras.movimiento_inventario m
                               JOIN catalogo.producto p ON p.id = m.producto_id
                               WHERE m.apartado_id = NEW.id AND m.tipo_movimiento = 'LIBERACION_APARTADO'
                                 AND m.cantidad_stock = 0 AND m.cantidad_reservada = -1
                                 AND m.stock_anterior = m.stock_posterior
                                 AND p.stock_actual = m.stock_posterior
                                 AND p.stock_reservado = m.reservado_posterior)
                   AND EXISTS (SELECT 1 FROM ventas.reembolso_apartado r
                               JOIN caja.movimiento_caja m ON m.id = r.movimiento_caja_id
                               JOIN caja.sesion_operador s ON s.id = r.sesion_operador_id
                               WHERE r.apartado_id = NEW.id
                                 AND m.tipo = 'EGRESO' AND m.categoria_movimiento = 'REEMBOLSO_APARTADO'
                                 AND m.valor = 300 AND m.usuario_id = NEW.usuario_anulacion_id
                                 AND s.estado = 'CERRADA' AND s.fecha_fin IS NOT NULL
                                 AND s.usuario_id = NEW.usuario_anulacion_id
                                 AND s.id = m.sesion_operador_id
                                 AND r.correlacion_id = m.correlacion_id
                                 AND (SELECT count(*) = 2 AND sum(valor) = 300
                                        AND count(DISTINCT movimiento_caja_id) = 1
                                      FROM ventas.reembolso_apartado WHERE apartado_id = NEW.id)
                                 AND EXISTS (SELECT 1 FROM ventas.reembolso_apartado rr
                                             JOIN catalogo.metodo_pago mp ON mp.id = rr.metodo_pago_id
                                             WHERE rr.apartado_id = NEW.id AND rr.movimiento_caja_id = m.id
                                               AND mp.codigo = 'EFECTIVO' AND rr.valor = 100)
                                 AND EXISTS (SELECT 1 FROM ventas.reembolso_apartado rr
                                             JOIN catalogo.metodo_pago mp ON mp.id = rr.metodo_pago_id
                                             WHERE rr.apartado_id = NEW.id AND rr.movimiento_caja_id = m.id
                                               AND mp.codigo = 'NEQUI' AND rr.valor = 200
                                               AND rr.referencia = 'REF-NEQUI')
                                 AND EXISTS (SELECT 1 FROM seguridad.autorizacion_operacion a
                                             WHERE a.id = NEW.autorizacion_anulacion_id AND a.estado = 'UTILIZADA'
                                               AND a.resultado = 'EXITOSA' AND a.fecha_utilizacion IS NOT NULL
                                               AND a.usuario_solicitante_id = NEW.usuario_anulacion_id
                                               AND a.usuario_autorizador_id <> NEW.usuario_anulacion_id
                                               AND a.correlacion_id = m.correlacion_id)
                                 AND EXISTS (SELECT 1 FROM auditoria.operacion_idempotente i
                                             WHERE i.entidad_id = NEW.id AND i.tipo_operacion = 'ANULAR_APARTADO'
                                               AND i.sesion_operador_id = s.id AND i.resultado IS NOT NULL
                                               AND i.correlacion_id = m.correlacion_id)
                                 AND EXISTS (SELECT 1 FROM auditoria.evento_auditoria a
                                             WHERE a.entidad_id = NEW.id AND a.accion = 'LAYAWAYCANCELLED'
                                               AND a.correlacion_id = m.correlacion_id)
                                 AND EXISTS (SELECT 1 FROM auditoria.evento_auditoria a
                                             WHERE a.entidad_id = s.id AND a.accion = 'OPERATORSESSIONCLOSED'
                                               AND a.correlacion_id = m.correlacion_id))
                THEN
                  -- nextval is intentionally not rolled back, unlike every business row.
                  PERFORM nextval('ventas.test_cancellation_commit_observed');
                END IF;
                RAISE EXCEPTION 'Forced isolated layaway cancellation commit failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_cancellation_commit
              AFTER UPDATE ON ventas.apartado DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION ventas.test_fail_cancellation_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredCancellationFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT is_called FROM ventas.test_cancellation_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<CreatedLayawayDto> CreateForCancellationAsync(HttpClient client,
        Scenario scenario, string key)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        return await ReadAsync<CreatedLayawayDto>(await PostAsync(client, key,
            Request(scenario, [new("EFECTIVO", 1001m, 1001m, null)]) with { Authorization = null }),
            HttpStatusCode.Created);
    }

    private static CancelLayawayRequest CancellationRequest(long version, decimal amount,
        decimal cash, decimal nequi) => new(version, amount,
        (cash > 0 ? new[] { new LayawayRefundRequest("EFECTIVO", cash, null) } : [])
            .Concat(nequi > 0 ? new[] { new LayawayRefundRequest("NEQUI", nequi, "REF-NEQUI") } : [])
            .ToArray(), null, null, new(SupervisorName, SupervisorPassword));

    private static async Task<HttpResponseMessage> PostCancellationAsync(HttpClient client,
        Guid layawayId, string key, CancelLayawayRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"/api/layaways/{layawayId}/cancellation") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }
}
