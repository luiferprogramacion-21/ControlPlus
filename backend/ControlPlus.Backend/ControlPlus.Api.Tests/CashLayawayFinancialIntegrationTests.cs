using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task LayawayMixedInitialPayment_AttributesEachMethodOnceAndExcludesLinkedMovement()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var movementId = await SeedLayawayInitialPaymentAsync(scenario);

        var reconciliation = await ReconcileAsync(client);
        Assert.Equal(700m, reconciliation.TotalExpected);
        Assert.Equal(300m, Assert.Single(reconciliation.PaymentMethods,
            method => method.Code == "EFECTIVO").ExpectedAmount);
        Assert.Equal(150m, Assert.Single(reconciliation.PaymentMethods,
            method => method.Code == "TRANSFERENCIA").ExpectedAmount);
        Assert.Equal(250m, Assert.Single(reconciliation.PaymentMethods,
            method => method.Code == "NEQUI").ExpectedAmount);

        var closed = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(reconciliation)), HttpStatusCode.OK);
        Assert.Equal(700m, closed.TotalExpected);
        Assert.Equal(0m, closed.TotalDifference);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var payments = await context.PagoApartado.AsNoTracking().ToArrayAsync();
        Assert.Equal(3, payments.Length);
        Assert.All(payments, payment => Assert.Equal(movementId, payment.MovimientoCajaId));
        Assert.Equal(600m, payments.Sum(payment => payment.ValorAplicado));
        Assert.Equal(50m, payments.Single(payment => payment.Cambio > 0m).Cambio);
        Assert.Equal(700m, await context.DetalleArqueoMedioPago
            .Where(detail => detail.TurnoCajaId == scenario.Shift.Id)
            .SumAsync(detail => detail.ValorEsperado));
        Assert.Equal(300m, await context.TurnoCaja.Where(shift => shift.Id == scenario.Shift.Id)
            .Select(shift => shift.EfectivoEsperado).SingleAsync());
    }

    // A focused Caja fixture: the Apartados API exercises creation and authorization separately.
    private async Task<Guid> SeedLayawayInitialPaymentAsync(CorrectionScenario scenario)
    {
        var customerId = Guid.CreateVersion7();
        var categoryId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var authorizationId = Guid.CreateVersion7();
        var layawayId = Guid.CreateVersion7();
        var detailId = Guid.CreateVersion7();
        var movementId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var code = $"LAY-CASH-{productId:N}";
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var command = new NpgsqlCommand("""
            INSERT INTO catalogo.cliente
              (id, nombre_completo, direccion, telefono, usuario_creacion_id)
            VALUES (@customer, 'Cliente de caja apartado', 'Dirección de prueba', '3110000000', @operator);
            INSERT INTO catalogo.categoria
              (id, nombre, usuario_creacion_id)
            VALUES (@category, @code, @operator);
            INSERT INTO catalogo.producto
              (id, categoria_id, unidad_medida_id, codigo_interno, nombre,
               precio_minorista, stock_actual, stock_reservado, usuario_creacion_id)
            SELECT @product, @category, id, @code, 'Producto apartado de caja',
              600, 1, 1, @operator
            FROM catalogo.unidad_medida
            WHERE activo ORDER BY codigo LIMIT 1;
            INSERT INTO seguridad.autorizacion_operacion
              (id, usuario_solicitante_id, usuario_autorizador_id, permiso_id, tipo_operacion,
               correlacion_id, descripcion_operacion, metodo_autenticacion, resultado, estado,
               fecha_solicitud, fecha_expiracion, fecha_utilizacion)
            SELECT @authorization, @operator, @supervisor, id, 'CREACION_APARTADO',
              @correlation, 'Fixture financiero de apartado', 'PASSWORD', 'EXITOSA', 'UTILIZADA',
              @now, @now + interval '5 minutes', @now
            FROM seguridad.permiso WHERE codigo = 'LAYAWAY.MANAGE';
            INSERT INTO ventas.apartado
              (id, instalacion_id, turno_caja_id, cliente_id, usuario_id, autorizacion_operacion_id,
               prefijo, serie, consecutivo, fecha_hora, fecha_limite, porcentaje_minimo_inicial,
               abono_inicial, subtotal_bruto, total, saldo_pendiente, estado, fecha_pago)
            SELECT @layaway, instalacion_id, @shift, @customer, @operator, @authorization,
              'APT', 'CASH', 1, @now, @deadline, 20, 600, 600, 600, 0, 'PAGADO', @now
            FROM caja.caja WHERE id = (SELECT caja_id FROM caja.turno_caja WHERE id = @shift);
            INSERT INTO ventas.detalle_apartado
              (id, apartado_id, producto_id, codigo_producto, nombre_producto, unidad_medida,
               cantidad, precio_reserva, subtotal_bruto, subtotal_neto)
            VALUES (@detail, @layaway, @product, @code, 'Producto apartado de caja',
              'UNIDAD', 1, 600, 600, 600);
            INSERT INTO caja.movimiento_caja
              (id, turno_caja_id, usuario_id, tipo, categoria_movimiento, valor, fecha_hora,
               correlacion_id)
            VALUES (@movement, @shift, @operator, 'INGRESO', 'PAGO_APARTADO', 600, @now,
              @correlation);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("customer", customerId);
            command.Parameters.AddWithValue("category", categoryId);
            command.Parameters.AddWithValue("product", productId);
            command.Parameters.AddWithValue("authorization", authorizationId);
            command.Parameters.AddWithValue("layaway", layawayId);
            command.Parameters.AddWithValue("detail", detailId);
            command.Parameters.AddWithValue("movement", movementId);
            command.Parameters.AddWithValue("correlation", correlationId);
            command.Parameters.AddWithValue("code", code);
            command.Parameters.AddWithValue("operator", scenario.Administrator.User.Id);
            command.Parameters.AddWithValue("supervisor", scenario.Users.Supervisor.Id);
            command.Parameters.AddWithValue("shift", scenario.Shift.Id);
            command.Parameters.AddWithValue("now", _clock.UtcNow.UtcDateTime);
            command.Parameters.AddWithValue("deadline", scenario.Shift.OperatingDate.AddDays(15));
            await command.ExecuteNonQueryAsync();
        }

        var sequence = 0;
        foreach (var (methodCode, applied, received) in new[]
                 {
                     ("EFECTIVO", 200m, (decimal?)250m),
                     ("TRANSFERENCIA", 150m, (decimal?)null),
                     ("NEQUI", 250m, (decimal?)null)
                 })
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO ventas.pago_apartado
                  (id, apartado_id, instalacion_id, turno_caja_id, metodo_pago_id, usuario_id,
                   movimiento_caja_id, prefijo, serie, consecutivo, valor_aplicado,
                   valor_recibido, cambio, referencia, fecha_hora)
                SELECT @payment, @layaway, a.instalacion_id, @shift, m.id, @operator,
                  @movement, 'PAP', 'CASH', @sequence, @applied,
                  CASE WHEN m.afecta_efectivo THEN @received ELSE NULL END,
                  CASE WHEN m.afecta_efectivo THEN @received - @applied ELSE 0 END,
                  CASE WHEN m.requiere_referencia THEN @reference ELSE NULL END, @now
                FROM ventas.apartado a CROSS JOIN catalogo.metodo_pago m
                WHERE a.id = @layaway AND m.codigo = @method;
                """, connection, transaction);
            command.Parameters.AddWithValue("payment", Guid.CreateVersion7());
            command.Parameters.AddWithValue("layaway", layawayId);
            command.Parameters.AddWithValue("shift", scenario.Shift.Id);
            command.Parameters.AddWithValue("operator", scenario.Administrator.User.Id);
            command.Parameters.AddWithValue("movement", movementId);
            command.Parameters.AddWithValue("sequence", ++sequence);
            command.Parameters.AddWithValue("applied", applied);
            command.Parameters.AddWithValue("received", NpgsqlDbType.Numeric, (object?)received ?? DBNull.Value);
            command.Parameters.AddWithValue("reference", $"LAY-{Guid.CreateVersion7():N}");
            command.Parameters.AddWithValue("now", _clock.UtcNow.UtcDateTime);
            command.Parameters.AddWithValue("method", methodCode);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        return movementId;
    }
}
