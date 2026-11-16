using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class CreditRiskControlsMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261115020000_ConfigurableWholesaleMinimumV1";
    private const string Target = "20261116010000_CreditRiskControlsV1";
    private const string Identification = "CREDIT-RISK-MIGRATION";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_StartsInWarningMode_AddsAnOptionalPositiveLimit_AndReverts()
    {
        var connectionString = _postgres.GetConnectionString();
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(connectionString).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(25, (await db.Database.GetAppliedMigrationsAsync()).Count());

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"""
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
            VALUES ('{Guid.CreateVersion7()}','Riesgo prueba','{Identification}');
            """);

        await migrator.MigrateAsync(Target);
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(26, applied.Length);
        Assert.Equal(Target, applied[^1]);

        // Un establecimiento existente queda en modo aviso (sin bloqueo).
        Assert.Equal(false, await ScalarAsync(connection, $"""
            SELECT credito_bloquear_riesgo FROM configuracion.establecimiento WHERE identificacion = '{Identification}'
            """));
        Assert.Equal("NO", await ScalarAsync(connection, """
            SELECT is_nullable FROM information_schema.columns
            WHERE table_schema = 'configuracion' AND table_name = 'establecimiento' AND column_name = 'credito_bloquear_riesgo'
            """));

        // El cupo es opcional, numérico entero y positivo.
        Assert.Equal("YES|numeric|18|0", await ScalarAsync(connection, """
            SELECT is_nullable || '|' || data_type || '|' || numeric_precision || '|' || numeric_scale
            FROM information_schema.columns
            WHERE table_schema = 'catalogo' AND table_name = 'cliente' AND column_name = 'cupo_credito'
            """));
        var check = (string)(await ScalarAsync(connection, """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'ck_cliente_cupo_credito'
            """))!;
        Assert.Contains("cupo_credito IS NULL", check);
        Assert.Contains("cupo_credito > (0)", check.Replace("(0)::numeric", "(0)"));

        await migrator.MigrateAsync(Previous);
        Assert.Equal(0L, await ScalarAsync(connection, """
            SELECT count(*) FROM information_schema.columns
            WHERE (table_name = 'cliente' AND column_name = 'cupo_credito')
               OR (table_name = 'establecimiento' AND column_name = 'credito_bloquear_riesgo')
            """));
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
}
