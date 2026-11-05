using System.Data;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ControlPlus.Api.Migrations;

internal static class PermanentProductReferencesPreflightRunner
{
    internal static async Task<ControlledPreflightResult> RunAsync(
        IConfiguration configuration, string target, CancellationToken cancellationToken)
    {
        var configured = configuration.GetConnectionString("ControlPlusDb");
        if (string.IsNullOrWhiteSpace(configured)) return Rejected("database configuration unavailable");
        try
        {
            await using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>().UseNpgsql(configured).Options);
            var catalog = db.Database.GetMigrations().ToArray();
            var index = Array.IndexOf(catalog, target);
            if (index < 1 || catalog[index - 1] != "20261028010000_SaleReceiptDataSnapshotV1")
                return Rejected("unexpected migration predecessor");
            await db.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var pgTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            await CustomerSearchDatabaseValidation.SetTransactionReadOnlyAsync(connection, pgTransaction, cancellationToken);
            if (!await CustomerSearchDatabaseValidation.IsTransactionReadOnlyAsync(connection, pgTransaction, cancellationToken))
                return Rejected("read-only context unavailable");
            var history = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            if (!history.SequenceEqual(catalog.Take(index), StringComparer.Ordinal))
                return Rejected("the target must be the first pending migration");
            await using var command = new NpgsqlCommand("""
                SELECT
                  EXISTS (SELECT 1 FROM (
                    SELECT upper(btrim(code)) AS code, count(DISTINCT id) FROM (
                      SELECT codigo_interno AS code,id FROM catalogo.producto
                      UNION ALL SELECT codigo_barras,id FROM catalogo.producto WHERE codigo_barras IS NOT NULL
                    ) codes GROUP BY upper(btrim(code)) HAVING count(DISTINCT id) > 1
                  ) conflicts),
                  to_regclass('catalogo.producto_codigo') IS NOT NULL
                    OR to_regclass('catalogo.producto_referencia_seq') IS NOT NULL
                    OR to_regprocedure('catalogo.siguiente_referencia_producto()') IS NOT NULL
                    OR to_regprocedure('catalogo.reservar_codigo_producto(text,uuid)') IS NOT NULL
                    OR to_regprocedure('catalogo.proteger_codigos_producto()') IS NOT NULL
                    OR to_regprocedure('catalogo.registrar_codigos_producto()') IS NOT NULL
                """, connection, pgTransaction);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var incompatible = reader.GetBoolean(0) || reader.GetBoolean(1);
            await reader.DisposeAsync();
            await transaction.RollbackAsync(cancellationToken);
            return incompatible ? Rejected("ambiguous codes or reserved objects; no changes made") :
                new ControlledPreflightResult(0, "Permanent references preflight: REPEATABLE READ, READ ONLY; aliases unambiguous; rollback=confirmed.");
        }
        catch
        {
            return new ControlledPreflightResult(1, "Permanent references preflight failed without changing the database.");
        }
    }

    private static ControlledPreflightResult Rejected(string reason) => new(2, $"Permanent references preflight rejected: {reason}.");
}
