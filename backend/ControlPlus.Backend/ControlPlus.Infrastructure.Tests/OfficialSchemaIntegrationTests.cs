using System.Data.Common;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class OfficialSchemaIntegrationTests
{
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
    }

    private static async Task<long> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
