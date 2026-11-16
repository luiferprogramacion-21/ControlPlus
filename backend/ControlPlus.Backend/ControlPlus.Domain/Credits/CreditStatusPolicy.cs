namespace ControlPlus.Domain.Credits;

/// <summary>
/// Canonical V1 rule for deciding whether an outstanding credit amount is overdue.
/// There is no monetary late fee: a due date only changes the displayed/business state.
/// </summary>
public static class CreditStatusPolicy
{
    public static bool IsOverdue(DateOnly dueOn, decimal outstandingAmount, DateOnly today) =>
        outstandingAmount > 0 && dueOn < today;

    public static string ResolveOpenState(DateOnly dueOn, decimal outstandingAmount, DateOnly today) =>
        IsOverdue(dueOn, outstandingAmount, today) ? "VENCIDO" : "PENDIENTE";
}
