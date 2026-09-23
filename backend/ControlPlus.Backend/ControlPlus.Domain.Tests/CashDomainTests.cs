using ControlPlus.Domain.Common;
using ControlPlus.Domain.OfficialModel;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class CashDomainTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 9, 15, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1)]
    [InlineData(0.5)]
    public void Open_RejectsNegativeOrFractionalInitialMoney(decimal amount) =>
        Assert.Throws<CashMoneyException>(() => TurnoCaja.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CashConstants.IndividualMode, new DateOnly(2026, 9, 15), amount, null, UtcNow));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.5)]
    public void Movement_RejectsNonPositiveOrFractionalMoney(decimal amount) =>
        Assert.Throws<CashMoneyException>(() => MovimientoCaja.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            CashConstants.IncomeMovement, CashConstants.ManualIncomeCategory,
            amount, "Prueba", Guid.CreateVersion7(), UtcNow));

    [Fact]
    public void Open_AssignsResponsibleUserOnlyToIndividualShift()
    {
        var userId = Guid.CreateVersion7();
        var individual = TurnoCaja.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), userId,
            CashConstants.IndividualMode, new DateOnly(2026, 9, 15), 10_000m, null, UtcNow);
        var shared = TurnoCaja.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), userId,
            CashConstants.SharedMode, new DateOnly(2026, 9, 15), 10_000m, null, UtcNow);

        Assert.Equal(userId, individual.UsuarioResponsableId);
        Assert.Null(shared.UsuarioResponsableId);
    }

    [Fact]
    public void Close_WithDifferenceRequiresReasonAndAuthorization()
    {
        var shift = TurnoCaja.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CashConstants.IndividualMode, new DateOnly(2026, 9, 15), 10_000m, null, UtcNow);

        Assert.Throws<DomainRuleViolationException>(() => shift.Close(
            Guid.CreateVersion7(), 10_000m, 9_000m, -1_000m, null, null, "Faltante", UtcNow.AddHours(8)));

        shift.Close(
            Guid.CreateVersion7(), 10_000m, 9_000m, -1_000m,
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Faltante", UtcNow.AddHours(8));
        Assert.Equal(CashConstants.ClosedState, shift.Estado);
        Assert.Equal(-1_000m, shift.Diferencia);
        Assert.Equal(-1_000m, shift.DiferenciaTotal);
    }

    [Fact]
    public void Close_BalancedShiftRejectsDifferenceMetadata()
    {
        var shift = TurnoCaja.Open(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CashConstants.IndividualMode, new DateOnly(2026, 9, 15), 10_000m, null, UtcNow);

        Assert.Throws<DomainRuleViolationException>(() => shift.Close(
            Guid.CreateVersion7(), 10_000m, 10_000m, 0m,
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, UtcNow.AddHours(8)));
    }

    [Theory]
    [InlineData(1000, 1000, 200)]
    [InlineData(1000, 1000, -200)]
    [InlineData(1000, 1200, 0)]
    [InlineData(1000, 800, 0)]
    public void Close_KeepsCashDifferenceIndependentOfTotal(decimal expected, decimal counted, decimal totalDifference)
    {
        var shift = TurnoCaja.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CashConstants.IndividualMode, new DateOnly(2026, 9, 15), 1000m, null, UtcNow);
        shift.Close(Guid.CreateVersion7(), expected, counted, totalDifference,
            totalDifference == 0 ? null : Guid.CreateVersion7(),
            totalDifference == 0 ? null : Guid.CreateVersion7(), null, UtcNow.AddHours(8));
        Assert.Equal(counted - expected, shift.Diferencia);
        Assert.Equal(totalDifference, shift.DiferenciaTotal);
    }

    [Fact]
    public void Money_EnforcesBothSignedNumericLimitsAndCheckedAccumulation()
    {
        Assert.Equal(CashMoney.Maximum, CashMoney.Require(CashMoney.Maximum));
        Assert.Equal(-CashMoney.Maximum, CashMoney.Require(-CashMoney.Maximum));
        Assert.Equal(CashMoney.Maximum, CashMoney.Subtract(CashMoney.Maximum, 0m));
        Assert.Equal(-CashMoney.Maximum, CashMoney.Subtract(0m, CashMoney.Maximum));
        Assert.Throws<CashMoneyException>(() => CashMoney.Require(CashMoney.Maximum + 1m));
        Assert.Throws<CashMoneyException>(() => CashMoney.Require(-CashMoney.Maximum - 1m));
        Assert.Throws<CashMoneyException>(() => CashMoney.Sum([CashMoney.Maximum, 1m]));
        Assert.Throws<CashMoneyException>(() => CashMoney.Subtract(CashMoney.Maximum, -1m));
        Assert.Throws<CashMoneyException>(() => CashMoney.Subtract(-CashMoney.Maximum, 1m));
        Assert.Throws<CashMoneyException>(() => CashMoney.Require(decimal.MaxValue));
        Assert.Throws<CashMoneyException>(() => CashMoney.Require(-1m, allowNegative: false));
    }

    [Fact]
    public void Close_RejectsOutOfRangeBeforeChangingShift()
    {
        var shift = TurnoCaja.Open(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CashConstants.IndividualMode, new DateOnly(2026, 9, 15), CashMoney.Maximum, null, UtcNow);
        Assert.Throws<CashMoneyException>(() => shift.Close(Guid.CreateVersion7(), CashMoney.Maximum,
            CashMoney.Maximum + 1m, 1m, Guid.CreateVersion7(), Guid.CreateVersion7(), null, UtcNow));
        Assert.True(shift.IsOpen);
        Assert.Null(shift.EfectivoContado);
        Assert.Null(shift.Diferencia);
        Assert.Null(shift.DiferenciaTotal);
    }
}
