using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class CashDrawerMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261116020000_CreditInstallmentsV1";
    private const string Target = "20261119010000_CashDrawerV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_DisablesExistingProfiles_AndProtectsOneDecisionPerOperation()
    {
        var connectionString = _postgres.GetConnectionString();
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(connectionString).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(27, (await db.Database.GetAppliedMigrationsAsync()).Count());

        var establishmentId = Guid.CreateVersion7();
        var installationId = Guid.CreateVersion7();
        var terminalId = Guid.CreateVersion7();
        var profileId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var cashRegisterId = Guid.CreateVersion7();
        var shiftId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"""
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
            VALUES ('{establishmentId}','Cajon temporal','CAJON-{establishmentId.ToString("N")[..12]}');
            INSERT INTO configuracion.instalacion
              (id,establecimiento_id,codigo,serie,version_aplicacion,version_esquema)
            VALUES ('{installationId}','{establishmentId}','CAJON','001','test','test');
            INSERT INTO configuracion.terminal(id,instalacion_id,codigo,nombre)
            VALUES ('{terminalId}','{installationId}','T1','Terminal de prueba');
            INSERT INTO configuracion.perfil_impresora
              (id,terminal_id,nombre,tipo_impresora,nombre_sistema)
            VALUES ('{profileId}','{terminalId}','Recibos','RECIBOS','Cola de prueba');
            INSERT INTO seguridad.usuario
              (id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,
               password_hash,sello_seguridad,sello_concurrencia)
            VALUES ('{userId}','cajon_{userId:N}','CAJON_{userId:N}',
                    'Prueba de cajon','hash','sello','concurrencia');
            INSERT INTO caja.caja(id,instalacion_id,codigo,nombre)
            VALUES ('{cashRegisterId}','{installationId}','CAJA','Caja de prueba');
            INSERT INTO caja.turno_caja
              (id,caja_id,terminal_id,modo_operacion,usuario_apertura_id,
               usuario_responsable_id,fecha_operativa)
            VALUES ('{shiftId}','{cashRegisterId}','{terminalId}',
                    'INDIVIDUAL','{userId}','{userId}',CURRENT_DATE);
            """);

        await migrator.MigrateAsync(Target);
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(28, applied.Length);
        Assert.Equal(Target, applied[^1]);
        // The activation postflight checks these names; PostgreSQL 18 also lists NOT NULL rules in pg_constraint.
        Assert.Equal(10L, await ScalarAsync(connection, """
            SELECT count(*) FROM pg_constraint
            WHERE conrelid = 'caja.apertura_cajon'::regclass AND convalidated
              AND conname IN ('pk_apertura_cajon','fk_apertura_cajon_perfil',
                              'fk_apertura_cajon_terminal','fk_apertura_cajon_turno',
                              'fk_apertura_cajon_usuario','ck_apertura_cajon_tipo',
                              'ck_apertura_cajon_estado','ck_apertura_cajon_perfil_estado',
                              'ck_apertura_cajon_motivo_manual','uq_apertura_cajon_operacion')
            """));
        Assert.Equal(3L, await ScalarAsync(connection, """
            SELECT count(*) FROM pg_constraint
            WHERE conrelid = 'configuracion.perfil_impresora'::regclass AND convalidated
              AND conname IN ('ck_perfil_impresora_cajon_pin','ck_perfil_impresora_cajon_pulsos',
                              'ck_perfil_impresora_cajon_habilitado')
            """));
        Assert.Equal(true, await ScalarAsync(connection, """
            SELECT NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'controlplus_app')
               OR (has_table_privilege('controlplus_app','caja.apertura_cajon','SELECT')
                   AND has_table_privilege('controlplus_app','caja.apertura_cajon','INSERT')
                   AND has_table_privilege('controlplus_app','caja.apertura_cajon','UPDATE')
                   AND has_table_privilege('controlplus_app','caja.apertura_cajon','DELETE'))
            """));
        Assert.Equal("false|NULL|100|100", await ScalarAsync(connection, $"""
            SELECT cajon_habilitado::text || '|' || coalesce(cajon_pin::text,'NULL') || '|'
                   || cajon_pulso_on_ms || '|' || cajon_pulso_off_ms
            FROM configuracion.perfil_impresora WHERE id = '{profileId}'
            """));

        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => ExecuteAsync(connection, $"""
            UPDATE configuracion.perfil_impresora SET cajon_habilitado = true WHERE id = '{profileId}'
            """));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => ExecuteAsync(connection, $"""
            UPDATE configuracion.perfil_impresora SET cajon_pin = 3 WHERE id = '{profileId}'
            """));
        await ExecuteAsync(connection, $"""
            UPDATE configuracion.perfil_impresora
            SET cajon_pin = 2, cajon_habilitado = true WHERE id = '{profileId}'
            """);
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => ExecuteAsync(connection, $"""
            UPDATE configuracion.perfil_impresora SET cajon_pulso_on_ms = 21 WHERE id = '{profileId}'
            """));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => ExecuteAsync(connection, $"""
            UPDATE configuracion.perfil_impresora SET cajon_pulso_off_ms = 502 WHERE id = '{profileId}'
            """));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => ExecuteAsync(connection, $"""
            UPDATE configuracion.perfil_impresora SET tipo_impresora = 'ETIQUETAS' WHERE id = '{profileId}'
            """));

        var saleId = Guid.CreateVersion7();
        await ExecuteAsync(connection, $"""
            INSERT INTO caja.apertura_cajon
              (id,terminal_id,turno_caja_id,solicitado_por_id,tipo_operacion,operacion_id,estado)
            VALUES ('{Guid.CreateVersion7()}','{terminalId}','{shiftId}',
                    '{userId}','VENTA','{saleId}','OMITIDO')
            """);
        await AssertSqlStateAsync(PostgresErrorCodes.UniqueViolation, () => ExecuteAsync(connection, $"""
            INSERT INTO caja.apertura_cajon
              (id,terminal_id,turno_caja_id,solicitado_por_id,tipo_operacion,operacion_id,estado)
            VALUES ('{Guid.CreateVersion7()}','{terminalId}','{shiftId}',
                    '{userId}','VENTA','{saleId}','OMITIDO')
            """));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => ExecuteAsync(connection, $"""
            INSERT INTO caja.apertura_cajon
              (id,terminal_id,turno_caja_id,solicitado_por_id,tipo_operacion,operacion_id,estado)
            VALUES ('{Guid.CreateVersion7()}','{terminalId}','{shiftId}',
                    '{userId}','VENTA','{Guid.CreateVersion7()}','INTENTANDO')
            """));
        await AssertSqlStateAsync(PostgresErrorCodes.CheckViolation, () => ExecuteAsync(connection, $"""
            INSERT INTO caja.apertura_cajon
              (id,perfil_impresora_id,terminal_id,turno_caja_id,solicitado_por_id,
               tipo_operacion,operacion_id,estado)
            VALUES ('{Guid.CreateVersion7()}','{profileId}','{terminalId}','{shiftId}',
                    '{userId}','MANUAL','{Guid.CreateVersion7()}','INTENTANDO')
            """));
        await ExecuteAsync(connection, $"""
            INSERT INTO caja.apertura_cajon
              (id,perfil_impresora_id,terminal_id,turno_caja_id,solicitado_por_id,
               tipo_operacion,operacion_id,estado,motivo)
            VALUES ('{Guid.CreateVersion7()}','{profileId}','{terminalId}','{shiftId}',
                    '{userId}','MANUAL','{Guid.CreateVersion7()}','INTENTANDO','Prueba supervisada')
            """);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSqlStateAsync(string expected, Func<Task> operation)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(operation);
        Assert.Equal(expected, error.SqlState);
    }
}
