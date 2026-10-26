using ControlPlus.Domain.Sales;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class SaleChangeAvailabilityTests
{
    [Theory]
    [InlineData("2026-10-25", "2026-10-25", true)]
    [InlineData("2026-10-26", "2026-10-25", false)]
    [InlineData("2026-10-24", "2026-10-25", true)]
    public void Deadline_IsInclusiveAndUsesLocalDate(string current, string deadline, bool expected) =>
        Assert.Equal(expected, SaleChangeAvailability.IsWithinDeadline(
            DateOnly.Parse(current), DateOnly.Parse(deadline)));

    [Theory]
    [InlineData(3, 0, 3)]
    [InlineData(3, 2, 1)]
    [InlineData(3, 3, 0)]
    public void RemainingQuantity_SubtractsOnlyConfirmedReturns(int sold, int returned, int expected) =>
        Assert.Equal(expected, SaleChangeAvailability.RemainingQuantity(sold, returned));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, -1)]
    [InlineData(2, 3)]
    public void RemainingQuantity_RejectsInconsistentHistory(int sold, int returned) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SaleChangeAvailability.RemainingQuantity(sold, returned));

    [Fact]
    public void HistoricalReturnValue_AssignsDiscountRemainderToFinalReturnedItem()
    {
        Assert.Equal(99m, SaleChangeAvailability.HistoricalReturnValue(100m, 2m, 3, 0, 1));
        Assert.Equal(99m, SaleChangeAvailability.HistoricalReturnValue(100m, 2m, 3, 1, 1));
        Assert.Equal(100m, SaleChangeAvailability.HistoricalReturnValue(100m, 2m, 3, 2, 1));
        Assert.Equal(198m, SaleChangeAvailability.HistoricalReturnValue(100m, 2m, 3, 0, 2));
        Assert.Equal(298m, SaleChangeAvailability.HistoricalReturnValue(100m, 2m, 3, 0, 3));
    }

    [Fact]
    public void HistoricalReturnValue_NeverMakesFinalItemNegativeWhenDiscountIsLarge()
    {
        Assert.Equal(0m, SaleChangeAvailability.HistoricalReturnValue(1m, 2m, 3, 0, 1));
        Assert.Equal(0m, SaleChangeAvailability.HistoricalReturnValue(1m, 2m, 3, 1, 1));
        Assert.Equal(1m, SaleChangeAvailability.HistoricalReturnValue(1m, 2m, 3, 2, 1));
    }

    [Fact]
    public void HistoricalReturnValue_RejectsReturnsBeyondSoldQuantity() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SaleChangeAvailability.HistoricalReturnValue(100m, 2m, 3, 2, 2));
}
