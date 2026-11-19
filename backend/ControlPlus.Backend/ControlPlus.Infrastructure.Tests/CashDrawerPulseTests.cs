using ControlPlus.Application.Printing;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class CashDrawerPulseTests
{
    [Theory]
    [InlineData(2, 50, 100, new byte[] { 0x1b, 0x70, 0, 50, 100 })]
    [InlineData(5, 10, 250, new byte[] { 0x1b, 0x70, 1, 10, 250 })]
    public void EncodesOnlyTheEscPosDrawerPulse(int pin, byte onUnits, byte offUnits,
        byte[] expected)
    {
        Assert.Equal(expected, new CashDrawerPulse(pin, onUnits, offUnits).ToEscPosBytes());
    }

    [Theory]
    [InlineData(1, 50, 50)]
    [InlineData(3, 50, 50)]
    [InlineData(2, 0, 50)]
    [InlineData(2, 50, 0)]
    public void RejectsUnknownPinAndZeroDuration(int pin, byte onUnits, byte offUnits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CashDrawerPulse(pin, onUnits, offUnits));
    }
}
