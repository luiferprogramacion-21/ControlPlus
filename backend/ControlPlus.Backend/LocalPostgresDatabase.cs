using Npgsql;

namespace ControlPlus.Tests;

// Integration tests own disposable databases on the local PostgreSQL service.
// The dedicated role can create databases but must not be a superuser.
internal sealed class LocalPostgresDatabase : IAsyncDisposable
{
    private const string AdminConnectionVariable = "CONTROLPLUS_TEST_ADMIN_CONNECTION";
    private const string TestRole = "controlplus_test_admin";
    private const string DatabasePrefix = "controlplus_test_";

    private readonly List<string> _ownedDatabases = [];
    private string? _adminConnectionString;
    private string? _connectionString;

    public async Task StartAsync()
    {
        if (_connectionString is not null)
            throw new InvalidOperationException("La base temporal ya fue iniciada.");

        _adminConnectionString = GetAdminConnectionString();
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await VerifyTestRoleAsync(admin);

        _connectionString = await CreateDatabaseAsync(admin);
        await using var database = new NpgsqlConnection(_connectionString);
        await database.OpenAsync();
        await using var probe = new NpgsqlCommand("SELECT 1", database);
        await probe.ExecuteScalarAsync();
    }

    public string GetConnectionString() => _connectionString ??
        throw new InvalidOperationException("La base temporal todavía no fue iniciada.");

    // Controlled migration tests need additional empty databases in the same fixture.
    public async Task<string> CreateDatabaseAsync()
    {
        if (_adminConnectionString is null)
            throw new InvalidOperationException("La base temporal todavía no fue iniciada.");

        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        return await CreateDatabaseAsync(admin);
    }

    public async ValueTask DisposeAsync()
    {
        if (_adminConnectionString is null || _ownedDatabases.Count == 0)
            return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();

        for (var index = _ownedDatabases.Count - 1; index >= 0; index--)
        {
            var databaseName = _ownedDatabases[index];
            if (!databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(databaseName[DatabasePrefix.Length..], "N", out _))
                throw new InvalidOperationException("Se rechazó eliminar una base ajena a las pruebas.");

            await DropDatabaseAsync(admin, databaseName);
            _ownedDatabases.RemoveAt(index);
        }
    }

    private static async Task DropDatabaseAsync(NpgsqlConnection admin, string databaseName)
    {
        // PostgreSQL can briefly run a worker under another role in the test
        // database. FORCE closes our sessions; retry if that worker cannot be
        // signaled by this non-superuser test role.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                await using var drop = new NpgsqlCommand(
                    $"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin)
                {
                    CommandTimeout = 120
                };
                await drop.ExecuteNonQueryAsync();
                return;
            }
            catch (PostgresException exception) when (exception.SqlState == "42501" && attempt < 39)
            {
                await Task.Delay(250);
            }
        }
    }

    private async Task<string> CreateDatabaseAsync(NpgsqlConnection admin)
    {
        var databaseName = DatabasePrefix + Guid.NewGuid().ToString("N");
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
        _ownedDatabases.Add(databaseName);

        return new NpgsqlConnectionStringBuilder(_adminConnectionString ?? throw new InvalidOperationException("Falta la conexión del rol de pruebas."))
        {
            Database = databaseName,
            Pooling = true
        }.ConnectionString;
    }

    private static string GetAdminConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(AdminConnectionVariable);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Configure {AdminConnectionVariable} para ejecutar las pruebas locales; consulte docs/testing.md.");

        var builder = new NpgsqlConnectionStringBuilder(value);
        if (builder.Host != "127.0.0.1" || builder.Port != 5432 ||
            builder.Database != "postgres" || builder.Username != TestRole)
            throw new InvalidOperationException(
                $"{AdminConnectionVariable} debe apuntar a postgres en 127.0.0.1:5432 con el rol {TestRole}.");

        builder.Pooling = false;
        return builder.ConnectionString;
    }

    private static async Task VerifyTestRoleAsync(NpgsqlConnection admin)
    {
        const string sql = """
            SELECT current_database() = 'postgres'
               AND current_user = 'controlplus_test_admin'
               AND rolcreatedb AND NOT rolsuper
               AND current_setting('server_version_num')::int BETWEEN 180000 AND 189999
            FROM pg_roles WHERE rolname = current_user
            """;
        await using var check = new NpgsqlCommand(sql, admin);
        if (await check.ExecuteScalarAsync() is not true)
            throw new InvalidOperationException(
                "Las pruebas requieren PostgreSQL 18 y el rol local controlplus_test_admin con CREATEDB, sin SUPERUSER.");
    }
}
