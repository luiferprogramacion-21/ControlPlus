using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ControlPlus.Api.Migrations;

public sealed record ControlledMigrationResult(int ExitCode, string Message);

public static class ControlledMigrationRunner
{
    public const int FailedExitCode = 1;
    public const int RejectedExitCode = 2;
    public const string InitialProductInventoryMigration = "20261026010000_InitialProductInventoryMovementV1";
    public const string PermanentProductReferencesMigration = "20261029010000_PermanentProductReferencesV1";

    public static async Task<ControlledMigrationResult> RunAsync(
        IConfiguration configuration,
        string targetMigration,
        CancellationToken cancellationToken = default)
    {
        if (!ControlledExecutionCommand.IsValidMigrationId(targetMigration))
        {
            return Rejected("invalid-target", "the target identifier is invalid");
        }

        var safeTarget = targetMigration;

        var connectionString = configuration.GetConnectionString("ControlPlusDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Rejected(safeTarget, "the database configuration is unavailable");
        }

        try
        {
            var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            await using var context = new ControlPlusDbContext(options);

            var migrations = context.Database.GetMigrations().ToArray();
            var targetIndex = Array.IndexOf(migrations, targetMigration);
            if (targetIndex < 0)
            {
                return Rejected(safeTarget, "the target does not exist in the migration catalog");
            }

            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            if (!IsCoherentPrefix(migrations, applied))
            {
                return Rejected(safeTarget, "the migration history is not a coherent catalog prefix");
            }

            var lastAppliedIndex = applied.Length - 1;
            if (targetIndex < lastAppliedIndex)
            {
                return Rejected(safeTarget, "the target would require a reversal");
            }

            if (targetIndex == lastAppliedIndex)
            {
                return Rejected(safeTarget, "the target is already applied");
            }

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            var expectedPending = migrations.Skip(applied.Length).ToArray();
            if (!pending.SequenceEqual(expectedPending, StringComparer.Ordinal))
            {
                return Rejected(safeTarget, "the pending migration sequence is not coherent");
            }

            if (string.Equals(
                    targetMigration,
                    InventoryMigrationManifest.TargetMigration,
                    StringComparison.Ordinal) &&
                (!applied.SequenceEqual(
                    InventoryMigrationManifest.RequiredPreviousMigrations,
                    StringComparer.Ordinal) ||
                 targetIndex != applied.Length ||
                 targetIndex - applied.Length + 1 != 1))
            {
                return Rejected(
                    safeTarget,
                    "Inventory requires Caja as the fifth and last applied migration and can only apply Inventory");
            }

            if (string.Equals(
                    targetMigration,
                    CustomerSearchMigrationManifest.TargetMigration,
                    StringComparison.Ordinal) &&
                (!applied.SequenceEqual(
                    CustomerSearchMigrationManifest.RequiredPreviousMigrations,
                    StringComparer.Ordinal) ||
                 targetIndex != applied.Length ||
                 targetIndex - applied.Length + 1 != 1 ||
                 pending.Length == 0 ||
                 !string.Equals(pending[0], targetMigration, StringComparison.Ordinal)))
            {
                return Rejected(
                    safeTarget,
                    "Customer search indexes require Inventory as the sixth and last applied migration and can only apply the customer index target");
            }

            if (string.Equals(targetMigration, InitialProductInventoryMigration, StringComparison.Ordinal) &&
                (targetIndex != applied.Length || pending.Length == 0 ||
                 !string.Equals(pending[0], targetMigration, StringComparison.Ordinal)))
            {
                return Rejected(safeTarget, "initial product inventory must be the first pending migration and can only apply that target");
            }

            if (targetMigration == PermanentProductReferencesMigration && targetIndex != applied.Length)
                return Rejected(safeTarget, "permanent product references must be the first pending migration; earlier migrations need separate approval");

            var requestedSequence = expectedPending.Take(targetIndex - applied.Length + 1).ToArray();
            if (requestedSequence.Length == 0 ||
                !string.Equals(requestedSequence[^1], targetMigration, StringComparison.Ordinal))
            {
                return Rejected(safeTarget, "the target is not the exact end of an ascending pending sequence");
            }

            await context.GetService<IMigrator>().MigrateAsync(targetMigration, cancellationToken);

            var appliedAfter = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            var expectedAfter = migrations.Take(targetIndex + 1).ToArray();
            if (!appliedAfter.SequenceEqual(expectedAfter, StringComparer.Ordinal) ||
                appliedAfter.Count(item => string.Equals(item, targetMigration, StringComparison.Ordinal)) != 1)
            {
                return Failed(safeTarget);
            }

            return new ControlledMigrationResult(
                0,
                $"Migration target '{safeTarget}' applied successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ControlledMigrationResult(
                FailedExitCode,
                $"Migration target '{safeTarget}' was cancelled.");
        }
        catch (Exception exception)
        {
            // Only the PostgreSQL server's own error (code, message, table, constraint and PL/pgSQL line) is shown so the
            // operator knows which rule failed (20 November 2026). Other exceptions keep their details hidden because a
            // provider error can contain connection data.
            return Failed(safeTarget, DescribeFailure(exception));
        }
    }

    private static bool IsCoherentPrefix(IReadOnlyList<string> migrations, IReadOnlyList<string> applied)
    {
        if (applied.Count > migrations.Count)
        {
            return false;
        }

        for (var index = 0; index < applied.Count; index++)
        {
            if (!string.Equals(applied[index], migrations[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static ControlledMigrationResult Rejected(string target, string reason) =>
        new(RejectedExitCode, $"Migration target '{target}' rejected: {reason}.");

    private static ControlledMigrationResult Failed(string target, string? reason = null) =>
        new(FailedExitCode, $"Migration target '{target}' failed. No manual rollback was attempted." +
            (reason is null ? "" : $" Cause: {reason}"));

    internal static string DescribeFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is Npgsql.PostgresException postgres)
            {
                var parts = new List<string> { $"PostgreSQL {postgres.SqlState}: {postgres.MessageText}" };
                if (!string.IsNullOrWhiteSpace(postgres.TableName)) parts.Add($"table {postgres.SchemaName}.{postgres.TableName}");
                if (!string.IsNullOrWhiteSpace(postgres.ConstraintName)) parts.Add($"constraint {postgres.ConstraintName}");
                if (!string.IsNullOrWhiteSpace(postgres.Where)) parts.Add($"where {postgres.Where.ReplaceLineEndings(" | ")}");
                return string.Join("; ", parts);
            }
        }
        return exception.GetType().Name;
    }
}
