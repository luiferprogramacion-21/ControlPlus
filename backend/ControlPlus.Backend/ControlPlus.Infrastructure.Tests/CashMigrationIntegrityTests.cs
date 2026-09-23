using Npgsql;
using Xunit;
using static ControlPlus.Infrastructure.Tests.CashMigrationDatabase;

namespace ControlPlus.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class CashMigrationIntegrityTests
{
    [Theory]
    [InlineData("turno_caja_id", false)]
    [InlineData("metodo_pago_id", false)]
    [InlineData("id", false)]
    [InlineData("turno_caja_id", true)]
    [InlineData("metodo_pago_id", true)]
    [InlineData("importe", true)]
    [InlineData("eliminar", true)]
    public async Task ReconciliationDetail_RejectsIdentityChangesAndClosedMutations_WithFullRollback(
        string mutation, bool alreadyClosed)
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var first = await database.OpenShiftAsync();
        var second = await database.OpenShiftAsync(anotherRegister: true);
        if (alreadyClosed)
        {
            await using var closing = await database.Connection.BeginTransactionAsync();
            await database.ExecuteAsync(DetailSql(first) + CloseSql(first));
            await closing.CommitAsync();
        }
        var shifts = await database.RowsAsync("caja.turno_caja");
        var details = await database.RowsAsync("caja.detalle_arqueo_medio_pago");
        var users = await database.RowsAsync("seguridad.usuario");
        await using (var transaction = await database.Connection.BeginTransactionAsync())
        {
            if (!alreadyClosed) await database.ExecuteAsync(DetailSql(first));
            await database.ExecuteAsync($"UPDATE seguridad.usuario SET nombre_completo='Debe revertirse' WHERE id='{Executor}'");
            var change = mutation switch
            {
                "turno_caja_id" => $"UPDATE caja.detalle_arqueo_medio_pago SET turno_caja_id='{second}'",
                "metodo_pago_id" => "UPDATE caja.detalle_arqueo_medio_pago SET metodo_pago_id=(SELECT id FROM catalogo.metodo_pago WHERE codigo='NEQUI')",
                "id" => "UPDATE caja.detalle_arqueo_medio_pago SET id=gen_random_uuid()",
                "importe" => "UPDATE caja.detalle_arqueo_medio_pago SET valor_contado=valor_contado+1,diferencia=diferencia+1",
                _ => "DELETE FROM caja.detalle_arqueo_medio_pago"
            };
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
                change + $" WHERE turno_caja_id='{first}' AND metodo_pago_id=(SELECT id FROM catalogo.metodo_pago WHERE codigo='EFECTIVO')"));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            await transaction.RollbackAsync();
        }
        Assert.Equal(shifts, await database.RowsAsync("caja.turno_caja"));
        Assert.Equal(details, await database.RowsAsync("caja.detalle_arqueo_medio_pago"));
        Assert.Equal(users, await database.RowsAsync("seguridad.usuario"));
    }

    [Theory]
    [InlineData("EFECTIVO", true)]
    [InlineData("TRANSFERENCIA", false)]
    [InlineData("NEQUI", false)]
    [InlineData("CUARTO", false)]
    public async Task NegativeExpectedAndCountedAmounts_FollowPaymentMethodCashEffect(string method, bool rejected)
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        await database.ExecuteAsync("INSERT INTO catalogo.metodo_pago(id,codigo,nombre,afecta_efectivo) VALUES (gen_random_uuid(),'CUARTO','Cuarto medio',false)");
        var shift = await database.OpenShiftAsync();
        await using var transaction = await database.Connection.BeginTransactionAsync();
        await database.ExecuteAsync(DetailSql(shift));
        var sql = $"UPDATE caja.detalle_arqueo_medio_pago SET valor_esperado=-50,valor_contado=-50 WHERE turno_caja_id='{shift}' AND metodo_pago_id=(SELECT id FROM catalogo.metodo_pago WHERE codigo='{method}')";
        if (rejected)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(sql));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            await transaction.RollbackAsync();
            Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago"));
            Assert.Equal("ABIERTA", await database.ScalarAsync<string>("SELECT estado FROM caja.turno_caja"));
        }
        else
        {
            await database.ExecuteAsync(sql + ";" + CloseSql(shift));
            await transaction.CommitAsync();
            Assert.Equal(4L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago"));
            Assert.Equal(-50m, await database.ScalarAsync<decimal>($"SELECT valor_esperado FROM caja.detalle_arqueo_medio_pago WHERE metodo_pago_id=(SELECT id FROM catalogo.metodo_pago WHERE codigo='{method}')"));
            Assert.Equal(0m, await database.ScalarAsync<decimal>("SELECT diferencia_total FROM caja.turno_caja"));
        }
    }

    [Fact]
    public async Task CashAndElectronicDifferencesMayCompensate_WithoutAuthorization()
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        await using var transaction = await database.Connection.BeginTransactionAsync();
        await database.ExecuteAsync(DetailSql(shift, -20m, 20m) + CloseSql(shift, -20m, 20m));
        await transaction.CommitAsync();
        Assert.Equal(-20m, await database.ScalarAsync<decimal>("SELECT diferencia FROM caja.turno_caja"));
        Assert.Equal(0m, await database.ScalarAsync<decimal>("SELECT diferencia_total FROM caja.turno_caja"));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.autorizacion_cierre_turno"));
        Assert.Equal(0m, await database.ScalarAsync<decimal>("SELECT sum(diferencia) FROM caja.detalle_arqueo_medio_pago"));
    }

    [Fact]
    public async Task Commit_RejectsIncompleteReconciliationAndRestoresOpenShift()
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        var before = await database.RowsAsync("caja.turno_caja");
        await using (var transaction = await database.Connection.BeginTransactionAsync())
        {
            await database.ExecuteAsync(DetailSql(shift));
            Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago"));
            var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        }
        Assert.Equal(before, await database.RowsAsync("caja.turno_caja"));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago"));
    }

    [Fact]
    public async Task OfficialCashDifferenceConstraint_RejectsElectronicTotalInCashField()
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        await using var transaction = await database.Connection.BeginTransactionAsync();
        var sql = CloseSql(shift, 0m, 20m).Replace("diferencia=0", "diferencia=20", StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(sql));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_turno_caja_diferencia", error.ConstraintName);
        await transaction.RollbackAsync();
        Assert.Equal("ABIERTA", await database.ScalarAsync<string>("SELECT estado FROM caja.turno_caja"));
    }
}
