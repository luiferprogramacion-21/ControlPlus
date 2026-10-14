using System.Globalization;
using ControlPlus.Domain.Purchases;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class PurchaseCostPolicyTests
{
    [Theory]
    [InlineData("NO_DISCRIMINADO", null, "12.5000")]
    [InlineData("INCLUIDO", "19", "12.5000")]
    [InlineData("SEPARADO", "19", "14.875000")]
    public void EffectiveCost_UsesNetUnitCostAndDoesNotDoubleCountIncludedVat(
        string mode, string? percentage, string expected)
    {
        var rate = percentage is null ? (decimal?)null : decimal.Parse(percentage, CultureInfo.InvariantCulture);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture),
            PurchaseCostPolicy.EffectiveUnitCost(12.5m, mode, rate));
    }

    [Fact]
    public void Average_UsesFullDecimalPrecisionUntilFinalRound()
    {
        var unit = PurchaseCostPolicy.EffectiveUnitCost(12.3456m, "SEPARADO", 19m);
        Assert.Equal(14.691264m, unit);
        Assert.Equal(12.8148m, PurchaseCostPolicy.Average(10m, 2, 3, unit));
        Assert.Equal(14.6913m, PurchaseCostPolicy.Average(null, 8, 1, unit));
        Assert.Equal(14.6913m, PurchaseCostPolicy.Average(10m, 0, 1, unit));
    }

    [Fact]
    public void Average_RoundsHalfAwayFromZeroAndRejectsOfficialOverflow()
    {
        Assert.Equal(1.0001m, PurchaseCostPolicy.Average(null, 0, 1, 1.00005m));
        Assert.Throws<OverflowException>(() => PurchaseCostPolicy.Average(null, 0, 1,
            PurchaseCostPolicy.MaximumFourPlaceAmount + 1m));
        Assert.Throws<OverflowException>(() => PurchaseCostPolicy.Average(10m, int.MaxValue, 1, 10m));
    }
}
