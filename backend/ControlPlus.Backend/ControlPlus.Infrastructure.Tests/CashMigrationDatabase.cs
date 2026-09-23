using System.Globalization;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ControlPlus.Infrastructure.Tests;

// Every test owns a PostgreSQL 17 container; no ambient connection string is ever read.
internal sealed class CashMigrationDatabase : IAsyncDisposable
{
    public const string PreviousMigration = "20260913010000_SeedMeasurementUnitsV1";
    public const string CashMigration = "20260913020000_CashRegisterModuleV1";
    public static readonly Guid Establishment = Guid.Parse("01950000-0000-7000-8000-000000000001");
    public static readonly Guid Installation = Guid.Parse("01950000-0000-7000-8000-000000000002");
    public static readonly Guid Terminal = Guid.Parse("01950000-0000-7000-8000-000000000003");
    public static readonly Guid Register = Guid.Parse("01950000-0000-7000-8000-000000000004");
    public static readonly Guid Executor = Guid.Parse("01950000-0000-7000-8000-000000000005");
    public static readonly Guid Authorizer = Guid.Parse("01950000-0000-7000-8000-000000000006");
    public static readonly Guid OtherUser = Guid.Parse("01950000-0000-7000-8000-000000000007");
    public static readonly Guid Reason = Guid.Parse("01950000-0000-7000-8000-000000000008");
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("cash_migration_test")
        .WithUsername("cash_test")
        .WithPassword("isolated-test-password-only")
        .Build();

    public NpgsqlConnection Connection { get; private set; } = null!;

    // Keep the Testcontainers-provided connection string for secondary test connections.
    // Npgsql may omit the password when reading Connection.ConnectionString after opening it.
    public string ConnectionString => _postgres.GetConnectionString();

