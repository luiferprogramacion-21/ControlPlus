using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Sales;

public sealed record DiscountShare(Guid StableKey, decimal ExactAmount);

public static class SalePricingPolicy
{
    /// <summary>
    /// Initial minimum of units of the same product for the wholesale price. Since 15 November 2026 the effective value is
    /// configured per establishment (configuracion.establecimiento.minimo_unidades_mayorista) and this is only its default.
    /// </summary>
    public const int WholesaleMinimumQuantity = 12;

    public static decimal MaximumDiscountPercentage(RoleLevel role) => role switch
    {
        RoleLevel.Cajero => 5m,
        RoleLevel.Supervisor => 20m,
        RoleLevel.Administrador => 100m, // D7 (23/11/2026): sin porcentaje máximo; lo limita el costo.
        _ => 0m
    };

    public static decimal RoundCop(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    public static void EnsureDiscountWithinLimit(
        decimal grossAmount,
        decimal discountAmount,
        decimal maximumPercentage)
    {
        if (grossAmount <= 0 || discountAmount <= 0 || discountAmount > grossAmount)
            throw new DomainRuleViolationException("El descuento debe ser positivo y no puede superar el valor bruto.");
        if (maximumPercentage is < 0 or > 100)
            throw new DomainRuleViolationException("El límite de descuento debe estar entre cero y 100 por ciento.");
        if (discountAmount * 100m > grossAmount * maximumPercentage)
            throw new DomainRuleViolationException("El descuento supera el límite aplicable al rol.");
    }

    public static IReadOnlyDictionary<Guid, decimal> AllocateByLargestRemainder(
        decimal roundedTotal,
        IReadOnlyCollection<DiscountShare> shares)
    {
        if (roundedTotal < 0 || decimal.Truncate(roundedTotal) != roundedTotal)
            throw new DomainRuleViolationException("El descuento distribuido debe ser un valor entero en COP.");
        if (shares.Count == 0 || shares.Any(x => x.ExactAmount < 0))
            throw new DomainRuleViolationException("Se requiere al menos una participación de descuento no negativa.");

        var allocations = shares
            .Select(x => new Allocation(x.StableKey, decimal.Floor(x.ExactAmount), x.ExactAmount - decimal.Floor(x.ExactAmount)))
            .ToArray();
        var pending = checked((int)(roundedTotal - allocations.Sum(x => x.WholeAmount)));
        if (pending < 0 || pending > allocations.Length)
            throw new DomainRuleViolationException("El descuento redondeado no se puede distribuir con las participaciones indicadas.");

        foreach (var allocation in allocations
                     .OrderByDescending(x => x.Remainder)
                     .ThenBy(x => x.StableKey)
                     .Take(pending))
        {
            allocation.WholeAmount += 1m;
        }

        return allocations.ToDictionary(x => x.StableKey, x => x.WholeAmount);
    }

    public static void EnsureNotBelowCost(decimal netLineAmount, decimal unitCost, int quantity)
    {
        if (unitCost < 0 || quantity <= 0 || netLineAmount < unitCost * quantity)
            throw new DomainRuleViolationException("No se permite vender por debajo del costo.");
    }

    private sealed class Allocation(Guid stableKey, decimal wholeAmount, decimal remainder)
    {
        public Guid StableKey { get; } = stableKey;
        public decimal WholeAmount { get; set; } = wholeAmount;
        public decimal Remainder { get; } = remainder;
    }
}
