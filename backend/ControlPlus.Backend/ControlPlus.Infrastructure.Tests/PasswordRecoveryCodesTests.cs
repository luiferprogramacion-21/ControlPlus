using ControlPlus.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

/// <summary>Códigos de recuperación de contraseña: sin base de datos.</summary>
public sealed class PasswordRecoveryCodesTests
{
    private static readonly Guid UserId = Guid.Parse("019a0000-0000-7000-8000-0000000000aa");
    private static readonly DateTimeOffset Sent = new(2026, 11, 18, 14, 2, 30, TimeSpan.Zero);

    private static PasswordRecoveryCodes Codes(string signingKey = "clave-de-firma-de-pruebas-con-al-menos-sesenta-y-cuatro-bytes-utf8!!") =>
        new(Options.Create(new JwtOptions { Issuer = "pruebas", Audience = "pruebas", SigningKey = signingKey }));

    [Fact]
    public void Code_HasSixDigits_AndIsValidForAtLeastTenMinutes_ButNotFifteen()
    {
        var codes = Codes();
        var code = codes.Create(UserId, "sello-1", Sent);
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(TimeSpan.FromMinutes(10), codes.MinimumValidity);
        Assert.True(codes.Verify(UserId, "sello-1", code, Sent));
        Assert.True(codes.Verify(UserId, "sello-1", code, Sent.AddMinutes(10)));
        Assert.False(codes.Verify(UserId, "sello-1", code, Sent.AddMinutes(15)));
        Assert.False(codes.Verify(UserId, "sello-1", code, Sent.AddMinutes(-5)));
    }

    [Fact]
    public void Code_StopsWorkingWhenTheSecurityStampChanges_AndBelongsToItsUser()
    {
        var codes = Codes();
        var code = codes.Create(UserId, "sello-1", Sent);
        Assert.False(codes.Verify(UserId, "sello-2", code, Sent));
        Assert.False(codes.Verify(Guid.Parse("019a0000-0000-7000-8000-0000000000bb"), "sello-1", code, Sent));
        Assert.False(Codes("otra-clave-de-firma-de-pruebas-con-al-menos-sesenta-y-cuatro-bytes!!").Verify(UserId, "sello-1", code, Sent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("١٢٣٤٥٦")]
    public void MalformedCodes_AreRejected(string code) =>
        Assert.False(Codes().Verify(UserId, "sello-1", code, Sent));
}
