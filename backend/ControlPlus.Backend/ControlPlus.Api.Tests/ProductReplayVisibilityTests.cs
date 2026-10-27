using ControlPlus.Application.Catalog.Contracts;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class ProductReplayVisibilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Replay_CannotRevealMoreThanOriginalOrCurrentPermission(bool originalVisible, bool currentlyAllowed)
    {
        var original = new ProductDto(
            Guid.NewGuid(), Guid.NewGuid(), "Categoria", Guid.NewGuid(), "UNIDAD", null,
            "PRODUCTO", null, null, false, "Producto", null, 1000m, null,
            originalVisible ? 250m : null, originalVisible, null, 2, 0, 2, 0, false, true, 1);

        var replay = original.RestrictCostVisibility(currentlyAllowed);

        Assert.Equal(originalVisible && currentlyAllowed, replay.CanViewCost);
        Assert.Equal(originalVisible && currentlyAllowed ? 250m : null, replay.AverageCost);
        Assert.Equal(original.Id, replay.Id);
        Assert.Equal(original.CurrentStock, replay.CurrentStock);
        Assert.Equal(original.Version, replay.Version);
    }
}
