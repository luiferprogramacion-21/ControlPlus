using System.Data;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace ControlPlus.Api.Migrations;

internal static class InventoryPreflightRunner
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
            var catalog = context.Database.GetMigrations().ToArray();
            var targetIndex = Array.IndexOf(catalog, targetMigration);
            if (targetIndex != InventoryMigrationManifest.RequiredPreviousMigrations.Count ||
                !catalog.Take(targetIndex).SequenceEqual(
                    InventoryMigrationManifest.RequiredPreviousMigrations,
                    StringComparer.Ordinal))
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
            if (applied.Contains(targetMigration, StringComparer.Ordinal))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "the target is already applied",
                    cancellationToken);
            }

            if (!applied.SequenceEqual(
                    InventoryMigrationManifest.RequiredPreviousMigrations,
                    StringComparer.Ordinal))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "the EF history must contain exactly the five required predecessor migrations",
                    cancellationToken);
            }

            if (!await InventoryDatabaseValidation.HasRequiredTableShapeAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "configuracion.motivo_operacion does not have the required shape",
                    cancellationToken);
            }

            if (!await InventoryDatabaseValidation.HasRequiredNaturalKeyConstraintAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken))
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "uq_motivo_operacion_tipo_codigo does not have the required unique natural key shape",
                    cancellationToken);
            }

            var establishments = await InventoryDatabaseValidation.CountEstablishmentsAsync(
                connection,
                npgsqlTransaction,
                cancellationToken);
            var reasons = await InventoryDatabaseValidation.CountPreexistingReasonsAsync(
                connection,
                npgsqlTransaction,
                cancellationToken);
            var unrelatedFingerprint =
                await InventoryDatabaseValidation.CalculateUnrelatedReasonsFingerprintAsync(
                    connection,
                    npgsqlTransaction,
                    cancellationToken);
            var perCode = string.Join(
                ",",
                InventoryMigrationManifest.Reasons.Select(
                    reason => $"{reason.Code}={reasons.PerCode[reason.Code]}"));

            if (reasons.ApprovedRows != 0 ||
                reasons.WrongNaturalKeyIds != 0 ||
                reasons.DeterministicIdCollisions != 0)
            {
                return await RollbackRejectedAsync(
                    transaction,
                    targetMigration,
                    "inventory reason preconditions failed " +
                    $"(total={reasons.ApprovedRows}; {perCode}; " +
                    $"uuid_incorrectos={reasons.WrongNaturalKeyIds}; " +
                    $"colisiones_uuid={reasons.DeterministicIdCollisions})",
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
            return new ControlledPreflightResult(
                0,
                $"Preflight target '{targetMigration}' certified in a REPEATABLE READ, READ ONLY transaction; " +
                $"E={establishments}; historial=5/5/0; motivos=0/0/0; motivos_previos=0; " +
                $"codigos={perCode}; uuid_incorrectos=0; colisiones_uuid=0; " +
                $"motivos_ajenos={unrelatedFingerprint.Count}; " +
                $"motivos_ajenos_sha256={unrelatedFingerprint.Sha256}; rollback=confirmed.");
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
