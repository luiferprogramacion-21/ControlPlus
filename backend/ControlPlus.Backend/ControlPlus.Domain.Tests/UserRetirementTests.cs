using ControlPlus.Domain.OfficialModel;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class UserRetirementTests
{
    [Fact]
    public void Retire_FreesUserNameKeepsDisplayNameAndBlocksSignIn()
    {
        // Decisión D6 del 23/11/2026: se elimina un usuario con historial dejando solo su nombre.
        var now = new DateTimeOffset(2026, 11, 23, 15, 0, 0, TimeSpan.Zero);
        var user = Usuario.Create("ana.caja", "Ana Pérez", "hash-original", now);
        user.ChangeEmail("ana@example.com", now);
        var stamp = user.SecurityStamp;

        user.Retire("hash-inservible", now.AddMinutes(5));

        Assert.True(user.IsRetired);
        Assert.Equal(Usuario.RetiredUserNamePrefix + user.Id.ToString("N"), user.UserName);
        Assert.Equal(user.UserName.ToUpperInvariant(), user.NormalizedUserName);
        Assert.Equal("Ana Pérez", user.DisplayName);
        Assert.Null(user.Email);
        Assert.Equal("hash-inservible", user.PasswordHash);
        Assert.False(user.IsActive);
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.True(user.UserName.Length <= 100);
    }

    [Fact]
    public void NewUser_IsNotRetired() =>
        Assert.False(Usuario.Create("ana.caja", "Ana", "hash", DateTimeOffset.UtcNow).IsRetired);
}
