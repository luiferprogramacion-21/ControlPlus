using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ControlPlus.Application.Security.Ports;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ControlPlus.Infrastructure.Security;

public static class JwtClaimTypes
{
    public const string SecurityStamp = "security_stamp";
}

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, IClock clock) : ITokenIssuer
{
    public IssuedAccessToken Issue(TokenSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var jwt = options.Value;
        jwt.EnsureValid();

        var issuedAtUtc = clock.UtcNow;
        var expiresAtUtc = issuedAtUtc.AddMinutes(jwt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.UserId.ToString("D")),
            new(JwtRegisteredClaimNames.UniqueName, subject.UserName),
            new(ClaimTypes.Name, subject.DisplayName),
            new(JwtClaimTypes.SecurityStamp, subject.SecurityStamp),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("D"))
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            NotBefore = issuedAtUtc.UtcDateTime,
            Expires = expiresAtUtc.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha512)
        };

        var token = new JwtSecurityTokenHandler().CreateToken(descriptor);
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
