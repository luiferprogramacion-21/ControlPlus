using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class CommercialAuthorizationPolicyTests
{
    [Fact]
    // Decisión D7 del 23/11/2026: ya no hay tope del 80 %; un límite de rol mayor a 100 % se rechaza y el costo sigue
    // siendo el piso: el Administrador puede llegar a 85 % si el precio final no queda bajo el costo.
    public void DiscountLimit_CannotExceedOneHundredPercent_AndCostIsTheFloor()
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            CommercialAuthorizationPolicy.CalculateFinalPrice(10m, 100m, 101m, 101m, false));
        Assert.Equal(15m, CommercialAuthorizationPolicy.CalculateFinalPrice(10m, 100m, 85m, 100m, false));
        Assert.Throws<DomainRuleViolationException>(() =>
            CommercialAuthorizationPolicy.CalculateFinalPrice(20m, 100m, 85m, 100m, false));
    }

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
