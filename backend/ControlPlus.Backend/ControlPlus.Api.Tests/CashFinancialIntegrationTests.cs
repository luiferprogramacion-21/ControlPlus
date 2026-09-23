using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    [Theory, Trait("Category", "Integration")]
    [InlineData("EFECTIVO")]
    [InlineData("TRANSFERENCIA")]
    [InlineData("NEQUI")]
    [InlineData("TARJETA_TEST")]
    public async Task LinkedPayment_IsCountedOnceInItsOwnMethod(string methodCode)
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        await AddFourthPaymentMethodAsync();
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var movement = await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment(methodCode, 300m, methodCode == "EFECTIVO" ? 500m : null)]);

        var reconciliation = await ReconcileAsync(client);
        Assert.Equal(400m, reconciliation.TotalExpected);
        foreach (var method in reconciliation.PaymentMethods)
            Assert.Equal((method.Code == "EFECTIVO" ? 100m : 0m) +
                (method.Code == methodCode ? 300m : 0m), method.ExpectedAmount);

        var closed = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(reconciliation)), HttpStatusCode.OK);
        Assert.Equal(0m, closed.TotalDifference);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var payment = await context.PagoVenta.AsNoTracking().SingleAsync();
        Assert.Equal(movement, payment.MovimientoCajaId);
        Assert.Equal(300m, payment.ValorAplicado);
        Assert.Equal(methodCode == "EFECTIVO" ? 200m : 0m, payment.Cambio);
        var shift = await context.TurnoCaja.AsNoTracking().SingleAsync();
        Assert.Equal(methodCode == "EFECTIVO" ? 400m : 100m, shift.EfectivoEsperado);
        Assert.Equal(4, await context.DetalleArqueoMedioPago.CountAsync());
        Assert.Equal(400m, await context.DetalleArqueoMedioPago.SumAsync(x => x.ValorEsperado));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CombinedPayment_SharingOneMovement_PreservesManualFlowsAndChange()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        await AddFourthPaymentMethodAsync();
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var linkedMovement = await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
        [
            new FinancialPayment("EFECTIVO", 100m, 500m),
            new FinancialPayment("TRANSFERENCIA", 200m),
            new FinancialPayment("NEQUI", 300m),
            new FinancialPayment("TARJETA_TEST", 400m)
        ]);
        foreach (var (route, amount) in new[] { ("incomes", 40m), ("expenses", 10m), ("cash-drops", 20m) })
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
                $"/api/cash/movements/{route}", new CreateCashMovementRequest(amount,
                    "Flujo manual aislado", null))).StatusCode);

        var reconciliation = await ReconcileAsync(client);
        var expected = new Dictionary<string, decimal>
        {
            ["EFECTIVO"] = 210m, ["TRANSFERENCIA"] = 200m,
            ["NEQUI"] = 300m, ["TARJETA_TEST"] = 400m
        };
        Assert.Equal(1110m, reconciliation.TotalExpected);
        Assert.All(reconciliation.PaymentMethods, x => Assert.Equal(expected[x.Code], x.ExpectedAmount));
        var closed = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(reconciliation)), HttpStatusCode.OK);
        Assert.Equal(1110m, closed.TotalCounted);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(4, await context.PagoVenta.CountAsync(x => x.MovimientoCajaId == linkedMovement));
        Assert.Equal(5, await context.MovimientoCaja.CountAsync());
        Assert.Equal(210m, await context.TurnoCaja.Select(x => x.EfectivoEsperado).SingleAsync());
        Assert.Equal(0m, await context.TurnoCaja.Select(x => x.DiferenciaTotal).SingleAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task AppliedPaymentWithoutMovement_IsAttributedExactlyOnceAlongsideLinkedPayments()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var linkedMovement = await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment("TRANSFERENCIA", 200m)]);
        await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment("NEQUI", 300m)], linkMovement: false);

        var reconciliation = await ReconcileAsync(client);
        Assert.Equal(600m, reconciliation.TotalExpected);
        Assert.Equal(100m, Assert.Single(reconciliation.PaymentMethods,
            x => x.Code == "EFECTIVO").ExpectedAmount);
        Assert.Equal(200m, Assert.Single(reconciliation.PaymentMethods,
            x => x.Code == "TRANSFERENCIA").ExpectedAmount);
        Assert.Equal(300m, Assert.Single(reconciliation.PaymentMethods,
            x => x.Code == "NEQUI").ExpectedAmount);

        var closed = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(reconciliation)), HttpStatusCode.OK);
        Assert.Equal(600m, closed.TotalExpected);
        Assert.Equal(600m, closed.TotalCounted);
        Assert.Equal(0m, closed.TotalDifference);
        Assert.Equal(0m, closed.CashDifference);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Single(await context.PagoVenta.AsNoTracking()
            .Where(x => x.MovimientoCajaId == linkedMovement).ToArrayAsync());
        Assert.Single(await context.PagoVenta.AsNoTracking()
            .Where(x => x.MovimientoCajaId == null).ToArrayAsync());
        Assert.Equal(2, await context.MovimientoCaja.CountAsync());
        var details = await context.DetalleArqueoMedioPago.AsNoTracking()
            .Where(x => x.TurnoCajaId == scenario.Shift.Id).ToArrayAsync();
        Assert.Equal(600m, details.Sum(x => x.ValorEsperado));
        Assert.Equal(600m, details.Sum(x => x.ValorContado));
        Assert.Equal(0m, details.Sum(x => x.Diferencia));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("APLICADO")]
    [InlineData("ANULADO")]
    public async Task ExplicitReversal_NetsCombinedPaymentOnce_RegardlessOfOriginalPaymentState(string state)
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        await AddFourthPaymentMethodAsync();
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var original = await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
        [
            new FinancialPayment("EFECTIVO", 10m, 50m), new FinancialPayment("TRANSFERENCIA", 20m),
            new FinancialPayment("NEQUI", 30m), new FinancialPayment("TARJETA_TEST", 40m)
        ]);
        await SeedFinancialReversalAsync(scenario, scenario.Shift.Id, original, 100m, state);

        var reconciliation = await ReconcileAsync(client);
        Assert.Equal(100m, reconciliation.TotalExpected);
        Assert.All(reconciliation.PaymentMethods,
            x => Assert.Equal(x.Code == "EFECTIVO" ? 100m : 0m, x.ExpectedAmount));
        await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(reconciliation)), HttpStatusCode.OK);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(3, await context.MovimientoCaja.CountAsync());
        Assert.Equal(4, await context.PagoVenta.CountAsync(x => x.Estado == state));
        Assert.Equal(100m, await context.DetalleArqueoMedioPago.SumAsync(x => x.ValorEsperado));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("TRANSFERENCIA", "APLICADO", false)]
    [InlineData("TRANSFERENCIA", "ANULADO", false)]
    [InlineData("NEQUI", "APLICADO", false)]
    [InlineData("TARJETA_TEST", "ANULADO", false)]
    [InlineData("TRANSFERENCIA", "APLICADO", true)]
    public async Task ElectronicRefund_FromPreviousShift_CanCloseWithNegativeExpectedAndCounted(
        string methodCode, string paymentState, bool atNumericLimit)
    {
        var amount = atNumericLimit ? CashMoney.Maximum : 75m;
        var scenario = await PrepareCorrectionScenarioAsync(0m);
        await AddFourthPaymentMethodAsync();
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var original = await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment(methodCode, amount)]);
        await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(await ReconcileAsync(client))), HttpStatusCode.OK);
        var next = await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Turno de devolución electrónica")), HttpStatusCode.Created);
        await SeedFinancialReversalAsync(scenario, next.Id, original, amount, paymentState);

        var reconciliation = await ReconcileAsync(client);
        Assert.Equal(-amount, reconciliation.TotalExpected);
        Assert.Equal(-amount, Assert.Single(reconciliation.PaymentMethods, x => x.Code == methodCode).ExpectedAmount);
        Assert.Equal(0m, Assert.Single(reconciliation.PaymentMethods, x => x.Code == "EFECTIVO").ExpectedAmount);
        var closed = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{next.Id}/close", BalancedClose(reconciliation)), HttpStatusCode.OK);
        Assert.Equal(-amount, closed.TotalCounted);
        Assert.Equal(0m, closed.CashDifference);
        Assert.Equal(0m, closed.TotalDifference);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var details = await context.DetalleArqueoMedioPago.AsNoTracking().ToArrayAsync();
        Assert.Equal(amount, details.Where(x => x.TurnoCajaId == scenario.Shift.Id).Sum(x => x.ValorEsperado));
        Assert.Equal(-amount, details.Where(x => x.TurnoCajaId == next.Id).Sum(x => x.ValorEsperado));
        Assert.Equal(-amount, details.Where(x => x.TurnoCajaId == next.Id).Sum(x => x.ValorContado));
        Assert.Equal(2, await context.TurnoCaja.CountAsync(x => x.Estado == CashConstants.ClosedState));
        Assert.Equal(0, await context.AutorizacionCierreTurno.CountAsync());
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccumulatedPayments_OutsideNumeric18Range_Return400WithoutFinancialWrites(bool sameMethod)
    {
        var scenario = await PrepareCorrectionScenarioAsync(0m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment("TRANSFERENCIA", CashMoney.Maximum)]);
        var atLimit = await ReconcileAsync(client);
        Assert.Equal(CashMoney.Maximum, atLimit.TotalExpected);
        await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment(sameMethod ? "TRANSFERENCIA" : "NEQUI", 1m)]);
        var before = await CaptureFinancialStateAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/cash/reconciliation")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/cash/movements/incomes",
            new CreateCashMovementRequest(1m, "Acumulado de pagos fuera del rango", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(atLimit))).StatusCode);
        Assert.Equal(before, await CaptureFinancialStateAsync());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(CashConstants.OpenState, await context.TurnoCaja.Select(x => x.Estado).SingleAsync());
        Assert.Equal(2, await context.PagoVenta.CountAsync());
        Assert.Equal(0, await context.AutorizacionCierreTurno.CountAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CashRefund_CannotMakePhysicalExpectedBalanceNegative_InApiOrDatabase()
    {
        var scenario = await PrepareCorrectionScenarioAsync(0m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var original = await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment("EFECTIVO", 75m)]);
        await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(await ReconcileAsync(client))), HttpStatusCode.OK);
        var next = await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Devolución sin fondos físicos")), HttpStatusCode.Created);
        await SeedFinancialReversalAsync(scenario, next.Id, original, 75m, "APLICADO");
        var before = await CaptureFinancialStateAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/cash/reconciliation")).StatusCode);
        Assert.Equal(before, await CaptureFinancialStateAsync());

        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand("""
                INSERT INTO caja.detalle_arqueo_medio_pago
                  (id, turno_caja_id, metodo_pago_id, valor_esperado, valor_contado, diferencia)
                SELECT @id, @shift, id, -75, -75, 0 FROM catalogo.metodo_pago WHERE codigo = 'EFECTIVO';
                """, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("shift", next.Id);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            await transaction.RollbackAsync();
        }
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(CashConstants.OpenState, await context.TurnoCaja.Where(x => x.Id == next.Id)
            .Select(x => x.Estado).SingleAsync());
        Assert.Equal(0, await context.DetalleArqueoMedioPago.CountAsync(x => x.TurnoCajaId == next.Id));
        Assert.Equal(before, await CaptureFinancialStateAsync());
    }

    // Only database fixtures for Caja's future integration contract; no Ventas endpoint or service is introduced.
    private async Task<Guid> SeedFinancialPaymentAsync(CorrectionScenario scenario, Guid shiftId,
        IReadOnlyCollection<FinancialPayment> payments, bool linkMovement = true)
    {
        var movementId = Guid.CreateVersion7();
        var saleId = Guid.CreateVersion7();
        var categoryId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var detailId = Guid.CreateVersion7();
        var fixtureCode = $"FIN-{productId:N}";
        var total = payments.Sum(x => x.Applied);
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var movementSql = linkMovement
            ? """
              INSERT INTO caja.movimiento_caja
                (id, turno_caja_id, usuario_id, tipo, categoria_movimiento, valor, fecha_hora, correlacion_id)
              VALUES (@movement, @shift, @user, 'INGRESO', 'PAGO_VENTA', @amount, @now, @correlation);
              """
            : string.Empty;
        await using (var command = new NpgsqlCommand(movementSql + """
            INSERT INTO catalogo.categoria
              (id, nombre, usuario_creacion_id)
            VALUES (@category, @fixture_code, @user);
            INSERT INTO catalogo.producto
              (id, categoria_id, unidad_medida_id, codigo_interno, nombre,
               precio_minorista, usuario_creacion_id)
            SELECT @product, @category, id, @fixture_code, 'Producto financiero aislado',
              @amount, @user
            FROM catalogo.unidad_medida
            WHERE activo
            ORDER BY codigo
            LIMIT 1;
            INSERT INTO ventas.venta
              (id, instalacion_id, turno_caja_id, usuario_id, prefijo, serie, consecutivo,
               fecha_hora, fecha_operativa, fecha_limite_cambio, tipo_venta, subtotal_bruto, total)
            SELECT @sale, instalacion_id, @shift, @user, 'TEST', 'FINANCE',
              (SELECT coalesce(max(consecutivo), 0) + 1 FROM ventas.venta),
              @now, @date, @date, 'CONTADO', @amount, @amount
            FROM caja.caja WHERE id = (SELECT caja_id FROM caja.turno_caja WHERE id = @shift);
            INSERT INTO ventas.detalle_venta
              (id, venta_id, producto_id, codigo_producto, nombre_producto, unidad_medida,
               cantidad, tipo_precio, precio_unitario, subtotal_bruto, subtotal_neto)
            VALUES (@detail, @sale, @product, @fixture_code, 'Producto financiero aislado',
              'UNIDAD', 1, 'MINORISTA', @amount, @amount, @amount);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("movement", movementId);
            command.Parameters.AddWithValue("sale", saleId);
            command.Parameters.AddWithValue("category", categoryId);
            command.Parameters.AddWithValue("product", productId);
            command.Parameters.AddWithValue("detail", detailId);
            command.Parameters.AddWithValue("fixture_code", fixtureCode);
            command.Parameters.AddWithValue("shift", shiftId);
            command.Parameters.AddWithValue("user", scenario.Administrator.User.Id);
            command.Parameters.AddWithValue("amount", total);
            command.Parameters.AddWithValue("now", _clock.UtcNow.UtcDateTime);
            command.Parameters.AddWithValue("date", scenario.Shift.OperatingDate);
            command.Parameters.AddWithValue("correlation", Guid.CreateVersion7());
            await command.ExecuteNonQueryAsync();
        }
        foreach (var payment in payments)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO ventas.pago_venta
                  (id, venta_id, metodo_pago_id, movimiento_caja_id, usuario_id,
                   valor_aplicado, valor_recibido, cambio, referencia, fecha_hora)
                SELECT @id, @sale, id, @movement, @user, @applied,
                  CASE WHEN afecta_efectivo THEN coalesce(@received, @applied) ELSE NULL END,
                  CASE WHEN afecta_efectivo THEN coalesce(@received, @applied) - @applied ELSE 0 END,
                  CASE WHEN requiere_referencia THEN @reference ELSE NULL END,
                  @now
                FROM catalogo.metodo_pago WHERE codigo = @code;
                """, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("sale", saleId);
            command.Parameters.AddWithValue("movement", NpgsqlTypes.NpgsqlDbType.Uuid,
                linkMovement ? movementId : DBNull.Value);
            command.Parameters.AddWithValue("user", scenario.Administrator.User.Id);
            command.Parameters.AddWithValue("applied", payment.Applied);
            command.Parameters.AddWithValue("received", NpgsqlTypes.NpgsqlDbType.Numeric,
                (object?)payment.Received ?? DBNull.Value);
            command.Parameters.AddWithValue("reference", $"FIN-{Guid.CreateVersion7():N}");
            command.Parameters.AddWithValue("now", _clock.UtcNow.UtcDateTime);
            command.Parameters.AddWithValue("code", payment.Code);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        await transaction.CommitAsync();
        return movementId;
    }

    private async Task SeedFinancialReversalAsync(CorrectionScenario scenario, Guid targetShiftId,
        Guid originalMovementId, decimal amount, string paymentState)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO caja.movimiento_caja
              (id, turno_caja_id, usuario_id, movimiento_revertido_id, tipo, categoria_movimiento,
               valor, fecha_hora, correlacion_id)
            VALUES (@id, @shift, @user, @original, 'REVERSO', 'REVERSO_PAGO', @amount, @now, @correlation);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("shift", targetShiftId);
        command.Parameters.AddWithValue("user", scenario.Administrator.User.Id);
        command.Parameters.AddWithValue("original", originalMovementId);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("now", _clock.UtcNow.UtcDateTime);
        command.Parameters.AddWithValue("correlation", Guid.CreateVersion7());
        await command.ExecuteNonQueryAsync();
        if (paymentState == "ANULADO")
        {
            await using var cancellation = new NpgsqlCommand("""
                INSERT INTO configuracion.motivo_operacion
                  (id, establecimiento_id, tipo_operacion, codigo, nombre)
                SELECT @reason, id, 'ANULACION_PAGO', 'TEST_REVERSAL', 'Reverso aislado'
                FROM configuracion.establecimiento;
                INSERT INTO configuracion.motivo_operacion
                  (id, establecimiento_id, tipo_operacion, codigo, nombre)
                SELECT @sale_reason, id, 'ANULACION_VENTA', 'TEST_SALE_REVERSAL',
                  'Anulacion de venta aislada'
                FROM configuracion.establecimiento;
                INSERT INTO seguridad.autorizacion_operacion
                  (id, usuario_solicitante_id, usuario_autorizador_id, permiso_id, tipo_operacion,
                   correlacion_id, descripcion_operacion, metodo_autenticacion, resultado, estado,
                   fecha_solicitud, fecha_expiracion, fecha_utilizacion)
                SELECT @authorization, @user, @supervisor, id, 'ANULACION_VENTA', @correlation,
                  'Fixture aislado de reverso explícito', 'PASSWORD', 'EXITOSA', 'UTILIZADA',
                  @now, @now + interval '5 minutes', @now
                FROM seguridad.permiso WHERE codigo = 'CASH.SHIFT_MANAGE';
                UPDATE ventas.pago_venta SET estado = 'ANULADO', usuario_anulacion_id = @user,
                  autorizacion_anulacion_id = @authorization, motivo_anulacion_id = @reason,
                  fecha_anulacion = @now
                WHERE movimiento_caja_id = @original;
                UPDATE ventas.venta SET estado = 'ANULADA', version = version + 1
                WHERE id = (
                    SELECT venta_id FROM ventas.pago_venta
                    WHERE movimiento_caja_id = @original LIMIT 1
                );
                INSERT INTO ventas.anulacion_venta
                  (id, venta_id, usuario_solicitante_id, usuario_autorizador_id,
                   autorizacion_operacion_id, motivo_operacion_id, fecha_hora, correlacion_id)
                SELECT @annulment, venta_id, @user, @supervisor, @authorization, @sale_reason,
                  @now, @correlation
                FROM ventas.pago_venta WHERE movimiento_caja_id = @original LIMIT 1;
                """, connection, transaction);
            cancellation.Parameters.AddWithValue("reason", Guid.CreateVersion7());
            cancellation.Parameters.AddWithValue("sale_reason", Guid.CreateVersion7());
            cancellation.Parameters.AddWithValue("annulment", Guid.CreateVersion7());
            cancellation.Parameters.AddWithValue("authorization", Guid.CreateVersion7());
            cancellation.Parameters.AddWithValue("user", scenario.Administrator.User.Id);
            cancellation.Parameters.AddWithValue("supervisor", scenario.Users.Supervisor.Id);
            cancellation.Parameters.AddWithValue("correlation", Guid.CreateVersion7());
            cancellation.Parameters.AddWithValue("now", _clock.UtcNow.UtcDateTime);
            cancellation.Parameters.AddWithValue("original", originalMovementId);
            await cancellation.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private sealed record FinancialPayment(string Code, decimal Applied, decimal? Received = null);
}
