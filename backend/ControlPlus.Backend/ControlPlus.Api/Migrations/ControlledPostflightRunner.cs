using System.Data;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ControlPlus.Api.Migrations;

public sealed record ControlledPostflightResult(int ExitCode, string Message);

public static class ControlledPostflightRunner
{
    public const int FailedExitCode = 1;
    public const int RejectedExitCode = 2;

    public static async Task<ControlledPostflightResult> RunAsync(
        IConfiguration configuration,
        string targetMigration,
        string? expectedUnrelatedReasonsSha256,
        CancellationToken cancellationToken = default)
    {
        if (!ControlledExecutionCommand.IsValidMigrationId(targetMigration))
        {
            return Rejected("invalid-target", "the target identifier is invalid");
        }

        if (string.Equals(
                targetMigration,
                CustomerSearchMigrationManifest.TargetMigration,
                StringComparison.Ordinal))
        {
            if (expectedUnrelatedReasonsSha256 is not null)
            {
                return Rejected(targetMigration, "this target does not accept an unrelated-reasons SHA-256");
            }

            return await CustomerSearchPostflightRunner.RunAsync(
                configuration,
                targetMigration,
                cancellationToken);
        }

        if (!string.Equals(targetMigration, InventoryMigrationManifest.TargetMigration, StringComparison.Ordinal))
        {
            return Rejected(targetMigration, "the target is not supported by this postflight");
        }

        if (!ControlledExecutionCommand.IsValidSha256(expectedUnrelatedReasonsSha256))
        {
            return Rejected(targetMigration, "the expected unrelated-reasons SHA-256 is invalid");
        }

        var normalizedExpectedSha256 = expectedUnrelatedReasonsSha256!.ToLowerInvariant();

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
            var expectedHistory = InventoryMigrationManifest.RequiredPreviousMigrations
                .Append(InventoryMigrationManifest.TargetMigration)
                .ToArray();
            var catalog = context.Database.GetMigrations().ToArray();
            if (!catalog.Take(expectedHistory.Length).SequenceEqual(expectedHistory, StringComparer.Ordinal) ||
                Array.IndexOf(catalog, targetMigration) != expectedHistory.Length - 1)
            {
                return Rejected(targetMigration, "the EF migration catalog is not the required coherent sequence");
            }

            await context.Database.OpenConnectionAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
            await InventoryDatabaseValidation.SetTransactionReadOnlyAsync(
                connection,
                npgsqlTransaction,
                cancellationToken);
            if (!await InventoryDatabaseValidation.IsTransactionReadOnlyAsync(
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
                    "the EF history must contain exactly the six required migrations and the target once",
                    cancellationToken);
            }

            if (!await InventoryDatabaseValidation.HasRequiredTableShapeAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken) ||
                !await InventoryDatabaseValidation.HasRequiredNaturalKeyConstraintAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "the inventory reason table or natural key constraint is not valid",
                    cancellationToken);
            }

            var establishments = await InventoryDatabaseValidation.CountEstablishmentsAsync(
                connection,
                npgsqlTransaction,
                cancellationToken);
            var reasons = await InventoryDatabaseValidation.CountPostflightStateAsync(
                connection,
                npgsqlTransaction,
                cancellationToken);
            var unrelatedFingerprint =
                await InventoryDatabaseValidation.CalculateUnrelatedReasonsFingerprintAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken);
            var expectedReasons = checked(establishments * InventoryMigrationManifest.Reasons.Count);
            if (reasons.ApprovedRows != expectedReasons || reasons.InvalidExpectedRows != 0)
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    $"inventory reason verification failed (expected={expectedReasons}; actual={reasons.ApprovedRows}; invalid={reasons.InvalidExpectedRows})",
                    cancellationToken);
            }

            if (!string.Equals(
                    unrelatedFingerprint.Sha256,
                    normalizedExpectedSha256,
                    StringComparison.Ordinal))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "unrelated reasons fingerprint mismatch " +
                    $"(count={unrelatedFingerprint.Count}; " +
                    $"sha256={unrelatedFingerprint.Sha256})",
                    cancellationToken);
            }

            if (!await InventoryDatabaseValidation.IsTransactionReadOnlyAsync(
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
                $"E={establishments}; historial=6/6/0; motivos={reasons.ApprovedRows}/{expectedReasons}/0; " +
                $"motivos_ajenos={unrelatedFingerprint.Count}; " +
                $"motivos_ajenos_sha256={unrelatedFingerprint.Sha256}; rollback=confirmed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ControlledPostflightResult(
                FailedExitCode,
                $"Postflight target '{targetMigration}' was cancelled.");
        }
        catch
        {
            return Failed(targetMigration);
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
        new(RejectedExitCode, $"Postflight target '{target}' rejected: {reason}.");

    private static ControlledPostflightResult Failed(string target) =>
        new(FailedExitCode, $"Postflight target '{target}' failed without changing the database.");
}
