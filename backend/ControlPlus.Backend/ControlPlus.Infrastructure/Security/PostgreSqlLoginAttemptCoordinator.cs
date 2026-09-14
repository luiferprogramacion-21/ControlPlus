using ControlPlus.Application.Security.Ports;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

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

        // The advisory lock is the serialization mechanism. Read committed is intentional:
        // a serializable transaction can take its snapshot before waiting for the lock,
        // then fail with PostgreSQL 40001 after the preceding attempt commits.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({normalizedUserName}, 2026091302))",
            cancellationToken);

        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
