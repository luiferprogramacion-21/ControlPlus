using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ControlPlus.Api.Security;

public sealed class BootstrapOptions
{
    public const string SectionName = "Installation";

    public string MasterKey { get; init; } = string.Empty;
}

/// <summary>
/// Gates the one-time administrator setup endpoint with a local secret.
/// This prevents an unauthenticated caller from claiming a fresh installation.
/// </summary>
public sealed class BootstrapAccessValidator(IOptions<BootstrapOptions> options)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.MasterKey);

    public bool IsValid(string? suppliedKey)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(suppliedKey))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(options.Value.MasterKey);
        var supplied = Encoding.UTF8.GetBytes(suppliedKey);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
