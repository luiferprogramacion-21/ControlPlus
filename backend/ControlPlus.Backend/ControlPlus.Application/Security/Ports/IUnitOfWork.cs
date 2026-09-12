namespace ControlPlus.Application.Security.Ports;

/// <summary>
/// Commits the changes made by a use case atomically.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