    public static async Task<CashMigrationDatabase> StartAsync(bool applyCash = true)
    {
        var database = new CashMigrationDatabase();
        try
        {
            await database._postgres.StartAsync();
            database.Connection = await database.OpenConnectionAsync();
            await database.MigrateAsync(applyCash ? CashMigration : PreviousMigration);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    public async Task MigrateAsync(string target)
    {
        var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var context = new ControlPlusDbContext(options);
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    public Task ExecuteAsync(string sql) => ExecuteAsync(Connection, sql);

    public async Task ExecuteTransactionAsync(params string[] statements)
    {
        await using var transaction = await Connection.BeginTransactionAsync();
        try
        {
            foreach (var statement in statements)
            {
                await using var command = new NpgsqlCommand(statement, Connection, transaction);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var command = new NpgsqlCommand(sql, Connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public Task<string> RowsAsync(string table) => ScalarAsync<string>(
        $"SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text), '[]'::jsonb)::text FROM {table} t");

    public Task<string> ColumnsAsync() => ScalarAsync<string>("""
        SELECT coalesce(jsonb_agg(definition ORDER BY definition), '[]'::jsonb)::text FROM (
          SELECT concat_ws('|',table_schema,table_name,column_name,ordinal_position::text,
              data_type,udt_name,is_nullable,column_default,numeric_precision::text,numeric_scale::text) definition
          FROM information_schema.columns
          WHERE table_schema IN ('caja','ventas','catalogo','seguridad','configuracion','public')
        ) definitions
        """);

    public Task<string> ConstraintsAsync() => ScalarAsync<string>("""
        SELECT coalesce(jsonb_agg(definition ORDER BY definition), '[]'::jsonb)::text FROM (
          SELECT n.nspname || '.' || c.conname || ':' || pg_get_constraintdef(c.oid) definition
          FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
          WHERE n.nspname IN ('caja','ventas','catalogo','seguridad','configuracion','public')
        ) definitions
        """);

    public Task<string> IndexesAsync() => ScalarAsync<string>("""
        SELECT coalesce(jsonb_agg(indexdef ORDER BY indexdef), '[]'::jsonb)::text
        FROM pg_indexes
        WHERE schemaname IN ('caja','ventas','catalogo','seguridad','configuracion','public')
        """);

    public Task<string> HistoryAsync() => RowsAsync("\"__EFMigrationsHistory\"");

    public Task<string> SchemaAsync() => ScalarAsync<string>("""
        SELECT jsonb_agg(definition ORDER BY definition)::text FROM (
          SELECT concat_ws('|',table_schema,table_name,column_name,ordinal_position::text,
              data_type,udt_name,is_nullable,column_default,numeric_precision::text,numeric_scale::text) definition
          FROM information_schema.columns WHERE table_schema IN
              ('caja','seguridad','catalogo','configuracion','ventas','compras','auditoria','public')
          UNION ALL SELECT n.nspname || '.' || c.conname || ':' || pg_get_constraintdef(c.oid)
          FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
          WHERE n.nspname IN ('caja','seguridad','catalogo','configuracion','ventas','compras','auditoria')
          UNION ALL SELECT indexdef FROM pg_indexes WHERE schemaname IN
              ('caja','seguridad','catalogo','configuracion','ventas','compras','auditoria')
          UNION ALL SELECT pg_get_triggerdef(t.oid) FROM pg_trigger t
          JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
          WHERE NOT t.tgisinternal AND n.nspname IN ('caja','seguridad','catalogo','configuracion','ventas','compras','auditoria')
          UNION ALL SELECT pg_get_functiondef(p.oid) FROM pg_proc p
          JOIN pg_namespace n ON n.oid=p.pronamespace
          WHERE n.nspname IN ('caja','seguridad','catalogo','configuracion','ventas','compras','auditoria')
        ) definitions
        """);

    public Task SeedContextAsync() => ExecuteAsync($"""
        INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
        VALUES ('{Establishment}','Negocio aislado de prueba','TEST-CASH');
        INSERT INTO configuracion.instalacion(id,establecimiento_id,codigo,serie,version_aplicacion,version_esquema)
        VALUES ('{Installation}','{Establishment}','TEST-INSTALL','TEST','test','test');
        INSERT INTO configuracion.terminal(id,instalacion_id,codigo,nombre)
        VALUES ('{Terminal}','{Installation}','TEST-TERMINAL','Terminal de prueba');
        INSERT INTO caja.caja(id,instalacion_id,codigo,nombre)
        VALUES ('{Register}','{Installation}','TEST-CASH','Caja de prueba');
        INSERT INTO seguridad.usuario(id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,
            password_hash,sello_seguridad,sello_concurrencia)
        VALUES ('{Executor}','executor','EXECUTOR','Ejecutor prueba','unused-test-fixture','test','test'),
            ('{Authorizer}','authorizer','AUTHORIZER','Autorizador prueba','unused-test-fixture','test','test'),
            ('{OtherUser}','other-user','OTHER-USER','Otro usuario prueba','unused-test-fixture','test','test');
        INSERT INTO seguridad.usuario_rol(usuario_id,rol_id)
        SELECT '{Authorizer}',id FROM seguridad.rol WHERE codigo='SUPERVISOR';
        INSERT INTO configuracion.motivo_operacion(id,establecimiento_id,tipo_operacion,codigo,nombre)
        VALUES ('{Reason}','{Establishment}','DIFERENCIA_CIERRE','TEST-DIFF','Diferencia de prueba');
        """);

    public async Task<Guid> OpenShiftAsync(bool anotherRegister = false)
    {
        var register = anotherRegister ? Guid.CreateVersion7() : Register;
        if (anotherRegister)
            await ExecuteAsync($"INSERT INTO caja.caja(id,instalacion_id,codigo,nombre) VALUES ('{register}','{Installation}','{register}','Otra caja de prueba')");
        var shift = Guid.CreateVersion7();
        await ExecuteAsync($"""
            INSERT INTO caja.turno_caja(id,caja_id,terminal_id,modo_operacion,usuario_apertura_id,
                usuario_responsable_id,fecha_operativa,monto_inicial)
            VALUES ('{shift}','{register}','{Terminal}','INDIVIDUAL','{Executor}','{Executor}',
                (CURRENT_TIMESTAMP AT TIME ZONE 'America/Bogota')::date,100);
            """);
        return shift;
    }

    public static string DetailSql(Guid shift, decimal cashDifference = 0m, decimal electronicDifference = 0m) => $"""
        INSERT INTO caja.detalle_arqueo_medio_pago(id,turno_caja_id,metodo_pago_id,valor_esperado,valor_contado,diferencia)
        SELECT gen_random_uuid(),'{shift}',id,
            CASE WHEN afecta_efectivo THEN 100 ELSE 0 END,
            CASE codigo WHEN 'EFECTIVO' THEN {Number(100m + cashDifference)}
                        WHEN 'TRANSFERENCIA' THEN {Number(electronicDifference)} ELSE 0 END,
            CASE codigo WHEN 'EFECTIVO' THEN {Number(cashDifference)}
                        WHEN 'TRANSFERENCIA' THEN {Number(electronicDifference)} ELSE 0 END
        FROM catalogo.metodo_pago WHERE activo;
        """;

    public static string CloseSql(Guid shift, decimal cashDifference = 0m, decimal electronicDifference = 0m) => $"""
        UPDATE caja.turno_caja SET estado='CERRADA',usuario_cierre_id='{Executor}',
            fecha_hora_cierre=clock_timestamp(),efectivo_esperado=100,
            efectivo_contado={Number(100m + cashDifference)},diferencia={Number(cashDifference)},
            diferencia_total={Number(cashDifference + electronicDifference)},
            motivo_diferencia_id={(cashDifference + electronicDifference == 0m ? "NULL" : $"'{Reason}'::uuid")}
        WHERE id='{shift}';
        """;

    public static string AuthorizationSql(Guid authorization, Guid correlation) => $"""
        INSERT INTO seguridad.autorizacion_operacion(id,usuario_solicitante_id,usuario_autorizador_id,
            permiso_id,tipo_operacion,correlacion_id,descripcion_operacion,metodo_autenticacion,
            resultado,estado,fecha_solicitud,fecha_expiracion)
        SELECT '{authorization}','{Executor}','{Authorizer}',id,'CIERRE_CAJA_DIFERENCIA',
            '{correlation}','Cierre aislado de prueba','PASSWORD','EXITOSA','AUTORIZADA',
            clock_timestamp() - interval '1 second',clock_timestamp() + interval '2 minutes'
        FROM seguridad.permiso WHERE codigo='CASH.SHIFT_MANAGE';
        """;

    public static string LinkSql(Guid shift, Guid authorization, Guid correlation) => $"""
        INSERT INTO caja.autorizacion_cierre_turno(turno_caja_id,autorizacion_id,correlacion_id,
            usuario_ejecutor_id,fecha_vinculacion,instalacion_id,establecimiento_id)
        VALUES ('{shift}','{authorization}','{correlation}','{Executor}',clock_timestamp(),
            '{Installation}','{Establishment}');
        """;

    private static string Number(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        if (Connection is not null) await Connection.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
