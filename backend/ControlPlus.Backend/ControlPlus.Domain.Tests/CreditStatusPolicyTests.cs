using ControlPlus.Domain.Credits;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class CreditStatusPolicyTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    [Theory]
    [InlineData("2026-11-15", 1, true)]
    [InlineData("2026-11-16", 1, false)]
    [InlineData("2026-11-17", 1, false)]
    [InlineData("2026-11-15", 0, false)]
    public void IsOverdue_UsesOutstandingBalanceAndStrictlyPastDueDate(
        string dueOn, decimal balance, bool expected)
    {
        Assert.Equal(expected, CreditStatusPolicy.IsOverdue(
            DateOnly.ParseExact(dueOn, "yyyy-MM-dd"), balance, Today));
    }

    [Fact]
    public void ResolveOpenState_DoesNotAddMonetaryLateFees()
    {
        const decimal outstanding = 25_000m;
        Assert.Equal("VENCIDO", CreditStatusPolicy.ResolveOpenState(
            Today.AddDays(-1), outstanding, Today));
        Assert.Equal(25_000m, outstanding);
    }
}
