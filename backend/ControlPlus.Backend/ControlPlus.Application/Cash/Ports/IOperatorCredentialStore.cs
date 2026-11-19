namespace ControlPlus.Application.Cash.Ports;

/// <summary>Recoverable, protected copies of operator tokens. All writes join the caller's transaction.</summary>
public interface IOperatorCredentialStore
{
    Task<string?> ReadAsync(Guid userId, Guid credentialId, CancellationToken cancellationToken = default);
    Task StoreAsync(Guid userId, Guid credentialId, string token, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid userId, Guid credentialId, CancellationToken cancellationToken = default);
}
