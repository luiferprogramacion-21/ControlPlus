using ControlPlus.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class OperatorCredentialProtectorTests
{
    private static OperatorCredentialProtector Protector(string key = "clave-de-firma-de-pruebas-con-al-menos-sesenta-y-cuatro-bytes-utf8!!") =>
        new(Options.Create(new JwtOptions { Issuer = "pruebas", Audience = "pruebas", SigningKey = key }));

    [Fact]
    public void ProtectedToken_IsRecoverableOnlyForItsUserAndCredential_AndUsesRandomNonces()
    {
        var protector = Protector();
        var user = Guid.NewGuid(); var credential = Guid.NewGuid();
        const string token = "123456789012345678901234567890123456789012345678901234567890";
        var first = protector.Protect(user, credential, token);
        var second = protector.Protect(user, credential, token);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain(token, first);
        Assert.Equal(token, protector.Unprotect(user, credential, first));
        Assert.Null(protector.Unprotect(Guid.NewGuid(), credential, first));
        Assert.Null(protector.Unprotect(user, Guid.NewGuid(), first));
        Assert.Null(Protector("otra-clave-de-firma-de-pruebas-con-al-menos-sesenta-y-cuatro-bytes!!").Unprotect(user, credential, first));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("v2:abc")] [InlineData("v1:abc")]
    [InlineData("v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void MissingLegacyMalformedAndTamperedCopies_DoNotRevealAToken(string? value) =>
        Assert.Null(Protector().Unprotect(Guid.NewGuid(), Guid.NewGuid(), value));
}
