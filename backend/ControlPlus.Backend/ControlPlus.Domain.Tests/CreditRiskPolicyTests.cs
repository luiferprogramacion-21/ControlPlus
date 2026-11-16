using ControlPlus.Domain.Credits;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class CreditRiskPolicyTests
{
    [Fact]
    public void CustomerUpToDateAndWithoutLimit_HasNoRisk()
    {
        var risk = CreditRiskPolicy.Assess(5_000m, 0m, null, 100_000m);
        Assert.False(risk.HasRisk);
        Assert.Null(risk.AvailableCredit);
    }

    [Fact]
    public void AnyOverdueBalance_IsRisk()
    {
        var risk = CreditRiskPolicy.Assess(5_000m, 1m, null, 1_000m);
        Assert.True(risk.Overdue);
        Assert.False(risk.ExceedsLimit);
        Assert.True(risk.HasRisk);
    }

    [Theory]
    [InlineData(1_000, 500, 1_500, false, 500)]
    [InlineData(1_000, 501, 1_500, true, 500)]
    [InlineData(2_000, 1, 1_500, true, 0)]
    public void Limit_IsExceededOnlyWhenOutstandingPlusNewCreditGoesOverIt(
        int outstanding, int newCredit, int limit, bool exceeds, int available)
    {
        var risk = CreditRiskPolicy.Assess(outstanding, 0m, limit, newCredit);
        Assert.Equal(exceeds, risk.ExceedsLimit);
        Assert.Equal(available, risk.AvailableCredit);
        Assert.False(risk.Overdue);
    }
}
