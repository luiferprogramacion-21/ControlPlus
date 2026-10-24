using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Reports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task ReportCashFlowCoverage_AnnulledPaymentWithoutReversalDoesNotBecomeManualIncome()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var movementId = await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment("TRANSFERENCIA", 75m)]);
        // Annul the document and its payment with all required history, without a Caja reversal.
        // Keep the linked movement so an incorrect fallback to manual income is observable.
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand("""
                INSERT INTO configuracion.motivo_operacion
                  (id, establecimiento_id, tipo_operacion, codigo, nombre)
                SELECT @reason, id, 'ANULACION_PAGO', 'REPORT_PAYMENT_NO_REVERSAL', 'Pago anulado sin reverso'
                FROM configuracion.establecimiento;
                INSERT INTO configuracion.motivo_operacion
                  (id, establecimiento_id, tipo_operacion, codigo, nombre)
                SELECT @sale_reason, id, 'ANULACION_VENTA', 'REPORT_SALE_NO_REVERSAL', 'Venta anulada de prueba'
                FROM configuracion.establecimiento;
                INSERT INTO seguridad.autorizacion_operacion
                  (id, usuario_solicitante_id, usuario_autorizador_id, permiso_id, tipo_operacion,
                   correlacion_id, descripcion_operacion, metodo_autenticacion, resultado, estado,
                   fecha_solicitud, fecha_expiracion, fecha_utilizacion)
                SELECT @authorization, @user, @supervisor, id, 'ANULACION_VENTA', @correlation,
                  'Anulacion aislada sin reverso para reportes', 'PASSWORD', 'EXITOSA', 'UTILIZADA',
                  @now, @now + interval '5 minutes', @now
                FROM seguridad.permiso WHERE codigo = 'SALES.VOID';
                UPDATE ventas.pago_venta SET estado = 'ANULADO', usuario_anulacion_id = @user,
                  autorizacion_anulacion_id = @authorization, motivo_anulacion_id = @reason,
                  fecha_anulacion = @now
                WHERE movimiento_caja_id = @movement;
                UPDATE ventas.venta SET estado = 'ANULADA', version = version + 1
                WHERE id = (SELECT venta_id FROM ventas.pago_venta WHERE movimiento_caja_id = @movement);
                INSERT INTO ventas.anulacion_venta
                  (id, venta_id, usuario_solicitante_id, usuario_autorizador_id,
                   autorizacion_operacion_id, motivo_operacion_id, fecha_hora, correlacion_id)
                SELECT @annulment, venta_id, @user, @supervisor, @authorization, @sale_reason,
                  @now, @correlation
                FROM ventas.pago_venta WHERE movimiento_caja_id = @movement;
                """, connection, transaction);
            command.Parameters.AddWithValue("reason", Guid.CreateVersion7());
            command.Parameters.AddWithValue("sale_reason", Guid.CreateVersion7());
            command.Parameters.AddWithValue("authorization", Guid.CreateVersion7());
            command.Parameters.AddWithValue("correlation", Guid.CreateVersion7());
            command.Parameters.AddWithValue("annulment", Guid.CreateVersion7());
            command.Parameters.AddWithValue("user", scenario.Administrator.User.Id);
            command.Parameters.AddWithValue("supervisor", scenario.Supervisor.User.Id);
            command.Parameters.AddWithValue("movement", movementId);
            command.Parameters.AddWithValue("now", _clock.UtcNow.UtcDateTime);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var payment = await db.PagoVenta.AsNoTracking().SingleAsync();
        Assert.Equal("ANULADO", payment.Estado);
        Assert.NotNull(payment.FechaAnulacion);
        Assert.Equal(movementId, payment.MovimientoCajaId);
        Assert.True(await db.MovimientoCaja.AnyAsync(x => x.Id == movementId));
        Assert.False(await db.MovimientoCaja.AnyAsync(x => x.MovimientoRevertidoId == movementId));
        var report = await ReadSuccessAsync<DailyCashFlowReport>(await client.GetAsync(
            $"/api/reports/cash-flow/daily?from={scenario.Shift.OperatingDate:yyyy-MM-dd}&to={scenario.Shift.OperatingDate.AddDays(1):yyyy-MM-dd}"), HttpStatusCode.OK);
        Assert.Equal(100m, report.OpeningBalance);
        Assert.Equal(0m, report.PaymentTotal);
        Assert.Equal(0m, report.ManualTotal);
        Assert.All(report.Days, day =>
        {
            Assert.Empty(day.Payments);
            Assert.Empty(day.ManualMovements);
        });
        var expected = await scope.ServiceProvider.GetRequiredService<ICashRepository>()
            .GetExpectedAmountsAsync(scenario.Shift.Id);
        Assert.All(expected, x => Assert.Equal(x.Code == "EFECTIVO" ? 100m : 0m, x.ExpectedAmount));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("incomes", "INGRESO_MANUAL", 25)]
    [InlineData("expenses", "GASTO_MANUAL", -25)]
    [InlineData("cash-drops", "SANGRIA_MANUAL", -25)]
    public async Task ReportCashFlowCoverage_ManualReversalUsesOwnTurnDateAndOppositeSign(
        string route, string origin, int signedAmount)
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var original = await ReadSuccessAsync<CashMovementDto>(await client.PostAsJsonAsync(
            $"/api/cash/movements/{route}", new CreateCashMovementRequest(25m, "Movimiento manual de prueba", null)), HttpStatusCode.Created);
        await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(await ReconcileAsync(client))), HttpStatusCode.OK);
        var next = await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(100m, "Turno del reverso manual")), HttpStatusCode.Created);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var nextDate = scenario.Shift.OperatingDate.AddDays(1);
        await db.TurnoCaja.Where(x => x.Id == next.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FechaOperativa, nextDate));
        db.MovimientoCaja.Add(new MovimientoCaja
        {
            Id = Guid.CreateVersion7(), TurnoCajaId = next.Id, UsuarioId = scenario.Administrator.User.Id,
            MovimientoRevertidoId = original.Id, Tipo = "REVERSO", CategoriaMovimiento = "REVERSO_MANUAL",
            Valor = 25m, FechaHora = _clock.UtcNow.UtcDateTime, CorrelacionId = Guid.CreateVersion7()
        });
        await db.SaveChangesAsync();
        var installationId = await db.Caja.Select(x => x.InstalacionId).SingleAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IDailyCashFlowRepository>();
        var first = await repository.GetEntriesAsync(installationId,
            scenario.Shift.OperatingDate, scenario.Shift.OperatingDate, default);
        Assert.Equal(signedAmount, Assert.Single(first, x => x.Origin == origin).Amount);
        Assert.DoesNotContain(first, x => x.Origin.StartsWith("REVERSO_", StringComparison.Ordinal));
        // Read the reversal day alone: the original lies outside the report's date range.
        var second = await repository.GetEntriesAsync(installationId, nextDate, nextDate, default);
        Assert.Equal(new CashFlowEntry(nextDate, "REVERSO_" + origin, null, -signedAmount),
            Assert.Single(second, x => x.Origin != "APERTURA"));
        Assert.DoesNotContain(second, x => x.Method is not null);
        var expected = await scope.ServiceProvider.GetRequiredService<ICashRepository>()
            .GetExpectedAmountsAsync(next.Id);
        Assert.Equal(100m - signedAmount, expected.Sum(x => x.ExpectedAmount));
        Assert.Equal(expected.Sum(x => x.ExpectedAmount), second.Sum(x => x.Amount));
    }
}
