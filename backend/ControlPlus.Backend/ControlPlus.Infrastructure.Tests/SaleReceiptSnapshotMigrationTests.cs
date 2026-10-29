using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class SaleReceiptSnapshotMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261026010000_InitialProductInventoryMovementV1";
    private const string Target = "20261028010000_SaleReceiptDataSnapshotV1";
    private readonly LocalPostgresDatabase _postgres = new();
    private readonly Guid _user = Guid.CreateVersion7();
    private readonly Guid _installation = Guid.CreateVersion7();
    private readonly Guid _terminal = Guid.CreateVersion7();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task UpDownUp_PreservesExistingDocumentsAndRejectsLossyRollback()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await SeedContextAsync();
        var legacyId = Guid.CreateVersion7();
        var legacyOrigin = Guid.CreateVersion7();
        await InsertDocumentAsync(legacyId, legacyOrigin, withTemplate: true);
        var documentsBefore = await DocumentsAsync();
        var schemaBefore = await ConstraintsAsync();
        var historyBefore = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.False(await TemplateNullableAsync());

        await migrator.MigrateAsync(Target);
        Assert.True(await TemplateNullableAsync());
        Assert.Equal(documentsBefore, await DocumentsAsync());
        Assert.Equal(schemaBefore, await ConstraintsAsync());
        Assert.Equal(historyBefore.Append(Target), await db.Database.GetAppliedMigrationsAsync());
        await using (var official = new OfficialControlPlusDbContext(
                         new DbContextOptionsBuilder<OfficialControlPlusDbContext>()
                             .UseNpgsql(_postgres.GetConnectionString()).Options))
            Assert.True(official.Model.FindEntityType(typeof(DocumentoEmitido))!
                .FindProperty(nameof(DocumentoEmitido.PlantillaSnapshot))!.IsNullable);

        // Reversal is valid while all historical documents still have a real template snapshot.
        await migrator.MigrateAsync(Previous);
        Assert.False(await TemplateNullableAsync());
        Assert.Equal(documentsBefore, await DocumentsAsync());
        Assert.Equal(schemaBefore, await ConstraintsAsync());
        Assert.Equal(historyBefore, await db.Database.GetAppliedMigrationsAsync());

        await migrator.MigrateAsync(Target);
        await InsertDocumentAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), withTemplate: false);
        var documentsWithDataOnly = await DocumentsAsync();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertDocumentAsync(Guid.CreateVersion7(), legacyOrigin, withTemplate: true));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var foreignKey = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertDocumentAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), withTemplate: false,
                user: Guid.CreateVersion7()));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreignKey.SqlState);
        var check = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertDocumentAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), withTemplate: false,
                documentType: "TIPO_INVALIDO"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, check.SqlState);
        foreach (var sql in new[]
                 {
                     "UPDATE configuracion.documento_emitido SET hash_snapshot = 'changed' WHERE id = @id",
                     "DELETE FROM configuracion.documento_emitido WHERE id = @id"
                 })
        {
            var immutable = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql, legacyId));
            Assert.Equal("P0001", immutable.SqlState);
        }

        // SET NOT NULL must fail instead of changing or deleting any data-only document.
        var rollback = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, rollback.SqlState);
        Assert.True(await TemplateNullableAsync());
        Assert.Equal(documentsWithDataOnly, await DocumentsAsync());
        Assert.Equal(schemaBefore, await ConstraintsAsync());
        Assert.Equal(historyBefore.Append(Target), await db.Database.GetAppliedMigrationsAsync());
    }

    private async Task SeedContextAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO seguridad.usuario
                (id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,password_hash,sello_seguridad,sello_concurrencia)
            VALUES (@user,'snapshot-migration','SNAPSHOT-MIGRATION','Migration fixture','unused','test','test');
            INSERT INTO configuracion.establecimiento (id,nombre_comercial,identificacion)
            VALUES (@establishment,'Snapshot migration fixture','SNAPSHOT-MIGRATION');
            INSERT INTO configuracion.instalacion
                (id,establecimiento_id,codigo,serie,version_aplicacion,version_esquema)
            VALUES (@installation,@establishment,'SNAPSHOT-MIGRATION','TEST','test','test');
            INSERT INTO configuracion.terminal (id,instalacion_id,codigo,nombre)
            VALUES (@terminal,@installation,'TEST','Migration fixture');
            """, connection);
        command.Parameters.AddWithValue("user", _user);
        command.Parameters.AddWithValue("establishment", Guid.CreateVersion7());
        command.Parameters.AddWithValue("installation", _installation);
        command.Parameters.AddWithValue("terminal", _terminal);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertDocumentAsync(Guid id, Guid origin, bool withTemplate,
        Guid? user = null, string documentType = "COMPROBANTE_VENTA")
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO configuracion.documento_emitido
                (id,instalacion_id,terminal_id,emitido_por_id,tipo_documento,entidad_tipo,entidad_id,
                 numero_visible,version_documento,datos_snapshot,plantilla_snapshot,hash_snapshot)
            VALUES (@id,@installation,@terminal,@user,@type,'Venta',@origin,'TEST-1',1,
                    '{"historical":"migration fixture","value":123.00}'::jsonb,@template,'fixture-hash');
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("installation", _installation);
        command.Parameters.AddWithValue("terminal", _terminal);
        command.Parameters.AddWithValue("user", user ?? _user);
        command.Parameters.AddWithValue("type", documentType);
        command.Parameters.AddWithValue("origin", origin);
        // Only this isolated fixture models a document predating the migration with a template.
        command.Parameters.Add("template", NpgsqlDbType.Jsonb).Value = withTemplate
            ? "{\"templateVersion\":7,\"content\":\"historical migration fixture\"}"
            : DBNull.Value;
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private Task<bool> TemplateNullableAsync() => ScalarAsync<bool>("""
        SELECT NOT attnotnull FROM pg_attribute
        WHERE attrelid = 'configuracion.documento_emitido'::regclass
          AND attname = 'plantilla_snapshot' AND NOT attisdropped
        """);

    private Task<string> DocumentsAsync() => ScalarAsync<string>("""
        SELECT jsonb_agg(to_jsonb(d) ORDER BY d.id)::text FROM configuracion.documento_emitido d
        """);

    private Task<string> ConstraintsAsync() => ScalarAsync<string>("""
        SELECT jsonb_build_object(
            'constraints', (SELECT jsonb_agg(jsonb_build_array(conname,pg_get_constraintdef(oid)) ORDER BY conname)
                FROM pg_constraint WHERE conrelid = 'configuracion.documento_emitido'::regclass AND contype <> 'n'),
            'indexes', (SELECT jsonb_agg(jsonb_build_array(indexname,indexdef) ORDER BY indexname)
                FROM pg_indexes WHERE schemaname = 'configuracion' AND tablename = 'documento_emitido'),
            'triggers', (SELECT jsonb_agg(pg_get_triggerdef(oid) ORDER BY tgname)
                FROM pg_trigger WHERE tgrelid = 'configuracion.documento_emitido'::regclass AND NOT tgisinternal)
        )::text
        """);

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
