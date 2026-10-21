using ControlPlus.Domain.Common;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Domain.Sales;

public static class LayawayPolicy
{
    // The official detail and header tax amounts use numeric(18,4).
    private const decimal MaximumTaxAmount = 99999999999999.9999m;

    public static (decimal? Base, decimal? Tax) CalculateIncludedTax(decimal total, decimal? percentage)
    {
        CashMoney.Require(total, allowNegative: false);
        if (percentage is null) return (null, null);
        if (percentage is < 0 or > 100 || decimal.Round(percentage.Value, 2) != percentage.Value)
            throw new DomainRuleViolationException("El IVA debe ser un porcentaje válido con hasta dos decimales.");

        var basis = decimal.Round(total * 100m / (100m + percentage.Value), 4,
            MidpointRounding.AwayFromZero);
        return (RequireTaxAmount(basis), RequireTaxAmount(total - basis));
    }

    public static decimal SumTaxAmounts(IEnumerable<decimal> amounts)
    {
        var total = 0m;
        foreach (var amount in amounts)
            total = RequireTaxAmount(total + RequireTaxAmount(amount));
        return total;
    }

    private static decimal RequireTaxAmount(decimal amount)
    {
        if (amount < 0 || amount > MaximumTaxAmount || decimal.Round(amount, 4) != amount)
            throw new DomainRuleViolationException("La base gravable o el IVA del apartado excede el rango permitido.");
        return amount;
    }

    public static DateOnly ResolveDueDate(DateOnly operatingDate, int? configuredDays, DateOnly? requestedDate)
    {
        if (configuredDays is null)
        {
            if (requestedDate is not DateOnly date || date <= operatingDate)
                throw new DomainRuleViolationException("Debe indicar una fecha límite futura para el apartado.");
            return date;
        }

        if (configuredDays <= 0)
            throw new DomainRuleViolationException("El plazo configurado debe ser positivo.");
        DateOnly maximumDate;
        try { maximumDate = operatingDate.AddDays(configuredDays.Value); }
        catch (ArgumentOutOfRangeException)
        {
            throw new DomainRuleViolationException("El plazo configurado está fuera del rango admitido.");
        }
        var resolved = requestedDate ?? maximumDate;
        if (resolved <= operatingDate || resolved > maximumDate)
            throw new DomainRuleViolationException("La fecha límite debe ser futura y no superar el plazo configurado.");
        return resolved;
    }

    public static decimal MinimumInitialPayment(decimal total, decimal percentage)
    {
        CashMoney.Require(total, allowNegative: false);
        if (total <= 0 || percentage is <= 0 or > 100)
            throw new DomainRuleViolationException("El total y el porcentaje mínimo deben ser positivos y válidos.");
        return decimal.Ceiling(total * percentage / 100m);
    }
}
