using System.Data;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ControlPlus.Api.Migrations;

internal static class CustomerSearchPreflightRunner
{
    public static async Task<ControlledPreflightResult> RunAsync(
        IConfiguration configuration,
        string targetMigration,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("ControlPlusDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Rejected(targetMigration, "the database configuration is unavailable");
        }

        try
        {
            var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            await using var context = new ControlPlusDbContext(options);
            var expectedCatalog = CustomerSearchMigrationManifest.RequiredPreviousMigrations
                .Append(CustomerSearchMigrationManifest.TargetMigration)
                .ToArray();
            if (!context.Database.GetMigrations().SequenceEqual(expectedCatalog, StringComparer.Ordinal))
            {
                return Rejected(targetMigration, "the EF migration catalog is not the exact required sequence");
            }

            await context.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            await CustomerSearchDatabaseValidation.SetTransactionReadOnlyAsync(
                connection,
                npgsqlTransaction,
                cancellationToken);
            if (!await CustomerSearchDatabaseValidation.IsTransactionReadOnlyAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "PostgreSQL did not confirm a read-only transaction",
                    cancellationToken);
            }

            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            if (!applied.SequenceEqual(
                    CustomerSearchMigrationManifest.RequiredPreviousMigrations,
                    StringComparer.Ordinal))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "the EF history must contain exactly the six required predecessor migrations",
                    cancellationToken);
            }

            if (!await CustomerSearchDatabaseValidation.HasRequiredTableShapeAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "catalogo.cliente does not have the exact required shape",
                    cancellationToken);
            }

            if (!await CustomerSearchDatabaseValidation.ReservedIndexesAreAbsentAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "an index reserved for the target already exists",
                    cancellationToken);
            }

            if (!await CustomerSearchDatabaseValidation.IsTransactionReadOnlyAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "the read-only transaction context was not preserved",
                    cancellationToken);
            }

            await transaction.RollbackAsync(cancellationToken);
            return new ControlledPreflightResult(
                0,
                $"Preflight target '{targetMigration}' certified in a REPEATABLE READ, READ ONLY transaction; " +
                $"historial=6/6/0; indices_reservados=0/{CustomerSearchMigrationManifest.Indexes.Count}; " +
                "rollback=confirmed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ControlledPreflightResult(
                ControlledPreflightRunner.FailedExitCode,
                $"Preflight target '{targetMigration}' was cancelled.");
        }
        catch
        {
            return new ControlledPreflightResult(
                ControlledPreflightRunner.FailedExitCode,
                $"Preflight target '{targetMigration}' failed without changing the database.");
        }
    }

    private static async Task<ControlledPreflightResult> RollbackRejectedAsync(
        IDbContextTransaction transaction,
        string target,
        string reason,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return Rejected(target, reason);
    }

    private static ControlledPreflightResult Rejected(string target, string reason) =>
        new(
            ControlledPreflightRunner.RejectedExitCode,
            $"Preflight target '{target}' rejected: {reason}.");
}
