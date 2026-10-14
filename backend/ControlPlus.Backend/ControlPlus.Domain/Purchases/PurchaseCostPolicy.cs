namespace ControlPlus.Domain.Purchases;

/// <summary>Decimal purchase arithmetic matching PostgreSQL numeric round (half away from zero).</summary>
public static class PurchaseCostPolicy
{
    public const decimal MaximumFourPlaceAmount = 99999999999999.9999m;
    public const decimal MaximumTwoPlaceAmount = 9999999999999999.99m;

    public static decimal Round4(decimal amount) => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
    public static decimal Round2(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal EffectiveUnitCost(decimal unitCost, string vatMode, decimal? vatPercentage) =>
        vatMode == "SEPARADO"
            ? unitCost + unitCost * vatPercentage!.Value / 100m
            : unitCost;

    public static decimal Average(decimal? previousAverage, int previousStock,
        int receivedQuantity, decimal effectiveUnitCost)
    {
        var result = previousStock == 0 || previousAverage is null
            ? effectiveUnitCost
            : ((previousStock * previousAverage.Value) + (receivedQuantity * effectiveUnitCost)) /
              checked(previousStock + receivedQuantity);
        return CheckedFour(Round4(result));
    }

    public static decimal CheckedFour(decimal amount) => amount is < 0m or > MaximumFourPlaceAmount
        ? throw new OverflowException("El costo excede numeric(18,4).")
        : amount;

    public static decimal CheckedTwo(decimal amount) => amount is < 0m or > MaximumTwoPlaceAmount
        ? throw new OverflowException("El importe excede numeric(18,2).")
        : amount;
}
