using ControlPlus.Application.Security.Ports;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ControlPlus.Infrastructure.Security;

/// <summary>
/// Uses a transaction-scoped PostgreSQL advisory lock so concurrent sign-in attempts
/// for one account cannot lose failed-attempt increments.
/// </summary>
public sealed class PostgreSqlLoginAttemptCoordinator(OfficialControlPlusDbContext dbContext)
    : ILoginAttemptCoordinator
{
    public async Task<T> ExecuteAsync<T>(
        string normalizedUserName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUserName);
        ArgumentNullException.ThrowIfNull(operation);

        // Only a known serialization abort is retried, never an ambiguous connection
        // failure at COMMIT. All callback effects must belong to this transaction.
        const int maximumAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            dbContext.ChangeTracker.Clear();
            try
            {
                // Read committed takes fresh snapshots after waiting for the account
                // lock, shared by login and cash reauthorization in every API instance.
                await using var transaction = await dbContext.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.ReadCommitted, cancellationToken);
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({normalizedUserName}, 2026091302))",
                    cancellationToken);
                var result = await operation(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception exception) when (attempt < maximumAttempts && IsSerializationAbort(exception))
            {
                // The disposed transaction has rolled back before the next iteration.
                dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsSerializationAbort(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres.SqlState == PostgresErrorCodes.SerializationFailure;
        return false;
    }
}
