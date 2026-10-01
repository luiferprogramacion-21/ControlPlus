using ControlPlus.Domain.Common;

namespace ControlPlus.Domain.Inventory;

public sealed record InventoryAdjustmentResult(
    string MovementType,
    int PreviousStock,
    int NewStock,
    int ReservedStock);

public static class InventoryAdjustmentPolicy
{
    public static InventoryAdjustmentResult Apply(
        bool productIsActive,
        int currentStock,
        int reservedStock,
        int quantityDelta)
    {
        if (!productIsActive)
            throw new DomainRuleViolationException("Solo se puede ajustar un producto activo.");
        if (quantityDelta == 0)
            throw new DomainRuleViolationException("La cantidad del ajuste debe ser un entero distinto de cero.");
        if (currentStock < 0 || reservedStock < 0 || reservedStock > currentStock)
            throw new DomainRuleViolationException("El producto no conserva saldos de inventario validos.");

        int newStock;
        try
        {
            newStock = checked(currentStock + quantityDelta);
        }
        catch (OverflowException exception)
        {
            throw new DomainRuleViolationException("El ajuste excede el rango entero permitido.", exception);
        }

        if (newStock < 0)
            throw new DomainRuleViolationException("El ajuste no puede dejar existencias negativas.");
        if (reservedStock > newStock)
            throw new DomainRuleViolationException("El ajuste no puede dejar existencias por debajo de la cantidad reservada.");

        return new InventoryAdjustmentResult(
            quantityDelta > 0 ? "AJUSTE_ENTRADA" : "AJUSTE_SALIDA",
            currentStock,
            newStock,
            reservedStock);
    }
}
