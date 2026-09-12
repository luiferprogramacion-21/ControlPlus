using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Tests;

public sealed class UserSecurityTests
{
    private static readonly DateTimeOffset InitialUtc =
        new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FifthFailedLogin_LocksTheUserAndRotatesTheSecurityStamp()
    {
        var user = CreateUserWithRole(RoleLevel.Cajero);
        var securityStampBeforeLock = user.SecurityStamp;

        for (var attempt = 1; attempt < User.MaximumFailedLoginAttempts; attempt++)
        {
            var locked = user.RegisterFailedLogin(InitialUtc.AddMinutes(attempt));

            Assert.False(locked);
            Assert.True(user.IsActive);
            Assert.Equal(attempt, user.FailedLoginAttempts);
        }

        var lockedOnFifthAttempt = user.RegisterFailedLogin(
            InitialUtc.AddMinutes(User.MaximumFailedLoginAttempts));

        Assert.True(lockedOnFifthAttempt);
        Assert.True(user.IsLocked);
        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.Equal(User.MaximumFailedLoginAttempts, user.FailedLoginAttempts);
        Assert.Equal(InitialUtc.AddMinutes(User.MaximumFailedLoginAttempts), user.LockedAtUtc);
        Assert.NotEqual(securityStampBeforeLock, user.SecurityStamp);
    }

    [Fact]
    public void SuccessfulLogin_ResetsTheFailedAttemptCounter()
    {
        var user = CreateUserWithRole(RoleLevel.Cajero);

        user.RegisterFailedLogin(InitialUtc.AddMinutes(1));
        user.RegisterFailedLogin(InitialUtc.AddMinutes(2));
        user.RegisterFailedLogin(InitialUtc.AddMinutes(3));

        var successfulLoginAtUtc = InitialUtc.AddMinutes(4);
        user.RegisterSuccessfulLogin(successfulLoginAtUtc);

        Assert.True(user.IsActive);
        Assert.False(user.IsLocked);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Equal(successfulLoginAtUtc, user.LastLoginAtUtc);
    }

    [Fact]
    public void Reactivate_RequiresAStrictlyHigherRole()
    {
        var user = CreateUserWithRole(RoleLevel.Cajero);
        Lock(user);

        Assert.Throws<DomainRuleViolationException>(() =>
            user.Reactivate(RoleLevel.Cajero, InitialUtc.AddMinutes(6)));
        Assert.True(user.IsLocked);

        user.Reactivate(RoleLevel.Supervisor, InitialUtc.AddMinutes(7));

        Assert.True(user.IsActive);
        Assert.False(user.IsLocked);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedAtUtc);
    }

    private static User CreateUserWithRole(RoleLevel roleLevel)
    {
        var role = Role.Create(roleLevel.ToString(), roleLevel.ToString(), roleLevel, InitialUtc);
        var user = User.Create("caja-01", "Caja 01", "test-password-hash", InitialUtc);

        user.AddRole(role, InitialUtc);

        return user;
    }

    private static void Lock(User user)
    {
        for (var attempt = 1; attempt <= User.MaximumFailedLoginAttempts; attempt++)
        {
            user.RegisterFailedLogin(InitialUtc.AddMinutes(attempt));
        }
    }
}
