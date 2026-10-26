namespace ControlPlus.Domain.Sales;

public static class SaleChangeAvailability
{
    public static bool IsWithinDeadline(DateOnly currentLocalDate, DateOnly deadline) =>
        currentLocalDate <= deadline;

    public static int RemainingQuantity(int soldQuantity, int returnedQuantity)
    {
        if (soldQuantity < 1 || returnedQuantity < 0 || returnedQuantity > soldQuantity)
            throw new ArgumentOutOfRangeException(nameof(returnedQuantity));
        return soldQuantity - returnedQuantity;
    }

    public static decimal HistoricalReturnValue(decimal unitPrice, decimal lineDiscount,
        int soldQuantity, int previouslyReturned, int returning)
    {
        if (soldQuantity < 1 || previouslyReturned < 0 || returning < 1 ||
            previouslyReturned > soldQuantity - returning ||
            unitPrice < 0 || lineDiscount < 0 || lineDiscount > unitPrice * soldQuantity ||
            decimal.Truncate(unitPrice) != unitPrice || decimal.Truncate(lineDiscount) != lineDiscount)
            throw new ArgumentOutOfRangeException(nameof(returning));
        var historicalNet = checked(unitPrice * soldQuantity - lineDiscount);
        var perItemNet = decimal.Truncate(historicalNet / soldQuantity);
        var finalRemainder = previouslyReturned + returning == soldQuantity
            ? historicalNet - perItemNet * soldQuantity : 0m;
        return checked(perItemNet * returning + finalRemainder);
    }
}
