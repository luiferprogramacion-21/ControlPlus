using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class LayawayMinimumInitialPaymentMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261111010000_BusinessVatWholePesoLayawaysChangesV1";
    private const string Target = "20261112010000_LayawayMinimumInitialPaymentV1";
    private readonly LocalPostgresDatabase _postgres = new();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact, Trait("Category", "Integration")]
    public async Task Migration_DefaultsRequirementOn_PreservesHistory_AndAcceptsOnePesoSnapshot()
    {
        var connectionString = _postgres.GetConnectionString();
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(connectionString).Database);
        await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        Assert.Equal(22, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(Previous, (await db.Database.GetAppliedMigrationsAsync()).Last());

        var historical = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await SeedHistoricalLayawayAsync(connection, historical);
        var before = await LayawayFingerprintAsync(connection, historical);

        await migrator.MigrateAsync(Target);

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(23, applied.Length);
        Assert.Equal(Target, applied[^1]);
        Assert.Equal(before, await LayawayFingerprintAsync(connection, historical));
        Assert.True(await RequirementDefaultAsync(connection));
        Assert.Equal(30, await CreditDefaultDaysAsync(connection));

        var definition = await CheckDefinitionAsync(connection);
        var normalizedDefinition = definition.ToLowerInvariant();
        Assert.Contains("porcentaje_minimo_inicial >=", normalizedDefinition);
        Assert.Contains("case", normalizedDefinition);
        Assert.Contains("when", normalizedDefinition);
        Assert.Contains("then", normalizedDefinition);
        Assert.Contains("ceil", normalizedDefinition);
        var creditDefinition = await CreditCheckDefinitionAsync(connection);
        var normalizedCreditDefinition = creditDefinition.Replace("(", string.Empty)
            .Replace(")", string.Empty);
        Assert.Contains("dias_credito_predeterminado IS NULL", normalizedCreditDefinition);
        Assert.Contains("dias_credito_predeterminado >= 1", normalizedCreditDefinition);
        Assert.Contains("dias_credito_predeterminado <= 90", normalizedCreditDefinition);

        await InsertOnePesoLayawayAsync(connection);
        await SetCreditDefaultDaysAsync(connection, null);
        Assert.Null(await CreditDefaultDaysAsync(connection));
        await SetCreditDefaultDaysAsync(connection, 1);
        await SetCreditDefaultDaysAsync(connection, 90);
        await Assert.ThrowsAsync<PostgresException>(() => SetCreditDefaultDaysAsync(connection, 0));
        await Assert.ThrowsAsync<PostgresException>(() => SetCreditDefaultDaysAsync(connection, 91));
        Assert.Equal(2L, await CountLayawaysAsync(connection));
    }

    private static async Task SeedHistoricalLayawayAsync(NpgsqlConnection connection, Guid layawayId)
    {
        var actor = Guid.CreateVersion7();
        var authorizer = Guid.CreateVersion7();
        var establishment = Guid.CreateVersion7();
        var installation = Guid.CreateVersion7();
        var terminal = Guid.CreateVersion7();
        var register = Guid.CreateVersion7();
        var shift = Guid.CreateVersion7();
        var customer = Guid.CreateVersion7();
        var authorization = Guid.CreateVersion7();
        var category = Guid.CreateVersion7();
        var historicalProduct = Guid.CreateVersion7();
        var onePesoProduct = Guid.CreateVersion7();
        var historicalDetail = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand("""
            INSERT INTO seguridad.usuario
              (id,nombre_usuario,nombre_usuario_normalizado,nombre_completo,password_hash,sello_seguridad,sello_concurrencia)
            VALUES
              (@actor,'layaway-migration-actor','LAYAWAY-MIGRATION-ACTOR','Actor prueba','unused','test-a','test-a'),
              (@authorizer,'layaway-migration-authorizer','LAYAWAY-MIGRATION-AUTHORIZER','Autorizador prueba','unused','test-b','test-b');
            INSERT INTO configuracion.establecimiento(id,nombre_comercial,identificacion)
            VALUES (@establishment,'Apartados prueba','LAYAWAY-MINIMUM-MIGRATION');
            INSERT INTO configuracion.instalacion(id,establecimiento_id,codigo,serie,version_aplicacion,version_esquema)
            VALUES (@installation,@establishment,'LAYAWAY-MINIMUM','LAYMIN','test','test');
            INSERT INTO configuracion.terminal(id,instalacion_id,codigo,nombre)
            VALUES (@terminal,@installation,'LAYAWAY-MINIMUM','Terminal prueba');
            INSERT INTO caja.caja(id,instalacion_id,codigo,nombre)
            VALUES (@register,@installation,'LAYAWAY-MINIMUM','Caja prueba');
            INSERT INTO caja.turno_caja
              (id,caja_id,terminal_id,modo_operacion,usuario_apertura_id,usuario_responsable_id,
               fecha_operativa,monto_inicial)
            VALUES (@shift,@register,@terminal,'INDIVIDUAL',@actor,@actor,CURRENT_DATE,0);
            INSERT INTO catalogo.cliente(id,nombre_completo,direccion,telefono,usuario_creacion_id)
            VALUES (@customer,'Cliente prueba','Dirección prueba','3000000000',@actor);
            INSERT INTO catalogo.categoria(id,nombre,usuario_creacion_id)
            VALUES (@category,'Categoría migración apartados',@actor);
            INSERT INTO catalogo.producto
              (id,categoria_id,unidad_medida_id,codigo_interno,nombre,precio_minorista,
               stock_actual,stock_reservado,usuario_creacion_id)
            SELECT v.id,@category,u.id,v.codigo,v.nombre,v.precio,1,1,@actor
            FROM (VALUES
                (@historicalProduct,'LAYMIN-HIST','Producto migración histórico',101),
                (@onePesoProduct,'LAYMIN-UNO','Producto migración un peso',100)
              ) AS v(id,codigo,nombre,precio)
            CROSS JOIN (SELECT id FROM catalogo.unidad_medida WHERE activo ORDER BY codigo LIMIT 1) u;
            INSERT INTO seguridad.autorizacion_operacion
              (id,usuario_solicitante_id,usuario_autorizador_id,permiso_id,tipo_operacion,
               correlacion_id,descripcion_operacion,metodo_autenticacion,resultado,estado,
               fecha_solicitud,fecha_expiracion,fecha_utilizacion)
            SELECT @authorization,@actor,@authorizer,id,'CREAR_APARTADO',gen_random_uuid(),
              'Histórico de migración','PASSWORD','EXITOSA','UTILIZADA',
              clock_timestamp(),clock_timestamp() + interval '5 minutes',clock_timestamp()
            FROM seguridad.permiso WHERE codigo='LAYAWAY.MANAGE';
            INSERT INTO ventas.apartado
              (id,instalacion_id,turno_caja_id,cliente_id,usuario_id,autorizacion_operacion_id,
               prefijo,serie,consecutivo,fecha_hora,fecha_limite,porcentaje_minimo_inicial,
               abono_inicial,subtotal_bruto,total,saldo_pendiente)
            VALUES (@layaway,@installation,@shift,@customer,@actor,@authorization,
              'APT','MIG',1,clock_timestamp(),CURRENT_DATE + 15,20,21,101,101,80);
            INSERT INTO ventas.detalle_apartado
              (id,apartado_id,producto_id,codigo_producto,nombre_producto,unidad_medida,
               cantidad,precio_reserva,subtotal_bruto,subtotal_neto)
            VALUES (@historicalDetail,@layaway,@historicalProduct,'LAYMIN-HIST',
              'Producto migración histórico','UNIDAD',1,101,101,101);
            INSERT INTO ventas.pago_apartado
              (id,apartado_id,instalacion_id,turno_caja_id,metodo_pago_id,usuario_id,
               prefijo,serie,consecutivo,valor_aplicado,valor_recibido,cambio)
            SELECT @historicalPayment,@layaway,@installation,@shift,m.id,@actor,
              'PAP','MIG',1,21,21,0
            FROM catalogo.metodo_pago m WHERE m.codigo='EFECTIVO';
            """, connection);
        command.Parameters.AddWithValue("historicalPayment", Guid.CreateVersion7());
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("historicalProduct", historicalProduct);
        command.Parameters.AddWithValue("onePesoProduct", onePesoProduct);
        command.Parameters.AddWithValue("historicalDetail", historicalDetail);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("authorizer", authorizer);
        command.Parameters.AddWithValue("establishment", establishment);
        command.Parameters.AddWithValue("installation", installation);
        command.Parameters.AddWithValue("terminal", terminal);
        command.Parameters.AddWithValue("register", register);
        command.Parameters.AddWithValue("shift", shift);
        command.Parameters.AddWithValue("customer", customer);
        command.Parameters.AddWithValue("authorization", authorization);
        command.Parameters.AddWithValue("layaway", layawayId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertOnePesoLayawayAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            WITH ids AS (
              SELECT i.id installation, t.id shift, c.id customer, u.id actor,
                     (SELECT id FROM seguridad.usuario ORDER BY nombre_usuario DESC LIMIT 1) authorizer
              FROM configuracion.instalacion i
              CROSS JOIN caja.turno_caja t
              CROSS JOIN catalogo.cliente c
              CROSS JOIN seguridad.usuario u
              WHERE u.nombre_usuario='layaway-migration-actor'
              LIMIT 1
            ), auth AS (
              INSERT INTO seguridad.autorizacion_operacion
                (id,usuario_solicitante_id,usuario_autorizador_id,permiso_id,tipo_operacion,
                 correlacion_id,descripcion_operacion,metodo_autenticacion,resultado,estado,
                 fecha_solicitud,fecha_expiracion,fecha_utilizacion)
              SELECT gen_random_uuid(),ids.actor,ids.authorizer,p.id,'CREAR_APARTADO',gen_random_uuid(),
                'Opción A de migración','PASSWORD','EXITOSA','UTILIZADA',
                clock_timestamp(),clock_timestamp() + interval '5 minutes',clock_timestamp()
              FROM ids CROSS JOIN seguridad.permiso p WHERE p.codigo='LAYAWAY.MANAGE'
              RETURNING id
            )
            INSERT INTO ventas.apartado
              (id,instalacion_id,turno_caja_id,cliente_id,usuario_id,autorizacion_operacion_id,
               prefijo,serie,consecutivo,fecha_hora,fecha_limite,porcentaje_minimo_inicial,
               abono_inicial,subtotal_bruto,total,saldo_pendiente)
            SELECT @layaway,ids.installation,ids.shift,ids.customer,ids.actor,auth.id,
              'APT','MIG',2,clock_timestamp(),CURRENT_DATE + 15,0,1,100,100,99
            FROM ids CROSS JOIN auth;
            INSERT INTO ventas.detalle_apartado
              (id,apartado_id,producto_id,codigo_producto,nombre_producto,unidad_medida,
               cantidad,precio_reserva,subtotal_bruto,subtotal_neto)
            SELECT @detail,@layaway,p.id,p.codigo_interno,p.nombre,'UNIDAD',1,100,100,100
            FROM catalogo.producto p WHERE p.codigo_interno='LAYMIN-UNO';
            INSERT INTO ventas.pago_apartado
              (id,apartado_id,instalacion_id,turno_caja_id,metodo_pago_id,usuario_id,
               prefijo,serie,consecutivo,valor_aplicado,valor_recibido,cambio)
            SELECT @payment,a.id,a.instalacion_id,a.turno_caja_id,m.id,a.usuario_id,
              'PAP','MIG',2,1,1,0
            FROM ventas.apartado a CROSS JOIN catalogo.metodo_pago m
            WHERE a.id=@layaway AND m.codigo='EFECTIVO';
            """, connection);
        command.Parameters.AddWithValue("layaway", Guid.CreateVersion7());
        command.Parameters.AddWithValue("detail", Guid.CreateVersion7());
        command.Parameters.AddWithValue("payment", Guid.CreateVersion7());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> LayawayFingerprintAsync(NpgsqlConnection connection, Guid id)
    {
        await using var command = new NpgsqlCommand("""
            SELECT md5(concat_ws('|',id,instalacion_id,turno_caja_id,cliente_id,usuario_id,
              autorizacion_operacion_id,prefijo,serie,consecutivo,fecha_hora,fecha_limite,
              porcentaje_minimo_inicial,abono_inicial,subtotal_bruto,descuento_total,total,
              saldo_pendiente,estado,version))
            FROM ventas.apartado WHERE id=@id
            """, connection);
        command.Parameters.AddWithValue("id", id);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> RequirementDefaultAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT apartado_exige_minimo
            FROM configuracion.establecimiento
            WHERE identificacion='LAYAWAY-MINIMUM-MIGRATION'
            """, connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int?> CreditDefaultDaysAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT dias_credito_predeterminado
            FROM configuracion.establecimiento
            WHERE identificacion='LAYAWAY-MINIMUM-MIGRATION'
            """, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : (int)value!;
    }

    private static async Task SetCreditDefaultDaysAsync(NpgsqlConnection connection, int? days)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE configuracion.establecimiento
            SET dias_credito_predeterminado=@days
            WHERE identificacion='LAYAWAY-MINIMUM-MIGRATION'
            """, connection);
        command.Parameters.Add("days", NpgsqlDbType.Integer).Value =
            days is null ? DBNull.Value : days.Value;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> CheckDefinitionAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conrelid='ventas.apartado'::regclass AND conname='ck_apartado_montos'
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> CreditCheckDefinitionAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conrelid='configuracion.establecimiento'::regclass
              AND conname='ck_establecimiento_dias_credito_predeterminado'
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountLayawaysAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM ventas.apartado", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
