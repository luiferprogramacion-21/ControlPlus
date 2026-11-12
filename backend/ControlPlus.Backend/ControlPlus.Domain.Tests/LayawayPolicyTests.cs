using ControlPlus.Domain.Common;
using ControlPlus.Domain.Sales;

namespace ControlPlus.Domain.Tests;

public sealed class LayawayPolicyTests
{
    [Fact]
    public void IncludedTax_PreservesRoundingAndAcceptsRepresentableLimits()
    {
        var tax = LayawayPolicy.CalculateIncludedTax(1001m, 19m);
        Assert.Equal(841m, tax.Base);
        Assert.Equal(160m, tax.Tax);
        Assert.Equal((99999999999999m, 0m), LayawayPolicy.CalculateIncludedTax(99999999999999m, 0m));
        Assert.Equal(99999999999999.9999m,
            LayawayPolicy.SumTaxAmounts([99999999999999m, 0.9999m]));
        Assert.Equal((null, null), LayawayPolicy.CalculateIncludedTax(999999999999999999m, null));
    }

    [Fact]
    public void IncludedTax_RejectsLineOverflowEvenWhenWholeMoneyIsValid()
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            LayawayPolicy.CalculateIncludedTax(100000000000000m, 0m));
        Assert.Throws<DomainRuleViolationException>(() =>
            LayawayPolicy.CalculateIncludedTax(200000000000000m, 100m));
    }

    [Fact]
    public void TaxAccumulation_RejectsOverflowOfIndividuallyValidLines()
    {
        var first = LayawayPolicy.CalculateIncludedTax(60000000000000m, 0m);
        var second = LayawayPolicy.CalculateIncludedTax(60000000000000m, 0m);
        Assert.Throws<DomainRuleViolationException>(() =>
            LayawayPolicy.SumTaxAmounts([first.Base!.Value, second.Base!.Value]));
        Assert.Throws<DomainRuleViolationException>(() =>
            LayawayPolicy.SumTaxAmounts([99999999999999.9999m, 0.0001m]));
    }

    [Theory]
    [InlineData(1, 20, 1)]
    [InlineData(101, 20, 21)]
    [InlineData(505, 2.5, 13)]
    [InlineData(100, 100, 100)]
    public void MinimumInitialPayment_RoundsUpToWholePeso(
        decimal total, decimal percentage, decimal expected) =>
        Assert.Equal(expected, LayawayPolicy.MinimumInitialPayment(total, percentage));

    [Fact]
    public void ConfiguredDays_DefaultsToMaximumAndRejectsLaterDate()
    {
        var operatingDate = new DateOnly(2026, 10, 18);
        Assert.Equal(new DateOnly(2026, 11, 2),
            LayawayPolicy.ResolveDueDate(operatingDate, 15, null));
        Assert.Equal(new DateOnly(2026, 10, 19),
            LayawayPolicy.ResolveDueDate(operatingDate, 15, new DateOnly(2026, 10, 19)));
        Assert.Throws<DomainRuleViolationException>(() =>
            LayawayPolicy.ResolveDueDate(operatingDate, 15, new DateOnly(2026, 11, 3)));
    }

    [Fact]
    public void NullConfiguredDays_RequiresExplicitFutureDate()
    {
        var operatingDate = new DateOnly(2026, 10, 18);
        Assert.Throws<DomainRuleViolationException>(() =>
            LayawayPolicy.ResolveDueDate(operatingDate, null, null));
        Assert.Throws<DomainRuleViolationException>(() =>
            LayawayPolicy.ResolveDueDate(operatingDate, null, operatingDate));
        Assert.Equal(new DateOnly(2027, 1, 1),
            LayawayPolicy.ResolveDueDate(operatingDate, null, new DateOnly(2027, 1, 1)));
    }
}
