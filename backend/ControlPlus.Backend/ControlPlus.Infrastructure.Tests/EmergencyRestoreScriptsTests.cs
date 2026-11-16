using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

/// <summary>
/// Los SQL de la restauración de emergencia (scripts/backups/emergency) sobre una base temporal con todas las
/// migraciones: la comprobación detecta los objetos que controlplus_app no puede usar y el archivo de permisos
/// devuelve los que las migraciones concedieron sobre objetos de postgres (copias antiguas sin permisos).
/// </summary>
public sealed class EmergencyRestoreScriptsTests : IAsyncLifetime
{
    private static readonly string[] Schemas = ["auditoria", "caja", "catalogo", "compras", "configuracion", "seguridad", "ventas"];
    private readonly LocalPostgresDatabase _postgres = new();
    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task PrivilegesScript_RestoresTheGrantsMissingFromOldCopies_AndTheCheckConfirmsFullAccess()
    {
        await using (var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
            await db.GetService<IMigrator>().MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        if (await ScalarAsync(connection, "SELECT count(*) FROM pg_roles WHERE rolname = 'controlplus_app'") is not 1L)
            return; // Sin el rol de la aplicación no hay permisos que comprobar (el CI lo crea).

        // Estado de una copia antigua restaurada: la aplicación es dueña de casi todo (aquí, con permisos completos)
        // y los objetos de postgres llegan sin los permisos que concedieron las migraciones.
        var schemas = string.Join(", ", Schemas);
        await ExecuteAsync(connection, $"""
            GRANT USAGE ON SCHEMA {schemas} TO controlplus_app;
            GRANT ALL ON ALL TABLES IN SCHEMA {schemas} TO controlplus_app;
            GRANT ALL ON ALL SEQUENCES IN SCHEMA {schemas} TO controlplus_app;
            REVOKE ALL ON TABLE catalogo.producto_codigo FROM controlplus_app;
            REVOKE ALL ON SEQUENCE catalogo.producto_referencia_seq FROM controlplus_app;
            REVOKE ALL ON configuracion.destino_respaldo, configuracion.ejecucion_respaldo,
                configuracion.copia_respaldo, configuracion.prueba_restauracion FROM controlplus_app;
            """);
        Assert.True(await CheckAsync(connection) > 0);

        var granted = await ScalarAsync(connection, await ReadScriptAsync("Restore-AppPrivileges.sql"));
        Assert.Equal("APP_PRIVILEGES_RESTORED", granted);

        Assert.Equal(0L, await CheckAsync(connection));
        Assert.Equal(true, await ScalarAsync(connection, """
            SELECT has_table_privilege('controlplus_app', 'catalogo.producto_codigo', 'SELECT')
               AND has_table_privilege('controlplus_app', 'catalogo.producto_codigo', 'INSERT')
               AND has_table_privilege('controlplus_app', 'catalogo.producto_codigo', 'DELETE')
               AND NOT has_table_privilege('controlplus_app', 'catalogo.producto_codigo', 'UPDATE')
               AND has_sequence_privilege('controlplus_app', 'catalogo.producto_referencia_seq', 'USAGE')
               AND has_table_privilege('controlplus_app', 'configuracion.copia_respaldo', 'UPDATE')
               AND NOT has_table_privilege('controlplus_app', 'configuracion.copia_respaldo', 'DELETE')
            """));

        // Es repetible: aplicarlo otra vez no falla ni cambia el resultado.
        Assert.Equal("APP_PRIVILEGES_RESTORED", await ScalarAsync(connection, await ReadScriptAsync("Restore-AppPrivileges.sql")));
        Assert.Equal(0L, await CheckAsync(connection));
    }

    private static async Task<long> CheckAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(await ReadScriptAsync("Check-RestoredDatabase.sql"), connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetInt64(0) > 0); // migraciones
        Assert.False(string.IsNullOrEmpty(reader.GetString(1))); // última migración
        return reader.GetInt64(reader.GetOrdinal("objetos_sin_acceso"));
    }

    private static async Task<string> ReadScriptAsync(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "scripts", "backups", "emergency", name);
            if (File.Exists(path)) return await File.ReadAllTextAsync(path);
        }
        throw new FileNotFoundException("No se encontró el script de restauración de emergencia.", name);
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
