using ControlPlus.Domain.Common;
using ControlPlus.Domain.Sales;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Tests;

public sealed class SalePricingPolicyTests
{
    [Theory]
    [InlineData(RoleLevel.Cajero, 5)]
    [InlineData(RoleLevel.Supervisor, 20)]
    [InlineData(RoleLevel.Administrador, 100)] // D7: sin porcentaje máximo, lo limita el costo
    public void MaximumDiscount_UsesApprovedRoleLimits(RoleLevel role, decimal expected) =>
        Assert.Equal(expected, SalePricingPolicy.MaximumDiscountPercentage(role));

    [Fact]
    public void GeneralDiscount_DistributesRoundedPesoByLargestRemainderDeterministically()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var third = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var result = SalePricingPolicy.AllocateByLargestRemainder(16m,
        [
            new(first, 5.05m),
            new(second, 5.10m),
            new(third, 5.35m)
        ]);

        Assert.Equal(5m, result[first]);
        Assert.Equal(5m, result[second]);
        Assert.Equal(6m, result[third]);
        Assert.Equal(16m, result.Values.Sum());
    }

    [Fact]
    public void SaleBelowCost_IsRejectedAfterIntegerDiscountDistribution() =>
        Assert.Throws<DomainRuleViolationException>(() =>
            SalePricingPolicy.EnsureNotBelowCost(89m, 90m, 1));
}
