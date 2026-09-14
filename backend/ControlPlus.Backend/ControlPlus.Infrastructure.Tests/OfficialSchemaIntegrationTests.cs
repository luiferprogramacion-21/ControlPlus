using System.Data.Common;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class OfficialSchemaIntegrationTests
{
    private static readonly (string Schema, string Table)[] OfficialVersionedTables =
    [
        ("configuracion", "establecimiento"), ("configuracion", "instalacion"),
        ("configuracion", "terminal"), ("configuracion", "motivo_operacion"),
        ("seguridad", "usuario"), ("seguridad", "rol"),
        ("seguridad", "limite_operacion_rol"), ("seguridad", "credencial_usuario"),
        ("configuracion", "consecutivo_documento"), ("configuracion", "destino_respaldo"),
        ("configuracion", "perfil_impresora"), ("configuracion", "plantilla_impresion"),
        ("catalogo", "metodo_pago"), ("catalogo", "categoria"),
        ("catalogo", "producto"), ("catalogo", "cliente"), ("catalogo", "proveedor"),
        ("caja", "caja"), ("caja", "turno_caja"),
        ("ventas", "borrador_venta"), ("ventas", "detalle_borrador_venta"),
        ("ventas", "venta"), ("ventas", "credito"), ("ventas", "apartado"),
        ("compras", "pedido_compra"), ("compras", "detalle_pedido_compra"),
        ("compras", "compra")
    ];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Migrations_CreateTheApprovedPhase4SchemaAndSecuritySeed()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("controlplus_schema_test")
            .WithUsername("controlplus_test")
            .WithPassword("integration-password-not-for-production")
            .Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using var context = new ControlPlusDbContext(options);
        await context.Database.MigrateAsync();
        await context.Database.OpenConnectionAsync();

        Assert.Equal(59L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE'
              AND table_schema IN ('seguridad','configuracion','catalogo','caja','ventas','compras','auditoria')
            """));

        Assert.Equal(59L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            SELECT count(*)
            FROM information_schema.table_constraints
            WHERE constraint_type = 'PRIMARY KEY'
              AND table_schema IN ('seguridad','configuracion','catalogo','caja','ventas','compras','auditoria')
            """));

        Assert.Equal(0L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            SELECT count(*)
            FROM pg_constraint c
            JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE c.contype = 'f'
              AND n.nspname IN ('seguridad','configuracion','catalogo','caja','ventas','compras','auditoria')
              AND c.confdeltype <> 'r'
            """));

        Assert.Equal(3L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            SELECT count(*)
            FROM seguridad.rol r
            JOIN seguridad.limite_operacion_rol l ON l.rol_id = r.id
            WHERE r.codigo IN ('ADMINISTRADOR', 'SUPERVISOR', 'CAJERO')
              AND r.es_predefinido = true
              AND r.permisos_editables = true
              AND l.codigo_operacion = 'DESCUENTO_PORCENTAJE'
              AND ((r.codigo = 'ADMINISTRADOR' AND l.valor_maximo = 80)
                OR (r.codigo = 'SUPERVISOR' AND l.valor_maximo = 20)
                OR (r.codigo = 'CAJERO' AND l.valor_maximo = 5))
            """));

        Assert.Equal(1L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema = 'seguridad' AND table_name = 'usuario_permiso'
            """));

        Assert.Equal(1L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            SELECT count(*)
            FROM information_schema.table_constraints
            WHERE table_schema = 'seguridad' AND table_name = 'usuario_permiso'
              AND constraint_type = 'CHECK'
              AND constraint_name = 'ck_usuario_permiso_efecto'
            """));

        Assert.Equal(58L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            SELECT count(*) FROM seguridad.permiso
            """));

        Assert.Equal(1L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            SELECT count(*)
            FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20260913010000_SeedMeasurementUnitsV1'
            """));

        Assert.Equal(3L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            SELECT count(*)
            FROM catalogo.unidad_medida
            WHERE codigo IN ('UNIDAD', 'PAQUETE', 'METRO') AND activo = true
            """));

        Assert.Equal(11L, await ScalarAsync(context.Database.GetDbConnection(),
            """
            WITH columnas_enteras(esquema, tabla, columna) AS (
                VALUES
                    ('catalogo', 'producto', 'stock_actual'),
                    ('catalogo', 'producto', 'stock_reservado'),
                    ('catalogo', 'producto', 'stock_minimo'),
                    ('ventas', 'detalle_venta', 'cantidad'),
                    ('compras', 'detalle_compra', 'cantidad'),
                    ('compras', 'movimiento_inventario', 'cantidad_stock'),
                    ('compras', 'movimiento_inventario', 'cantidad_reservada'),
                    ('compras', 'movimiento_inventario', 'stock_anterior'),
                    ('compras', 'movimiento_inventario', 'stock_posterior'),
                    ('compras', 'movimiento_inventario', 'reservado_anterior'),
                    ('compras', 'movimiento_inventario', 'reservado_posterior')
            )
            SELECT count(*)
            FROM columnas_enteras e
            JOIN information_schema.columns c
              ON c.table_schema = e.esquema
             AND c.table_name = e.tabla
             AND c.column_name = e.columna
            WHERE c.data_type = 'integer'
            """));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OfficialModel_UsesVersionForOptimisticConcurrencyAndIncrementsItOnce()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("controlplus_concurrency_test")
            .WithUsername("controlplus_test")
            .WithPassword("integration-password-not-for-production")
            .Build();
        await postgres.StartAsync();

        var migrationOptions = new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using (var migrationContext = new ControlPlusDbContext(migrationOptions))
        {
            await migrationContext.Database.MigrateAsync();
        }

        var options = new DbContextOptionsBuilder<OfficialControlPlusDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using var firstContext = new OfficialControlPlusDbContext(options);
        await using var secondContext = new OfficialControlPlusDbContext(options);

        var concurrencyTables = firstContext.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty("Version")?.IsConcurrencyToken == true)
            .Select(entity => (Schema: entity.GetSchema()!, Table: entity.GetTableName()!))
            .OrderBy(entity => entity.Schema)
            .ThenBy(entity => entity.Table)
            .ToArray();
        Assert.Equal(
            OfficialVersionedTables.OrderBy(entity => entity.Schema).ThenBy(entity => entity.Table).ToArray(),
            concurrencyTables);

        await using (var schemaContext = new OfficialControlPlusDbContext(options))
        {
            await schemaContext.Database.OpenConnectionAsync();
            var physicalVersionedTables = await VersionedTablesAsync(schemaContext.Database.GetDbConnection());
            Assert.Equal(
                OfficialVersionedTables.OrderBy(entity => entity.Schema).ThenBy(entity => entity.Table).ToArray(),
                physicalVersionedTables.OrderBy(entity => entity.Schema).ThenBy(entity => entity.Table).ToArray());
        }

        var firstRole = await firstContext.Rol.SingleAsync(role => role.Codigo == "SUPERVISOR");
        var secondRole = await secondContext.Rol.SingleAsync(role => role.Codigo == "SUPERVISOR");
        var originalVersion = firstRole.Version;

        firstRole.Update("Supervisor concurrencia A", firstRole.Level, DateTimeOffset.UtcNow);
        await firstContext.SaveChangesAsync();
        Assert.Equal(originalVersion + 1, firstRole.Version);

        secondRole.Update("Supervisor concurrencia B", secondRole.Level, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());

        await using var verificationContext = new OfficialControlPlusDbContext(options);
        var persistedRole = await verificationContext.Rol.SingleAsync(role => role.Codigo == "SUPERVISOR");
        Assert.Equal(originalVersion + 1, persistedRole.Version);
    }

    private static async Task<long> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyCollection<(string Schema, string Table)>> VersionedTablesAsync(
        DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_schema, table_name
            FROM information_schema.columns
            WHERE column_name = 'version'
              AND data_type = 'bigint'
              AND table_schema IN ('configuracion', 'seguridad', 'catalogo', 'caja', 'ventas', 'compras')
            """;

        var tables = new List<(string Schema, string Table)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        return tables;
    }
}
