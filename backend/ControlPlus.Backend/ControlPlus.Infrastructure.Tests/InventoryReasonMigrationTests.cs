using ControlPlus.Tests;
using System.Security.Cryptography;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class InventoryReasonMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260913020000_CashRegisterModuleV1";
    private const string InventoryMigration = "20260930010000_SeedInventoryAdjustmentReasonsV1";
    private readonly LocalPostgresDatabase _postgres = new LocalPostgresDatabase();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task UpDownUp_PreservesPreexistingIdsAndCatalog_ForMultipleEstablishments()
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var firstEstablishment = Guid.CreateVersion7();
        var secondEstablishment = Guid.CreateVersion7();
        var preexistingCountReason = Guid.CreateVersion7();
        var preexistingDamageReason = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT INTO configuracion.establecimiento
                (id, nombre_comercial, identificacion, zona_horaria, porcentaje_minimo_apartado,
                 dias_limite_cambio, modo_turno_predeterminado, rpo_minutos, rto_minutos,
                 dias_prueba_restauracion, activo, fecha_creacion, version)
            VALUES
                (@first, 'Comercio ficticio uno', 'INV-MIGRATION-1', 'America/Bogota', 20,
                 15, 'INDIVIDUAL', 30, 120, 90, true, CURRENT_TIMESTAMP, 1),
                (@second, 'Comercio ficticio dos', 'INV-MIGRATION-2', 'America/Bogota', 20,
                 15, 'INDIVIDUAL', 30, 120, 90, true, CURRENT_TIMESTAMP, 1);
            INSERT INTO configuracion.motivo_operacion
                (id, establecimiento_id, tipo_operacion, codigo, nombre, descripcion,
                 orden_visual, activo, fecha_creacion, version)
            VALUES
                (@countReason, @first, 'AJUSTE_INVENTARIO', 'CONTEO_FISICO',
                 'Nombre previo uno', 'Descripción previa uno', 91, false, CURRENT_TIMESTAMP, 7),
                (@damageReason, @second, 'AJUSTE_INVENTARIO', 'MERMA_DANO',
                 'Nombre previo dos', 'Descripción previa dos', 92, false, CURRENT_TIMESTAMP, 8),
                (gen_random_uuid(), @first, 'AJUSTE_INVENTARIO', 'AJUSTE_LOCAL_PREEXISTENTE',
                 'Motivo local', 'Debe conservarse', 99, true, CURRENT_TIMESTAMP, 3);
            """,
            new NpgsqlParameter("first", firstEstablishment),
            new NpgsqlParameter("second", secondEstablishment),
            new NpgsqlParameter("countReason", preexistingCountReason),
            new NpgsqlParameter("damageReason", preexistingDamageReason));

        await migrator.MigrateAsync(InventoryMigration);
        Assert.Equal(4L, await ScalarAsync(
            "SELECT count(*) FROM configuracion.motivo_operacion WHERE establecimiento_id=@id AND tipo_operacion='AJUSTE_INVENTARIO' AND codigo <> 'AJUSTE_LOCAL_PREEXISTENTE'",
            new NpgsqlParameter("id", firstEstablishment)));
        Assert.Equal(4L, await ScalarAsync(
            "SELECT count(*) FROM configuracion.motivo_operacion WHERE establecimiento_id=@id AND tipo_operacion='AJUSTE_INVENTARIO'",
            new NpgsqlParameter("id", secondEstablishment)));
        Assert.Equal(preexistingCountReason, await ScalarAsync<Guid>(
            "SELECT id FROM configuracion.motivo_operacion WHERE establecimiento_id=@id AND codigo='CONTEO_FISICO'",
            new NpgsqlParameter("id", firstEstablishment)));
        Assert.Equal(preexistingDamageReason, await ScalarAsync<Guid>(
            "SELECT id FROM configuracion.motivo_operacion WHERE establecimiento_id=@id AND codigo='MERMA_DANO'",
            new NpgsqlParameter("id", secondEstablishment)));

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(9L, await ScalarAsync(
            "SELECT count(*) FROM configuracion.motivo_operacion WHERE tipo_operacion='AJUSTE_INVENTARIO'"));
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM configuracion.motivo_operacion WHERE codigo='AJUSTE_LOCAL_PREEXISTENTE'"));

        await migrator.MigrateAsync(InventoryMigration);
        Assert.Equal(9L, await ScalarAsync(
            "SELECT count(*) FROM configuracion.motivo_operacion WHERE tipo_operacion='AJUSTE_INVENTARIO'"));
        Assert.Equal(preexistingCountReason, await ScalarAsync<Guid>(
            "SELECT id FROM configuracion.motivo_operacion WHERE establecimiento_id=@id AND codigo='CONTEO_FISICO'",
            new NpgsqlParameter("id", firstEstablishment)));
        Assert.Equal(InventoryMigration, (await context.Database.GetAppliedMigrationsAsync()).Last());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Down_PreservesSeededReasonsThatAreAlreadyReferenced()
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        var establishmentId = Guid.CreateVersion7();
        await ExecuteAsync(
            """
            INSERT INTO configuracion.establecimiento
                (id, nombre_comercial, identificacion, zona_horaria, porcentaje_minimo_apartado,
                 dias_limite_cambio, modo_turno_predeterminado, rpo_minutos, rto_minutos,
                 dias_prueba_restauracion, activo, fecha_creacion, version)
            VALUES (@id, 'Comercio referenciado', 'INV-REFERENCE', 'America/Bogota', 20,
                    15, 'INDIVIDUAL', 30, 120, 90, true, CURRENT_TIMESTAMP, 1)
            """, new NpgsqlParameter("id", establishmentId));
        await migrator.MigrateAsync(InventoryMigration);
        await ExecuteAsync(
            """
            CREATE TABLE public.referencia_motivo_inventario_prueba (
                id uuid PRIMARY KEY,
                motivo_id uuid NOT NULL REFERENCES configuracion.motivo_operacion(id) ON DELETE RESTRICT);
            INSERT INTO public.referencia_motivo_inventario_prueba(id, motivo_id)
            SELECT gen_random_uuid(), id
            FROM configuracion.motivo_operacion
            WHERE establecimiento_id=@id AND tipo_operacion='AJUSTE_INVENTARIO';
            """, new NpgsqlParameter("id", establishmentId));

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(4L, await ScalarAsync("SELECT count(*) FROM public.referencia_motivo_inventario_prueba"));
        Assert.Equal(4L, await ScalarAsync(
            "SELECT count(*) FROM configuracion.motivo_operacion WHERE establecimiento_id=@id AND tipo_operacion='AJUSTE_INVENTARIO'",
            new NpgsqlParameter("id", establishmentId)));

        await migrator.MigrateAsync(InventoryMigration);
        Assert.Equal(4L, await ScalarAsync("SELECT count(*) FROM public.referencia_motivo_inventario_prueba"));
        Assert.Equal(4L, await ScalarAsync(
            "SELECT count(*) FROM configuracion.motivo_operacion WHERE establecimiento_id=@id AND tipo_operacion='AJUSTE_INVENTARIO'",
            new NpgsqlParameter("id", establishmentId)));
    }

    private async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private Task<long> ScalarAsync(string sql, params NpgsqlParameter[] parameters) =>
        ScalarAsync<long>(sql, parameters);

    private async Task<T> ScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private ControlPlusDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}
