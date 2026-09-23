using Npgsql;
using Xunit;
using static ControlPlus.Infrastructure.Tests.CashMigrationDatabase;

namespace ControlPlus.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class CashMigrationAuthorizationTests
{
    [Theory]
    [InlineData(20)]
    [InlineData(-20)]
    public async Task ValidAuthorization_IsBoundAndConsumedAtomicallyForItsShift(int difference)
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        var authorization = Guid.CreateVersion7();
        var correlation = Guid.CreateVersion7();
        await using (var transaction = await database.Connection.BeginTransactionAsync())
        {
            await database.ExecuteAsync(DetailSql(shift, difference) + AuthorizationSql(authorization, correlation)
                + LinkSql(shift, authorization, correlation) + CloseSql(shift, difference));
            await transaction.CommitAsync();
        }
        Assert.Equal(difference, await database.ScalarAsync<decimal>("SELECT diferencia FROM caja.turno_caja"));
        Assert.Equal(difference, await database.ScalarAsync<decimal>("SELECT diferencia_total FROM caja.turno_caja"));
        Assert.Equal(1L, await database.ScalarAsync<long>($"""
            SELECT count(*) FROM caja.autorizacion_cierre_turno l
            JOIN seguridad.autorizacion_operacion a ON a.id=l.autorizacion_id
            JOIN caja.turno_caja t ON t.id=l.turno_caja_id
            WHERE l.turno_caja_id='{shift}' AND l.autorizacion_id='{authorization}'
              AND l.correlacion_id='{correlation}' AND a.correlacion_id=l.correlacion_id
              AND l.usuario_ejecutor_id=t.usuario_cierre_id AND a.usuario_solicitante_id=t.usuario_cierre_id
              AND l.instalacion_id='{Installation}' AND l.establecimiento_id='{Establishment}'
              AND a.estado='UTILIZADA' AND a.fecha_utilizacion=l.fecha_vinculacion
              AND a.fecha_expiracion>l.fecha_vinculacion AND t.estado='CERRADA'
            """));
    }

    [Theory]
    [InlineData("correlacion")]
    [InlineData("vencida")]
    [InlineData("tipo")]
    [InlineData("solicitante")]
    [InlineData("consumida")]
    [InlineData("mismo_autorizador")]
    [InlineData("pendiente")]
    [InlineData("permiso")]
    [InlineData("autorizador_inactivo")]
    [InlineData("permiso_revocado")]
    [InlineData("instalacion")]
    [InlineData("establecimiento")]
    public async Task InvalidAuthorization_RejectsBindingAndRollsBackEveryAttemptedChange(string invalidity)
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        var authorization = Guid.CreateVersion7();
        var correlation = Guid.CreateVersion7();
        await database.ExecuteAsync(AuthorizationSql(authorization, correlation));
        var alter = invalidity switch
        {
            "vencida" => "fecha_solicitud=clock_timestamp()-interval '2 minutes',fecha_expiracion=clock_timestamp()-interval '1 minute'",
            "tipo" => "tipo_operacion='OTRA_OPERACION'",
            "solicitante" => $"usuario_solicitante_id='{OtherUser}'",
            "consumida" => "estado='UTILIZADA',fecha_utilizacion=clock_timestamp()",
            "mismo_autorizador" => $"usuario_autorizador_id='{Executor}'",
            "pendiente" => "estado='PENDIENTE',usuario_autorizador_id=NULL,metodo_autenticacion=NULL,resultado=NULL",
            "permiso" => "permiso_id=(SELECT id FROM seguridad.permiso WHERE codigo='CASH.OWN_READ')",
            _ => null
        };
        if (alter is not null)
            await database.ExecuteAsync($"UPDATE seguridad.autorizacion_operacion SET {alter} WHERE id='{authorization}'");
        if (invalidity == "autorizador_inactivo")
            await database.ExecuteAsync($"UPDATE seguridad.usuario SET activo=false WHERE id='{Authorizer}'");
        if (invalidity == "permiso_revocado")
            await database.ExecuteAsync($"""
                INSERT INTO seguridad.usuario_permiso(usuario_id,permiso_id,efecto,asignado_por_id)
                SELECT '{Authorizer}',id,'REVOCAR','{Executor}' FROM seguridad.permiso WHERE codigo='CASH.SHIFT_MANAGE';
                """);
        var authorizations = await database.RowsAsync("seguridad.autorizacion_operacion");
        var shifts = await database.RowsAsync("caja.turno_caja");
        var users = await database.RowsAsync("seguridad.usuario");
        var link = LinkSql(shift, authorization, invalidity == "correlacion" ? Guid.CreateVersion7() : correlation);
        if (invalidity == "instalacion") link = link.Replace(Installation.ToString(), Guid.CreateVersion7().ToString(), StringComparison.Ordinal);
        if (invalidity == "establecimiento") link = link.Replace(Establishment.ToString(), Guid.CreateVersion7().ToString(), StringComparison.Ordinal);
        await using (var transaction = await database.Connection.BeginTransactionAsync())
        {
            await database.ExecuteAsync(DetailSql(shift, 20m)
                + $"UPDATE seguridad.usuario SET nombre_completo='Debe revertirse' WHERE id='{Executor}';");
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(link));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            await transaction.RollbackAsync();
        }
        Assert.Equal(authorizations, await database.RowsAsync("seguridad.autorizacion_operacion"));
        Assert.Equal(shifts, await database.RowsAsync("caja.turno_caja"));
        Assert.Equal(users, await database.RowsAsync("seguridad.usuario"));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago"));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.autorizacion_cierre_turno"));
    }

    [Theory]
    [InlineData("autorizacion_correlacion")]
    [InlineData("autorizacion_estado")]
    [InlineData("autorizacion_solicitante")]
    [InlineData("autorizacion_autorizador")]
    [InlineData("autorizacion_expiracion")]
    [InlineData("autorizacion_tipo")]
    [InlineData("autorizacion_eliminar")]
    [InlineData("vinculo_turno")]
    [InlineData("vinculo_autorizacion")]
    [InlineData("vinculo_correlacion")]
    [InlineData("vinculo_contexto")]
    [InlineData("vinculo_eliminar")]
    public async Task ConsumedAuthorizationAndBinding_AreImmutableAfterClose(string mutation)
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        var authorization = Guid.CreateVersion7();
        var correlation = Guid.CreateVersion7();
        await using (var closing = await database.Connection.BeginTransactionAsync())
        {
            await database.ExecuteAsync(DetailSql(shift, 20m) + AuthorizationSql(authorization, correlation)
                + LinkSql(shift, authorization, correlation) + CloseSql(shift, 20m));
            await closing.CommitAsync();
        }
        var originalAuthorization = await database.RowsAsync("seguridad.autorizacion_operacion");
        var originalLink = await database.RowsAsync("caja.autorizacion_cierre_turno");
        var originalShift = await database.RowsAsync("caja.turno_caja");
        var sql = mutation switch
        {
            "autorizacion_correlacion" => "UPDATE seguridad.autorizacion_operacion SET correlacion_id=gen_random_uuid()",
            "autorizacion_estado" => "UPDATE seguridad.autorizacion_operacion SET estado='AUTORIZADA',fecha_utilizacion=NULL",
            "autorizacion_solicitante" => $"UPDATE seguridad.autorizacion_operacion SET usuario_solicitante_id='{OtherUser}'",
            "autorizacion_autorizador" => $"UPDATE seguridad.autorizacion_operacion SET usuario_autorizador_id='{Executor}'",
            "autorizacion_expiracion" => "UPDATE seguridad.autorizacion_operacion SET fecha_expiracion=fecha_expiracion+interval '1 day'",
            "autorizacion_tipo" => "UPDATE seguridad.autorizacion_operacion SET tipo_operacion='OTRO_TIPO'",
            "autorizacion_eliminar" => "DELETE FROM seguridad.autorizacion_operacion",
            "vinculo_turno" => "UPDATE caja.autorizacion_cierre_turno SET turno_caja_id=gen_random_uuid()",
            "vinculo_autorizacion" => "UPDATE caja.autorizacion_cierre_turno SET autorizacion_id=gen_random_uuid()",
            "vinculo_correlacion" => "UPDATE caja.autorizacion_cierre_turno SET correlacion_id=gen_random_uuid()",
            "vinculo_contexto" => "UPDATE caja.autorizacion_cierre_turno SET instalacion_id=gen_random_uuid()",
            _ => "DELETE FROM caja.autorizacion_cierre_turno"
        };
        await using (var transaction = await database.Connection.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(sql));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            await transaction.RollbackAsync();
        }
        Assert.Equal(originalAuthorization, await database.RowsAsync("seguridad.autorizacion_operacion"));
        Assert.Equal(originalLink, await database.RowsAsync("caja.autorizacion_cierre_turno"));
        Assert.Equal(originalShift, await database.RowsAsync("caja.turno_caja"));
    }

    [Fact]
    public async Task ReuseForAnotherShift_IsRejectedAndPreservesFirstClose()
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var first = await database.OpenShiftAsync();
        var authorization = Guid.CreateVersion7();
        var correlation = Guid.CreateVersion7();
        await using (var transaction = await database.Connection.BeginTransactionAsync())
        {
            await database.ExecuteAsync(DetailSql(first, 20m) + AuthorizationSql(authorization, correlation)
                + LinkSql(first, authorization, correlation) + CloseSql(first, 20m));
            await transaction.CommitAsync();
        }
        var second = await database.OpenShiftAsync();
        var before = await database.RowsAsync("caja.turno_caja");
        var links = await database.RowsAsync("caja.autorizacion_cierre_turno");
        await using (var transaction = await database.Connection.BeginTransactionAsync())
        {
            await database.ExecuteAsync(DetailSql(second, 20m));
            var error = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(LinkSql(second, authorization, correlation)));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            await transaction.RollbackAsync();
        }
        Assert.Equal(before, await database.RowsAsync("caja.turno_caja"));
        Assert.Equal(links, await database.RowsAsync("caja.autorizacion_cierre_turno"));
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago"));
        Assert.Equal(0L, await database.ScalarAsync<long>($"SELECT count(*) FROM caja.detalle_arqueo_medio_pago WHERE turno_caja_id='{second}'"));
    }

    [Fact]
    public async Task CommitFailure_RollsBackAuthorizationBindingConsumptionDetailsAndClosure()
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var shift = await database.OpenShiftAsync();
        var authorization = Guid.CreateVersion7();
        var correlation = Guid.CreateVersion7();
        var before = await database.RowsAsync("caja.turno_caja");
        await using (var transaction = await database.Connection.BeginTransactionAsync())
        {
            // Each write succeeds. The deferred totals constraint must fail specifically at COMMIT.
            await database.ExecuteAsync(DetailSql(shift, 20m) + AuthorizationSql(authorization, correlation)
                + LinkSql(shift, authorization, correlation)
                + CloseSql(shift, 20m).Replace("diferencia_total=20", "diferencia_total=21", StringComparison.Ordinal));
            Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.autorizacion_cierre_turno"));
            Assert.Equal("UTILIZADA", await database.ScalarAsync<string>("SELECT estado FROM seguridad.autorizacion_operacion"));
            Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago"));
            Assert.Equal("CERRADA", await database.ScalarAsync<string>("SELECT estado FROM caja.turno_caja"));
            var error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        }
        Assert.Equal(before, await database.RowsAsync("caja.turno_caja"));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM seguridad.autorizacion_operacion"));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.autorizacion_cierre_turno"));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago"));
    }

    [Fact]
    public async Task ConcurrentCloses_CannotConsumeTheSameAuthorizationTwice()
    {
        await using var database = await StartAsync();
        await database.SeedContextAsync();
        var first = await database.OpenShiftAsync();
        var second = await database.OpenShiftAsync(anotherRegister: true);
        var authorization = Guid.CreateVersion7();
        var correlation = Guid.CreateVersion7();
        await database.ExecuteAsync(AuthorizationSql(authorization, correlation));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var participants = 0;

        async Task<bool> TryCloseAsync(Guid shift)
        {
            await using var connection = await database.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, DetailSql(shift, 20m));
            if (Interlocked.Increment(ref participants) == 2) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            try
            {
                await ExecuteAsync(connection, LinkSql(shift, authorization, correlation) + CloseSql(shift, 20m));
                await transaction.CommitAsync();
                return true;
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.RaiseException)
            {
                await transaction.RollbackAsync();
                return false;
            }
        }

        var results = await Task.WhenAll(TryCloseAsync(first), TryCloseAsync(second));
        Assert.Single(results, succeeded => succeeded);
        Assert.Single(results, succeeded => !succeeded);
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.turno_caja WHERE estado='CERRADA'"));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.turno_caja WHERE estado='ABIERTA'"));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.autorizacion_cierre_turno"));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT count(*) FROM seguridad.autorizacion_operacion WHERE estado='UTILIZADA'"));
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago"));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT count(*) FROM caja.detalle_arqueo_medio_pago d JOIN caja.turno_caja t ON t.id=d.turno_caja_id WHERE t.estado='ABIERTA'"));
    }
}
