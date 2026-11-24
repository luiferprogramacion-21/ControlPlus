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
    public void Build_AcceptsSixCalendarMonthsOnlyWhenThatCreditRuleIsActive()
    {
        // Decisión D1 del 23/11/2026: hasta 180 días, o 6 meses calendario cuando el plazo se configura en meses.
        var grantedOn = new DateOnly(2026, 11, 21);
        var dueOn = new DateOnly(2027, 5, 21);
        Assert.Equal(grantedOn.AddDays(180), Assert.Single(CreditInstallmentPolicy.Build(2_200, grantedOn,
            grantedOn.AddDays(180), null)).DueOn);
        Assert.Throws<ArgumentException>(() => CreditInstallmentPolicy.Build(2_200, grantedOn, dueOn, null));
        var installment = Assert.Single(CreditInstallmentPolicy.Build(2_200, grantedOn, dueOn, null, 6));
        Assert.Equal(dueOn, installment.DueOn);
        Assert.Throws<ArgumentException>(() => CreditInstallmentPolicy.Build(2_200, grantedOn,
            dueOn.AddDays(1), null, 6));
    }

    [Fact]
    public void Build_ClampsThreeMonthLimitToLastDayOfDestinationMonth()
    {
        var grantedOn = new DateOnly(2026, 1, 31);
        var dueOn = new DateOnly(2026, 4, 30);
        Assert.Equal(dueOn, Assert.Single(CreditInstallmentPolicy.Build(2_200, grantedOn,
            dueOn, null, 3)).DueOn);
        Assert.Throws<ArgumentException>(() => CreditInstallmentPolicy.Build(2_200, grantedOn,
            dueOn.AddDays(1), null, 3));
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
