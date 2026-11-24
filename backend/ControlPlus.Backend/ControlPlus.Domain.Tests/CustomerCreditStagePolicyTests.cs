using ControlPlus.Domain.Credits;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class CustomerCreditStagePolicyTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    [Fact]
    public void Resolve_GivesEachStageItsOwnRule()
    {
        // Auditoría C.14: sin deuda, al día, por vencer (3 días o menos) y vencido.
        Assert.Equal("SIN_DEUDA", CustomerCreditStagePolicy.Resolve(0, 0, null, Today));
        Assert.Equal("AL_DIA", CustomerCreditStagePolicy.Resolve(50_000, 0, Today.AddDays(4), Today));
        Assert.Equal("POR_VENCER", CustomerCreditStagePolicy.Resolve(50_000, 0, Today.AddDays(3), Today));
        Assert.Equal("POR_VENCER", CustomerCreditStagePolicy.Resolve(50_000, 0, Today, Today));
        Assert.Equal("VENCIDO", CustomerCreditStagePolicy.Resolve(50_000, 10_000, Today.AddDays(1), Today));
    }

    [Fact]
    public void Resolve_WithBalanceButNoPendingDate_IsCurrent() =>
        Assert.Equal("AL_DIA", CustomerCreditStagePolicy.Resolve(50_000, 0, null, Today));
}
