using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Security.Ports;

/// <summary>
/// Issues a signed access token. The infrastructure adapter owns JWT-specific implementation details.
/// </summary>
public interface ITokenIssuer
{
    IssuedAccessToken Issue(TokenSubject subject);
}

public sealed record TokenSubject(
    Guid UserId,
    string UserName,
    string DisplayName,
    string SecurityStamp,
    // Sesión iniciada con una contraseña que no cumple la regla vigente: solo puede cambiarla.
    bool PasswordChangeRequired = false);

public sealed record IssuedAccessToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
