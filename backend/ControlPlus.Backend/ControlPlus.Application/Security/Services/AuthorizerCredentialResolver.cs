using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Application.Security.Services;

public sealed record ResolvedAuthorizerCredential(Guid CredentialId, string UserName);

/// <summary>Identifies the authorizer behind a scanned Code 128 operator credential.</summary>
public interface IAuthorizerCredentialResolver
{
    /// <summary>The active, unexpired credential whose token matches, or null. A random token of at least 72 bits, checked against its slow hash, needs no lockout counter.</summary>
    Task<ResolvedAuthorizerCredential?> ResolveAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="ResolveAsync"/>, but an unknown token yields a user name that does not exist and <see cref="Guid.Empty"/>,
    /// so the caller's normal flow fails exactly as for an unknown authorizer.
    /// </summary>
    Task<(string UserName, Guid CredentialId)> ResolveOrUnknownAsync(string token, CancellationToken cancellationToken = default);
}

public sealed class AuthorizerCredentialResolver(
    ICashRepository cashRepository, IUserRepository userRepository, IPasswordHasher passwordHasher, IClock clock)
    : IAuthorizerCredentialResolver
{
    /// <summary>User name that never exists; a service that receives it fails like any unknown authorizer.</summary>
    public const string UnknownUserName = "~credencial-invalida~";

    public async Task<(string UserName, Guid CredentialId)> ResolveOrUnknownAsync(string token, CancellationToken cancellationToken = default)
    {
        var match = await ResolveAsync(token, cancellationToken);
        return match is null ? (UnknownUserName, Guid.Empty) : (match.UserName, match.CredentialId);
    }

    public async Task<ResolvedAuthorizerCredential?> ResolveAsync(string token, CancellationToken cancellationToken = default)
    {
        token = token?.Trim() ?? string.Empty;
        if (token.Length == 0) return null;
        var fragment = token[^Math.Min(8, token.Length)..];
        var nowUtc = clock.UtcNow.UtcDateTime;
        foreach (var credential in await cashRepository.ListActiveCredentialsAsync(cancellationToken))
        {
            if (credential.FragmentoVisible != fragment || credential.FormatoCodigo != "CODE128" ||
                (credential.FechaVencimiento is not null && credential.FechaVencimiento <= nowUtc)) continue;
            if (!passwordHasher.Verify(token, credential.TokenHash)) continue;
            var user = await userRepository.GetByIdAsync(credential.UsuarioId, cancellationToken);
            return user is null ? null : new ResolvedAuthorizerCredential(credential.Id, user.UserName);
        }
        return null;
    }
}
