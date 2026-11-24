using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

/// <summary>
/// Decisiones de la auditoría del 23 November 2026 (D1, D7, D8): the operator's preflight accepts the state before the
/// migration and the postflight the state after, with the data fingerprints unchanged; afterwards the credit term reaches
/// 180 days, a refund without reference is accepted and the Administrator's discount limit is 100.
/// </summary>
public sealed class AuditDecisionsActivationTests : IAsyncLifetime
{
    private const string Previous = "20261121020000_CreditDefaultMonthsV1";
    private const string Target = "20261124010000_AuditDecisionsV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task PreflightAndPostflight_AcceptTheirStates_AndTheNewLimitsApply()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        var before = await RunAsync("Preflight.sql");
        Assert.Contains($"history|37|{Previous}", before);
        Assert.Contains("schema|PENDING", before);
        Assert.Contains("AUDIT_DECISIONS_PREFLIGHT_OK", before);
        var fingerprints = before.Where(line => line.Split('|')[0] is "establishment" or "layaway_refunds" or "change_payments" or "other_limits").ToArray();
        Assert.Equal(4, fingerprints.Length);

        await migrator.MigrateAsync(Target);
        var after = await RunAsync("Postflight.sql");
        Assert.Contains($"history|38|{Target}", after);
        Assert.Contains("schema|OK", after);
        Assert.Contains("AUDIT_DECISIONS_POSTFLIGHT_OK", after);
        Assert.All(fingerprints, line => Assert.Contains(line, after));

        // Run again on the migrated database, the preflight refuses: the activation cannot be applied twice.
        var again = await RunAsync("Preflight.sql");
        Assert.Contains("history|INVALID", again);
        Assert.Contains("schema|INVALID", again);
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
            var candidate = Path.Combine(directory.FullName, "scripts", "audit-decisions", fileName);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"No se encontró scripts/audit-decisions/{fileName}.");
    }
}
