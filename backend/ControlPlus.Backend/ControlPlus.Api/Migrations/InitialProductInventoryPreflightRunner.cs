using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ControlPlus.Api.Migrations;

internal static partial class InitialProductInventoryPreflightRunner
{
    public const string TargetMigration = ControlledMigrationRunner.InitialProductInventoryMigration;
    private static readonly string[] PreviousMigrations =
    [
        "20260912010000_OfficialPhase4Baseline",
        "20260912011000_SeedApprovedSecurityCatalog",
        "20260912012000_HybridPermissionsV1",
        "20260913010000_SeedMeasurementUnitsV1",
        "20260913020000_CashRegisterModuleV1",
        "20260930010000_SeedInventoryAdjustmentReasonsV1",
        "20261005010000_CustomerSearchIndexesV1",
        "20261014010000_InventoryMovementRegistrationOrderV1",
        "20261020010000_LayawayDeliveryWithoutSaleV1",
        "20261021010000_LayawayCancellationOptionalReasonV1"
    ];
    private const string OfficialExpression = "tipo_movimiento=ANYARRAY['COMPRA','ANULACION_COMPRA','VENTA'," +
        "'ANULACION_VENTA','RESERVA_APARTADO','LIBERACION_APARTADO','ENTREGA_APARTADO'," +
        "'CAMBIO_DEVOLUCION','CAMBIO_ENTREGA','AJUSTE_ENTRADA','AJUSTE_SALIDA','REVERSO']";

    public static async Task<ControlledPreflightResult> RunAsync(
        IConfiguration configuration, string targetMigration, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("ControlPlusDb");
        if (string.IsNullOrWhiteSpace(connectionString)) return Rejected(targetMigration, "the database configuration is unavailable");
        try
        {
            var options = new DbContextOptionsBuilder<ControlPlusDbContext>().UseNpgsql(connectionString).Options;
            await using var context = new ControlPlusDbContext(options);
            var expected = PreviousMigrations.Append(TargetMigration).ToArray();
            if (!context.Database.GetMigrations().Take(expected.Length).SequenceEqual(expected, StringComparer.Ordinal))
                return Rejected(targetMigration, "the EF migration catalog does not have the exact required prefix");

            await context.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var pgTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            await CustomerSearchDatabaseValidation.SetTransactionReadOnlyAsync(connection, pgTransaction, cancellationToken);
            if (!await CustomerSearchDatabaseValidation.IsTransactionReadOnlyAsync(connection, pgTransaction, cancellationToken))
                return await RollbackRejectedAsync(transaction, targetMigration, "PostgreSQL did not confirm a read-only transaction", cancellationToken);

            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            if (!applied.SequenceEqual(PreviousMigrations, StringComparer.Ordinal))
                return await RollbackRejectedAsync(transaction, targetMigration, "the EF history must contain exactly the ten required predecessor migrations", cancellationToken);
            if (!await HasOfficialMovementTypeConstraintAsync(connection, pgTransaction, cancellationToken))
                return await RollbackRejectedAsync(transaction, targetMigration, "the official inventory movement type constraint is absent or incompatible", cancellationToken);
            if (await HasReservedObjectsAsync(connection, pgTransaction, cancellationToken))
                return await RollbackRejectedAsync(transaction, targetMigration, "an object reserved for the target already exists", cancellationToken);
            if (!await CustomerSearchDatabaseValidation.IsTransactionReadOnlyAsync(connection, pgTransaction, cancellationToken))
                return await RollbackRejectedAsync(transaction, targetMigration, "the read-only transaction context was not preserved", cancellationToken);

            await transaction.RollbackAsync(cancellationToken);
            return new ControlledPreflightResult(0,
                $"Preflight target '{targetMigration}' certified in a REPEATABLE READ, READ ONLY transaction; historial=10/10/0; objetos_reservados=0/2; rollback=confirmed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ControlledPreflightResult(ControlledPreflightRunner.FailedExitCode, $"Preflight target '{targetMigration}' was cancelled.");
        }
        catch
        {
            return new ControlledPreflightResult(ControlledPreflightRunner.FailedExitCode, $"Preflight target '{targetMigration}' failed without changing the database.");
        }
    }

    private static async Task<bool> HasOfficialMovementTypeConstraintAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT c.convalidated, pg_catalog.pg_get_expr(c.conbin, c.conrelid)
            FROM pg_catalog.pg_constraint c
            JOIN pg_catalog.pg_class r ON r.oid = c.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = r.relnamespace
            WHERE n.nspname = 'compras' AND r.relname = 'movimiento_inventario'
              AND c.conname = 'ck_movimiento_inventario_tipo' AND c.contype = 'c'
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(0) || reader.IsDBNull(1)) return false;
        var expression = reader.GetString(1);
        if (await reader.ReadAsync(cancellationToken)) return false;
        return IsOfficialMovementTypeExpression(expression);
    }

    private static bool IsOfficialMovementTypeExpression(string expression)
    {
        // PostgreSQL deparses IN as = ANY(ARRAY[...]) with casts and parentheses.
        // Normalize only outside SQL string literals; their contents are data.
        var normalized = new StringBuilder();
        var outside = new StringBuilder();
        var inLiteral = false;

        void FlushOutside()
        {
            var text = PostgreSqlCastPattern().Replace(outside.ToString(), "");
            normalized.Append(WhitespaceOrParenthesisPattern().Replace(text, ""));
            outside.Clear();
        }

        for (var i = 0; i < expression.Length; i++)
        {
            var value = expression[i];
            if (value == '\'')
            {
                if (!inLiteral) FlushOutside();
                normalized.Append(value);
                if (inLiteral && i + 1 < expression.Length && expression[i + 1] == '\'')
                    normalized.Append(expression[++i]);
                else
                    inLiteral = !inLiteral;
            }
            else if (inLiteral) normalized.Append(value);
            else outside.Append(value);
        }

        if (inLiteral) return false;
        FlushOutside();
        return string.Equals(normalized.ToString(), OfficialExpression, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"::(?:character varying|text|varchar)(?:\[\])?", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PostgreSqlCastPattern();

    [GeneratedRegex(@"[\s()]", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex WhitespaceOrParenthesisPattern();

    private static async Task<bool> HasReservedObjectsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_catalog.pg_class r JOIN pg_catalog.pg_namespace n ON n.oid = r.relnamespace
                WHERE n.nspname = 'compras' AND r.relname = 'uq_movimiento_inventario_inicial_producto'
            ) OR EXISTS (
                SELECT 1 FROM pg_catalog.pg_constraint c JOIN pg_catalog.pg_namespace n ON n.oid = c.connamespace
                WHERE n.nspname = 'compras' AND c.conname = 'ck_movimiento_inventario_inicial'
            )
            """, connection, transaction);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<ControlledPreflightResult> RollbackRejectedAsync(
        IDbContextTransaction transaction, string target, string reason, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return Rejected(target, reason);
    }

    private static ControlledPreflightResult Rejected(string target, string reason) =>
        new(ControlledPreflightRunner.RejectedExitCode, $"Preflight target '{target}' rejected: {reason}.");
}
