using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class CommercialAuthorizationPolicyTests
{
    [Fact]
    public void DiscountLimit_CannotExceedEightyPercent() =>
        Assert.Throws<DomainRuleViolationException>(() =>
            CommercialAuthorizationPolicy.CalculateFinalPrice(10m, 100m, 81m, 81m, false));

    [Fact]
    public void SaleBelowCost_IsRejected() =>
        Assert.Throws<DomainRuleViolationException>(() =>
            CommercialAuthorizationPolicy.CalculateFinalPrice(90m, 100m, 20m, 20m, false));

    [Fact]
    public void WholesalePriceAndManualDiscount_CannotAccumulate() =>
        Assert.Throws<DomainRuleViolationException>(() =>
            CommercialAuthorizationPolicy.CalculateFinalPrice(10m, 100m, 5m, 5m, true));

    [Fact]
    public void SensitiveAuthorization_RequiresDifferentExecutorAndAuthorizer()
    {
        var sameUser = Guid.CreateVersion7();
        Assert.Throws<DomainRuleViolationException>(() =>
            CommercialAuthorizationPolicy.EnsureIndependentAuthorization(sameUser, sameUser, RoleLevel.Administrador));
    }

    [Theory]
    [InlineData(RoleLevel.None)]
    [InlineData(RoleLevel.Cajero)]
    public void SensitiveAuthorization_RequiresSupervisorOrAdministrator(RoleLevel role) =>
        Assert.Throws<DomainRuleViolationException>(() =>
            CommercialAuthorizationPolicy.EnsureIndependentAuthorization(Guid.CreateVersion7(), Guid.CreateVersion7(), role));
}
