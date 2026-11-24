namespace ControlPlus.Domain.Credits;

public sealed record PlannedCreditInstallment(int Number, DateOnly DueOn, decimal Amount);

public static class CreditInstallmentPolicy
{
    public const int MaximumInstallments = 90;
    // Decisión D1 del 23/11/2026: un crédito puede durar hasta 6 meses calendario o 180 días.
    public const int MaximumCalendarMonths = 6;
    public const int MaximumDays = 180;

    public static IReadOnlyCollection<PlannedCreditInstallment> Build(decimal total,
        DateOnly operatingDate, DateOnly? singleDueDate, IReadOnlyCollection<DateOnly>? installmentDueDates,
        int? maximumCalendarMonths = null)
    {
        if (maximumCalendarMonths is < 1 or > MaximumCalendarMonths)
            throw new ArgumentOutOfRangeException(nameof(maximumCalendarMonths));
        var maximumDueDate = maximumCalendarMonths is { } months
            ? operatingDate.AddMonths(months)
            : operatingDate.AddDays(MaximumDays);
        var dates = installmentDueDates?.ToArray() ?? [];
        if (dates.Length == 0)
        {
            if (singleDueDate is null) throw new ArgumentException("La fecha de vencimiento es obligatoria.");
            dates = [singleDueDate.Value];
        }
        else if (singleDueDate is not null)
            throw new ArgumentException("No mezcle el vencimiento único con las fechas de cuotas.");

        if (dates.Length > MaximumInstallments || dates.Length > total ||
            dates.Any(x => x <= operatingDate || x > maximumDueDate) ||
            dates.Zip(dates.Skip(1)).Any(x => x.First >= x.Second))
            throw new ArgumentException(maximumCalendarMonths is null
                ? $"La fecha de vencimiento debe estar entre mañana y {MaximumDays} días, sin superar el total en pesos."
                : $"Las cuotas deben tener fechas crecientes entre mañana y {maximumCalendarMonths} meses calendario, sin superar el total en pesos.");

        var wholeTotal = decimal.Truncate(total);
        if (wholeTotal != total || total <= 0) throw new ArgumentException("El total debe ser un valor COP entero positivo.");
        var baseAmount = decimal.Floor(total / dates.Length);
        var remainder = (int)(total - baseAmount * dates.Length);
        return dates.Select((date, index) => new PlannedCreditInstallment(index + 1, date,
            baseAmount + (index < remainder ? 1 : 0))).ToArray();
    }
}
