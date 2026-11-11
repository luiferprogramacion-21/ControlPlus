using System.Text.Json.Serialization;

namespace ControlPlus.Application.Security.Contracts;

/// <summary>
/// Credentials of the second user who authorizes an operation: user name and password, or the Code 128 token scanned from the
/// authorizer's operator credential (never both). Every authorization request of the API implements it.
/// </summary>
public interface IAuthorizerCredentials
{
    string? UserName { get; }
    string? Password { get; }
    string? CredentialToken { get; }
    /// <summary>Set by the service after it resolved <see cref="CredentialToken"/>; never read from the request body.</summary>
    [JsonIgnore] Guid? ResolvedCredentialId { get; }
}

public static class AuthorizerCredentialsExtensions
{
    /// <summary>True when the authorizer was identified by a scanned credential instead of a password.</summary>
    public static bool UsesCredential(this IAuthorizerCredentials credentials) =>
        !string.IsNullOrWhiteSpace(credentials.CredentialToken);

    /// <summary>Either user name and password, or only the scanned credential (before it is resolved).</summary>
    public static bool IsComplete(this IAuthorizerCredentials credentials) => credentials.UsesCredential()
        ? string.IsNullOrWhiteSpace(credentials.Password) &&
          (credentials.ResolvedCredentialId is not null || string.IsNullOrWhiteSpace(credentials.UserName))
        : !string.IsNullOrWhiteSpace(credentials.UserName) && !string.IsNullOrWhiteSpace(credentials.Password);

    /// <summary>A scanned credential must replace the user name and password, never accompany them.</summary>
    public static bool MixesCredentialWithPassword(this IAuthorizerCredentials credentials) => credentials.UsesCredential() &&
        (!string.IsNullOrWhiteSpace(credentials.UserName) || !string.IsNullOrWhiteSpace(credentials.Password));

    /// <summary>Method stored in <c>seguridad.autorizacion_operacion.metodo_autenticacion</c>.</summary>
    public static string AuthenticationMethod(this IAuthorizerCredentials? credentials) =>
        credentials?.UsesCredential() == true ? "CODIGO_BARRAS_PIN" : "PASSWORD";
}
