using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

/// <summary>
/// The operator's checks for the five operational-correction migrations (20 November 2026) run against a disposable
/// database: the preflight accepts the state before them, the postflight the state after, and the data fingerprints match.
/// </summary>
public sealed class OperationalCorrectionsActivationTests : IAsyncLifetime
{
    private const string Previous = "20261119020000_OptionalEstablishmentIdentificationV1";
    private const string Target = "20261120050000_AdditionalSaleLinkV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task PreflightAndPostflight_AcceptTheirStates_AndKeepTheDataFingerprints()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        var before = await RunAsync("Preflight.sql");
        Assert.Contains($"history|30|{Previous}", before);
        Assert.Contains("schema|PENDING", before);
        Assert.Contains("OPERATIONAL_CORRECTIONS_PREFLIGHT_OK", before);
        var fingerprints = before.Where(line => line.Split('|')[0] is "sales" or "payments" or "layaways" or "layaway_lines" or "cash").ToArray();
        Assert.Equal(5, fingerprints.Length);

        // Resumable: after the first migration (as happened on the store) the preflight still accepts the start.
        await migrator.MigrateAsync("20261120010000_SaleCorrectionsV1");
        var resumed = await RunAsync("Preflight.sql");
        Assert.Contains("history|31|20261120010000_SaleCorrectionsV1", resumed);
        Assert.Contains("schema|PENDING", resumed);
        Assert.All(fingerprints, line => Assert.Contains(line, resumed));

        await migrator.MigrateAsync(Target);
        var after = await RunAsync("Postflight.sql");
        Assert.Contains($"history|35|{Target}", after);
        foreach (var expected in new[] { "schema|OK", "backfill|OK", "fks|RESTRICT", "privileges|OK", "OPERATIONAL_CORRECTIONS_POSTFLIGHT_OK" })
            Assert.Contains(expected, after);
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
            var candidate = Path.Combine(directory.FullName, "scripts", "operational-corrections", fileName);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"No se encontró scripts/operational-corrections/{fileName}.");
    }
}
