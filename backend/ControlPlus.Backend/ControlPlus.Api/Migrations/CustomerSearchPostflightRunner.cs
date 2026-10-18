using System.Data;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ControlPlus.Api.Migrations;

internal static class CustomerSearchPostflightRunner
{
    public static async Task<ControlledPostflightResult> RunAsync(
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
            var expectedHistory = CustomerSearchMigrationManifest.RequiredPreviousMigrations
                .Append(CustomerSearchMigrationManifest.TargetMigration)
                .ToArray();
            if (!context.Database.GetMigrations().Take(expectedHistory.Length)
                    .SequenceEqual(expectedHistory, StringComparer.Ordinal))
            {
                return Rejected(targetMigration, "the EF migration catalog does not start with the exact required sequence");
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
            if (!applied.SequenceEqual(expectedHistory, StringComparer.Ordinal) ||
                applied.Count(item => string.Equals(item, targetMigration, StringComparison.Ordinal)) != 1)
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "the EF history must contain exactly the seven required migrations and the target once",
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

            if (!await CustomerSearchDatabaseValidation.IndexesMatchManifestAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "the customer search indexes do not match the exact manifest",
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
            return new ControlledPostflightResult(
                0,
                $"Postflight target '{targetMigration}' certified in a REPEATABLE READ, READ ONLY transaction; " +
                $"historial=7/7/0; indices={CustomerSearchMigrationManifest.Indexes.Count}/" +
                $"{CustomerSearchMigrationManifest.Indexes.Count}/0; rollback=confirmed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ControlledPostflightResult(
                ControlledPostflightRunner.FailedExitCode,
                $"Postflight target '{targetMigration}' was cancelled.");
        }
        catch
        {
            return new ControlledPostflightResult(
                ControlledPostflightRunner.FailedExitCode,
                $"Postflight target '{targetMigration}' failed without changing the database.");
        }
    }

    private static async Task<ControlledPostflightResult> RollbackRejectedAsync(
        IDbContextTransaction transaction,
        string target,
        string reason,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return Rejected(target, reason);
    }

    private static ControlledPostflightResult Rejected(string target, string reason) =>
        new(
            ControlledPostflightRunner.RejectedExitCode,
            $"Postflight target '{target}' rejected: {reason}.");
}
