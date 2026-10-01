using ControlPlus.Domain.Common;
using ControlPlus.Domain.Inventory;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class InventoryAdjustmentPolicyTests
{
    [Theory]
    [InlineData(10, 2, 5, "AJUSTE_ENTRADA", 15)]
    [InlineData(10, 2, -8, "AJUSTE_SALIDA", 2)]
    public void Apply_ReturnsOfficialMovementAndPreservesReservedStock(
        int current, int reserved, int delta, string expectedType, int expectedStock)
    {
        var result = InventoryAdjustmentPolicy.Apply(true, current, reserved, delta);

        Assert.Equal(expectedType, result.MovementType);
        Assert.Equal(current, result.PreviousStock);
        Assert.Equal(expectedStock, result.NewStock);
        Assert.Equal(reserved, result.ReservedStock);
    }

    [Theory]
    [InlineData(false, 10, 0, 1)]
    [InlineData(true, 10, 0, 0)]
    [InlineData(true, 1, 0, -2)]
    [InlineData(true, 10, 8, -3)]
    [InlineData(true, int.MaxValue, 0, 1)]
    public void Apply_RejectsInvalidOrUnsafeAdjustment(bool active, int current, int reserved, int delta)
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            InventoryAdjustmentPolicy.Apply(active, current, reserved, delta));
    }
}
