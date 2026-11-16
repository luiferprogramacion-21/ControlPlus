namespace ControlPlus.Domain.Credits;

/// <summary>Resultado de evaluar si un cliente puede recibir un crédito nuevo sin aviso.</summary>
/// <param name="Overdue">Tiene saldo vencido según <see cref="CreditStatusPolicy"/>.</param>
/// <param name="ExceedsLimit">El saldo pendiente más el crédito nuevo supera su cupo.</param>
/// <param name="AvailableCredit">Cupo restante antes del crédito nuevo; NULL si el cliente no tiene cupo.</param>
public sealed record CreditRiskAssessment(bool Overdue, bool ExceedsLimit, decimal? AvailableCredit)
{
    public bool HasRisk => Overdue || ExceedsLimit;
}

/// <summary>
/// Decisión del 16 de noviembre de 2026: vender a crédito a un moroso o por encima del cupo del cliente siempre avisa;
/// si el establecimiento activó el bloqueo, exige la autorización de un Administrador.
/// </summary>
public static class CreditRiskPolicy
{
    public static CreditRiskAssessment Assess(decimal outstandingBalance, decimal overdueBalance, decimal? creditLimit,
        decimal newCreditAmount) =>
        new(overdueBalance > 0,
            creditLimit is { } limit && outstandingBalance + newCreditAmount > limit,
            creditLimit is { } available ? Math.Max(0m, available - outstandingBalance) : null);
}
