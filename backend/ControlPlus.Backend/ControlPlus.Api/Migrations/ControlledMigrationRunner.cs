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
        catch
        {
            // Deliberately omit exception details: provider errors can contain connection data.
            return Failed(safeTarget);
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

    private static ControlledMigrationResult Failed(string target) =>
        new(FailedExitCode, $"Migration target '{target}' failed. No manual rollback was attempted.");
}
