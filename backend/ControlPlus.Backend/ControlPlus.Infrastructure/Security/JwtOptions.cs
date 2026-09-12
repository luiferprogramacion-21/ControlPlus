using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ControlPlus.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 30;

    public int ClockSkewSeconds { get; init; } = 30;

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Jwt:Audience is required.");
        }

        if (Encoding.UTF8.GetByteCount(SigningKey) < 64)
        {
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 64 UTF-8 bytes for HS512.");
        }

        if (AccessTokenMinutes is < 1 or > 480)
        {
            throw new InvalidOperationException("Jwt:AccessTokenMinutes must be between 1 and 480.");
        }

        if (ClockSkewSeconds is < 0 or > 300)
        {
            throw new InvalidOperationException("Jwt:ClockSkewSeconds must be between 0 and 300.");
        }
    }

    public SymmetricSecurityKey CreateSigningKey()
    {
        EnsureValid();
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
    }
}
