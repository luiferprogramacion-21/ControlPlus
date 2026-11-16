using ControlPlus.Domain.Credits;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class CreditInstallmentPolicyTests
{
    [Fact]
    public void Build_SinglePayment_PreservesAmountAndDueDate()
    {
        var today = new DateOnly(2026, 11, 16);
        var installment = Assert.Single(CreditInstallmentPolicy.Build(100_000, today, today.AddDays(30), null));
        Assert.Equal(1, installment.Number);
        Assert.Equal(100_000, installment.Amount);
        Assert.Equal(today.AddDays(30), installment.DueOn);
    }

    [Fact]
    public void Build_Installments_SplitsWholePesosAndPreservesTotal()
    {
        var today = new DateOnly(2026, 11, 16);
        var result = CreditInstallmentPolicy.Build(100, today, null,
            [today.AddDays(10), today.AddDays(20), today.AddDays(30)]).ToArray();
        Assert.Equal([34m, 33m, 33m], result.Select(x => x.Amount));
        Assert.Equal(100, result.Sum(x => x.Amount));
    }

    [Fact]
    public void Build_RejectsAmbiguousOrUnorderedPlans()
    {
        var today = new DateOnly(2026, 11, 16);
        Assert.Throws<ArgumentException>(() => CreditInstallmentPolicy.Build(100, today,
            today.AddDays(10), [today.AddDays(10), today.AddDays(20)]));
        Assert.Throws<ArgumentException>(() => CreditInstallmentPolicy.Build(100, today, null,
            [today.AddDays(20), today.AddDays(10)]));
    }

    [Fact]
    public void Status_IsOverdueWhenAnyInstallmentWithBalanceIsPastDue()
    {
        var today = new DateOnly(2026, 11, 16);
        var schedule = new[] { (today.AddDays(-1), 1m), (today.AddDays(30), 99m) };
        Assert.True(CreditStatusPolicy.HasOverdueInstallment(schedule, today));
        Assert.Equal("VENCIDO", CreditStatusPolicy.ResolveOpenState(schedule, today));
    }
}
