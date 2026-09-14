namespace ControlPlus.Application.Security.Ports;

/// <summary>
/// Serializes authentication state transitions for one normalized user name.
/// </summary>
public interface ILoginAttemptCoordinator
{
    Task<T> ExecuteAsync<T>(
        string normalizedUserName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
