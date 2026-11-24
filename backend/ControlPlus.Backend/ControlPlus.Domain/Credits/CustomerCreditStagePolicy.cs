namespace ControlPlus.Domain.Credits;

/// <summary>
/// Etapa del crédito de un cliente para mostrarla con su color (auditoría del 23 de noviembre de 2026, C.14): sin deuda,
/// al día, por vencer (la próxima cuota con saldo vence en <see cref="DueSoonDays"/> días o menos) o vencido (moroso, con
/// la regla de <see cref="CreditStatusPolicy"/>). Solo informa: el aviso y el bloqueo de créditos nuevos siguen en
/// <see cref="CreditRiskPolicy"/>.
/// </summary>
public static class CustomerCreditStagePolicy
{
    public const string NoDebt = "SIN_DEUDA";
    public const string Current = "AL_DIA";
    public const string DueSoon = "POR_VENCER";
    public const string Overdue = "VENCIDO";

    /// <summary>Mismo margen que «Apartados próximos» del Dashboard.</summary>
    public const int DueSoonDays = 3;

    public static string Resolve(decimal outstandingBalance, decimal overdueBalance, DateOnly? nextDueOn, DateOnly today)
    {
        if (outstandingBalance <= 0) return NoDebt;
        if (overdueBalance > 0) return Overdue;
        return nextDueOn is { } due && due.DayNumber - today.DayNumber <= DueSoonDays ? DueSoon : Current;
    }
}
