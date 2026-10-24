using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Reports;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    [Theory, Trait("Category", "Integration")]
    [InlineData("COBRO", "EFECTIVO", 40)]
    [InlineData("COBRO", "TRANSFERENCIA", 40)]
    [InlineData("DEVOLUCION", "EFECTIVO", -40)]
    [InlineData("DEVOLUCION", "TRANSFERENCIA", -40)]
    public async Task DailyCashFlow_ChangePaymentPreservesSignMethodAndShiftDateWithoutCountingLinkedMovement(
        string type, string methodCode, int signedAmount)
    {
        var scenario = await PrepareCorrectionScenarioAsync(500m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var saleMovementId = await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment("EFECTIVO", 100m, 100m)]);
        await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(await ReconcileAsync(client))), HttpStatusCode.OK);
        var next = await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(500m, "Turno de cambio para reporte")), HttpStatusCode.Created);
        var operationDate = scenario.Shift.OperatingDate.AddDays(1);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        // Deliberately differ from both the sale date and payment timestamp's date.
        // The report must use the payment shift's operational date.
        await db.TurnoCaja.Where(x => x.Id == next.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FechaOperativa, operationDate));
        var changeMovementId = await SeedCashFlowChangePaymentAsync(scenario, next.Id, saleMovementId, type, methodCode);

        var payment = await db.PagoCambioVenta.AsNoTracking().SingleAsync();
        Assert.Equal(changeMovementId, payment.MovimientoCajaId);
        Assert.Equal(40m, payment.Valor);
        Assert.Equal(type == "COBRO" && methodCode == "EFECTIVO" ? 10m : 0m, payment.Cambio);
        var origin = type == "COBRO" ? "COBRO_CAMBIO_VENTA" : "DEVOLUCION_CAMBIO_VENTA";
        var repository = scope.ServiceProvider.GetRequiredService<IDailyCashFlowRepository>();
        var installationId = await db.Caja.Select(x => x.InstalacionId).SingleAsync();
        var entries = await repository.GetEntriesAsync(installationId, operationDate, operationDate, default);
        Assert.Equal(new CashFlowEntry(operationDate, origin, methodCode, signedAmount),
            Assert.Single(entries, x => x.Method is not null));
        Assert.Equal(500m, Assert.Single(entries, x => x.Method is null).Amount);
        Assert.DoesNotContain(entries, x => x.Method is null && x.Origin != "APERTURA");
        var originalDay = await repository.GetEntriesAsync(installationId,
            scenario.Shift.OperatingDate, scenario.Shift.OperatingDate, default);
        Assert.DoesNotContain(originalDay, x => x.Origin == origin);
        Assert.Equal(100m, Assert.Single(originalDay, x => x.Method is not null).Amount);

        // Independently compare with the production Caja calculation, including all methods.
        var expected = await scope.ServiceProvider.GetRequiredService<ICashRepository>()
            .GetExpectedAmountsAsync(next.Id);
        foreach (var method in expected)
            Assert.Equal((method.Code == "EFECTIVO" ? 500m : 0m) +
                (method.Code == methodCode ? signedAmount : 0m), method.ExpectedAmount);
        var report = await ReadSuccessAsync<DailyCashFlowReport>(await client.GetAsync(
            $"/api/reports/cash-flow/daily?from={operationDate:yyyy-MM-dd}&to={operationDate:yyyy-MM-dd}"), HttpStatusCode.OK);
        Assert.Equal(signedAmount, report.PaymentTotal);
        Assert.Equal(0m, report.ManualTotal);
        Assert.Equal(500m, report.OpeningBalance);
        Assert.Equal(expected.Sum(x => x.ExpectedAmount),
            report.OpeningBalance + report.PaymentTotal + report.ManualTotal);
        Assert.Equal(new CashFlowEntry(operationDate, origin, methodCode, signedAmount),
            Assert.Single(Assert.Single(report.Days).Payments));
    }

    private async Task<Guid> SeedCashFlowChangePaymentAsync(CorrectionScenario scenario, Guid shiftId,
        Guid saleMovementId, string type, string methodCode)
    {
        var movementId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // Keep all official constraints and triggers enabled, including deferred settlement validation.
        await using var command = new NpgsqlCommand("""
            INSERT INTO seguridad.autorizacion_operacion
              (id, usuario_solicitante_id, usuario_autorizador_id, permiso_id, tipo_operacion,
               correlacion_id, descripcion_operacion, metodo_autenticacion, resultado, estado,
               fecha_solicitud, fecha_expiracion, fecha_utilizacion)
            SELECT @authorization, @user, @supervisor, id, 'CAMBIO_VENTA', @correlation,
              'Cambio aislado para validar reportes', 'PASSWORD', 'EXITOSA', 'UTILIZADA',
              @now, @now + interval '5 minutes', @now
            FROM seguridad.permiso WHERE codigo = 'SALES.CHANGE_AUTHORIZE';

            INSERT INTO ventas.cambio_venta
              (id, venta_id, instalacion_id, turno_caja_id, autorizacion_operacion_id, usuario_id,
               prefijo, serie, consecutivo, fecha_hora, total_devuelto, total_entregado, diferencia,
               tratamiento_diferencia, valor_pago_o_devolucion, correlacion_id)
            SELECT @change, v.id, v.instalacion_id, @shift, @authorization, @user,
              'CAM', 'REPORTC2', 1, @now, 100, @delivered, @difference,
              @treatment, 40, @correlation
            FROM ventas.venta v JOIN ventas.pago_venta p ON p.venta_id = v.id
            WHERE p.movimiento_caja_id = @sale_movement;

            INSERT INTO ventas.detalle_cambio_venta
              (id, cambio_venta_id, detalle_venta_origen_id, producto_id, tipo_detalle,
               codigo_producto, nombre_producto, unidad_medida, cantidad, precio_unitario, subtotal_neto)
            SELECT @returned_detail, @change, d.id, d.producto_id, 'DEVUELTO',
              d.codigo_producto, d.nombre_producto, d.unidad_medida, 1, 100, 100
            FROM ventas.detalle_venta d JOIN ventas.cambio_venta c ON c.venta_id = d.venta_id
            WHERE c.id = @change;
            INSERT INTO ventas.detalle_cambio_venta
              (id, cambio_venta_id, producto_id, tipo_detalle,
               codigo_producto, nombre_producto, unidad_medida, cantidad, precio_unitario, subtotal_neto)
            SELECT @delivered_detail, @change, d.producto_id, 'ENTREGADO',
              d.codigo_producto, d.nombre_producto, d.unidad_medida, 1, @delivered, @delivered
            FROM ventas.detalle_venta d JOIN ventas.cambio_venta c ON c.venta_id = d.venta_id
            WHERE c.id = @change;

            INSERT INTO caja.movimiento_caja
              (id, turno_caja_id, usuario_id, tipo, categoria_movimiento, valor, fecha_hora, correlacion_id)
            VALUES (@movement, @shift, @user, @movement_type, 'CAMBIO_VENTA', 40, @now, @correlation);
            INSERT INTO ventas.pago_cambio_venta
              (id, cambio_venta_id, turno_caja_id, metodo_pago_id, usuario_id, movimiento_caja_id,
               tipo_movimiento, valor, valor_recibido, cambio, referencia, fecha_hora)
            SELECT @payment, @change, @shift, id, @user, @movement, @type, 40,
              CASE WHEN @type = 'COBRO' AND afecta_efectivo THEN 50 ELSE NULL END,
              CASE WHEN @type = 'COBRO' AND afecta_efectivo THEN 10 ELSE 0 END,
              CASE WHEN requiere_referencia THEN 'REPORT-C2-CHANGE' ELSE NULL END, @now
            FROM catalogo.metodo_pago WHERE codigo = @method;
            """, connection, transaction);
        command.Parameters.AddWithValue("authorization", Guid.CreateVersion7());
        command.Parameters.AddWithValue("user", scenario.Administrator.User.Id);
        command.Parameters.AddWithValue("supervisor", scenario.Supervisor.User.Id);
        command.Parameters.AddWithValue("correlation", Guid.CreateVersion7());
        command.Parameters.AddWithValue("now", _clock.UtcNow.UtcDateTime);
        command.Parameters.AddWithValue("change", Guid.CreateVersion7());
        command.Parameters.AddWithValue("shift", shiftId);
        command.Parameters.AddWithValue("delivered", type == "COBRO" ? 140m : 60m);
        command.Parameters.AddWithValue("difference", type == "COBRO" ? 40m : -40m);
        command.Parameters.AddWithValue("treatment", type == "COBRO" ? "PAGO_INMEDIATO" : "DEVOLUCION");
        command.Parameters.AddWithValue("sale_movement", saleMovementId);
        command.Parameters.AddWithValue("returned_detail", Guid.CreateVersion7());
        command.Parameters.AddWithValue("delivered_detail", Guid.CreateVersion7());
        command.Parameters.AddWithValue("movement", movementId);
        command.Parameters.AddWithValue("movement_type", type == "COBRO" ? "INGRESO" : "EGRESO");
        command.Parameters.AddWithValue("payment", Guid.CreateVersion7());
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("method", methodCode);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return movementId;
    }
}
