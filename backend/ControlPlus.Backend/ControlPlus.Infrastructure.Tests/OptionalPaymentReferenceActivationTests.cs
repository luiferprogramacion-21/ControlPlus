using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

/// <summary>
/// Referencia opcional en abonos de crédito y pagos de apartado (21 November 2026): the operator's preflight accepts the
/// state before the migration and the postflight the state after; after it the shared function accepts a missing
/// reference and still rejects a blank one.
/// </summary>
public sealed class OptionalPaymentReferenceActivationTests : IAsyncLifetime
{
    private const string Previous = "20261120050000_AdditionalSaleLinkV1";
    private const string Target = "20261121010000_OptionalCreditLayawayPaymentReferenceV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task PreflightAndPostflight_AcceptTheirStates_AndTheFunctionAcceptsAMissingReference()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        var before = await RunAsync("Preflight.sql");
        Assert.Contains($"history|35|{Previous}", before);
        Assert.Contains("function|PENDING", before);
        Assert.Contains("OPTIONAL_PAYMENT_REFERENCE_PREFLIGHT_OK", before);
        var fingerprints = before.Where(line => line.Split('|')[0] is "credit_payments" or "layaway_payments").ToArray();
        Assert.Equal(2, fingerprints.Length);
        Assert.Equal("ERROR", await CheckReferenceAsync(null));

        await migrator.MigrateAsync(Target);
        var after = await RunAsync("Postflight.sql");
        Assert.Contains($"history|36|{Target}", after);
        Assert.Contains("function|OK", after);
        Assert.Contains("OPTIONAL_PAYMENT_REFERENCE_POSTFLIGHT_OK", after);
        Assert.All(fingerprints, line => Assert.Contains(line, after));
        Assert.Equal("OK", await CheckReferenceAsync(null));
        Assert.Equal("ERROR", await CheckReferenceAsync(" "));

        // Run again on the migrated database, the preflight refuses: the activation cannot be applied twice.
        var again = await RunAsync("Preflight.sql");
        Assert.Contains("history|INVALID", again);
        Assert.Contains("function|INVALID", again);
    }

    /// <summary>
    /// Calls the shared validator through a temporary table with the same trigger, inside a transaction that is rolled
    /// back: a Nequi payment with the given reference either passes ("OK") or is rejected ("ERROR").
    /// </summary>
    private async Task<string> CheckReferenceAsync(string? reference)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var create = new NpgsqlCommand("""
            CREATE TEMP TABLE pago_prueba (metodo_pago_id uuid, valor_recibido numeric, cambio numeric NOT NULL DEFAULT 0,
                                           referencia varchar(200)) ON COMMIT DROP;
            CREATE TRIGGER tg_pago_prueba BEFORE INSERT ON pago_prueba
                FOR EACH ROW EXECUTE FUNCTION ventas.validar_datos_cobro();
            """, connection, transaction))
            await create.ExecuteNonQueryAsync();
        await using var insert = new NpgsqlCommand("""
            INSERT INTO pago_prueba (metodo_pago_id, referencia)
            SELECT id, @reference FROM catalogo.metodo_pago WHERE codigo = 'NEQUI'
            """, connection, transaction);
        insert.Parameters.AddWithValue("reference", (object?)reference ?? DBNull.Value);
        try
        {
            return await insert.ExecuteNonQueryAsync() == 1 ? "OK" : "MISSING";
        }
        catch (PostgresException)
        {
            return "ERROR";
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    /// <summary>Runs a psql script through Npgsql (without its meta-commands) and returns the first column of every row.</summary>
    private async Task<List<string>> RunAsync(string fileName)
    {
        var sql = string.Join('\n', (await File.ReadAllLinesAsync(ScriptPath(fileName)))
            .Where(line => !line.TrimStart().StartsWith('\\')));
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        do { while (await reader.ReadAsync()) if (!reader.IsDBNull(0)) lines.Add(reader.GetValue(0).ToString()!); }
        while (await reader.NextResultAsync());
        return lines;
    }

    private static string ScriptPath(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "scripts", "optional-payment-reference", fileName);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"No se encontró scripts/optional-payment-reference/{fileName}.");
    }
}
