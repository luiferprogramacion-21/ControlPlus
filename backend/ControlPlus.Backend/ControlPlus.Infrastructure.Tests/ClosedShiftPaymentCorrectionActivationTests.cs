using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

/// <summary>
/// Decisión D3 de la auditoría del 23 de noviembre de 2026: el preflight del operador acepta el estado anterior, el
/// postflight el posterior con las huellas de pagos, movimientos y turnos intactas, y el Down devuelve la función oficial.
/// </summary>
public sealed class ClosedShiftPaymentCorrectionActivationTests : IAsyncLifetime
{
    private const string Previous = "20261124010000_AuditDecisionsV1";
    private const string Target = "20261124020000_ClosedShiftPaymentCorrectionV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task PreflightAndPostflight_AcceptTheirStates_AndDownRestoresTheOfficialFunction()
    {
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(Previous);
        var before = await RunAsync("Preflight.sql");
        Assert.Contains($"history|38|{Previous}", before);
        Assert.Contains("schema|PENDING", before);
        Assert.Contains("CLOSED_SHIFT_PAYMENT_CORRECTION_PREFLIGHT_OK", before);
        var fingerprints = before.Where(line => line.Split('|')[0] is "sale_payments" or "cash_movements" or "shifts").ToArray();
        Assert.Equal(3, fingerprints.Length);

        await migrator.MigrateAsync(Target);
        var after = await RunAsync("Postflight.sql");
        Assert.Contains($"history|39|{Target}", after);
        Assert.Contains("schema|OK", after);
        Assert.Contains("CLOSED_SHIFT_PAYMENT_CORRECTION_POSTFLIGHT_OK", after);
        Assert.All(fingerprints, line => Assert.Contains(line, after));

        // Aplicada, el preflight la rechaza: la activación no se repite.
        var again = await RunAsync("Preflight.sql");
        Assert.Contains("history|INVALID", again);
        Assert.Contains("schema|INVALID", again);

        // El Down vuelve a la regla oficial: el preflight reconoce otra vez el estado anterior.
        await migrator.MigrateAsync(Previous);
        Assert.Contains("schema|PENDING", await RunAsync("Preflight.sql"));
    }

    /// <summary>Corre un script de psql por Npgsql (sin sus metacomandos) y devuelve la primera columna de cada fila.</summary>
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
            var candidate = Path.Combine(directory.FullName, "scripts", "closed-shift-payment-correction", fileName);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"No se encontró scripts/closed-shift-payment-correction/{fileName}.");
    }
}
