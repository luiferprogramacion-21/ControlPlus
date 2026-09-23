using ControlPlus.Domain.Common;

namespace ControlPlus.Domain.OfficialModel;

public static class CashConstants
{
    public const string IndividualMode = "INDIVIDUAL";
    public const string SharedMode = "COMPARTIDO";
    public const string OpenState = "ABIERTA";
    public const string ClosedState = "CERRADA";
    public const string ActiveSessionState = "ACTIVA";
    public const string ClosedSessionState = "CERRADA";
    public const string OpeningMovement = "APERTURA";
    public const string IncomeMovement = "INGRESO";
    public const string ExpenseMovement = "EGRESO";
    public const string ManualIncomeCategory = "INGRESO_MANUAL";
    public const string ExpenseCategory = "GASTO";
    public const string CashDropCategory = "SANGRIA";
    public const string CashDifferenceReasonType = "DIFERENCIA_CIERRE";
    public const string CashDifferenceAuthorizationType = "CIERRE_CAJA_DIFERENCIA";
}

public partial class Caja
{
    public static Caja Create(Guid installationId, string code, string name, DateTimeOffset createdAtUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        InstalacionId = DomainGuard.RequiredId(installationId, nameof(installationId)),
        Codigo = DomainGuard.NormalizeCode(code, nameof(code)),
        Nombre = DomainGuard.RequiredText(name, nameof(name)),
        Activo = true,
        FechaCreacion = DomainGuard.Utc(createdAtUtc, nameof(createdAtUtc)).UtcDateTime,
        Version = 1
    };
}

public partial class Establecimiento
{
    public void ChangeCashShiftMode(string mode, DateTimeOffset changedAtUtc)
    {
        var normalized = DomainGuard.NormalizeCode(mode, nameof(mode));
        if (normalized is not CashConstants.IndividualMode and not CashConstants.SharedMode)
        {
            throw new DomainRuleViolationException("Cash shift mode must be INDIVIDUAL or COMPARTIDO.");
        }

        ModoTurnoPredeterminado = normalized;
        FechaModificacion = DomainGuard.Utc(changedAtUtc, nameof(changedAtUtc)).UtcDateTime;
    }
}

public partial class TurnoCaja
{
    public bool IsOpen => Estado == CashConstants.OpenState;

    public static TurnoCaja Open(
        Guid cashRegisterId,
        Guid terminalId,
        Guid openingUserId,
        string mode,
        DateOnly operatingDate,
        decimal initialAmount,
        string? observations,
        DateTimeOffset openedAtUtc)
    {
        CashMoney.Require(initialAmount, allowNegative: false);

        var normalizedMode = DomainGuard.NormalizeCode(mode, nameof(mode));
        if (normalizedMode is not CashConstants.IndividualMode and not CashConstants.SharedMode)
        {
            throw new DomainRuleViolationException("Cash shift mode must be INDIVIDUAL or COMPARTIDO.");
        }

        return new TurnoCaja
        {
            Id = Guid.CreateVersion7(),
            CajaId = DomainGuard.RequiredId(cashRegisterId, nameof(cashRegisterId)),
            TerminalId = DomainGuard.RequiredId(terminalId, nameof(terminalId)),
            ModoOperacion = normalizedMode,
            UsuarioAperturaId = DomainGuard.RequiredId(openingUserId, nameof(openingUserId)),
            UsuarioResponsableId = normalizedMode == CashConstants.IndividualMode ? openingUserId : null,
            FechaOperativa = operatingDate,
            FechaHoraApertura = DomainGuard.Utc(openedAtUtc, nameof(openedAtUtc)).UtcDateTime,
            MontoInicial = initialAmount,
            Estado = CashConstants.OpenState,
            Observaciones = NormalizeOptional(observations),
            Version = 1
        };
    }

    public void Close(
        Guid closingUserId,
        decimal cashExpected,
        decimal cashCounted,
        decimal totalDifference,
        Guid? differenceReasonId,
        Guid? authorizationId,
        string? differenceObservation,
        DateTimeOffset closedAtUtc)
    {
        if (!IsOpen) throw new DomainRuleViolationException("Only an open cash shift can be closed.");
        CashMoney.Require(cashExpected, allowNegative: false);
        CashMoney.Require(cashCounted, allowNegative: false);
        CashMoney.Require(totalDifference);
        var cashDifference = CashMoney.Subtract(cashCounted, cashExpected);
        if (totalDifference == 0 && (differenceReasonId is not null || authorizationId is not null))
            throw new DomainRuleViolationException("A balanced closing cannot include a difference reason or authorization.");
        if (totalDifference != 0 && (differenceReasonId is null || authorizationId is null))
            throw new DomainRuleViolationException("A difference reason and authorization are required for an unbalanced closing.");

        UsuarioCierreId = DomainGuard.RequiredId(closingUserId, nameof(closingUserId));
        MotivoDiferenciaId = differenceReasonId;
        FechaHoraCierre = DomainGuard.Utc(closedAtUtc, nameof(closedAtUtc)).UtcDateTime;
        EfectivoEsperado = cashExpected;
        EfectivoContado = cashCounted;
        Diferencia = cashDifference;
        DiferenciaTotal = totalDifference;
        Estado = CashConstants.ClosedState;
        ObservacionDiferencia = NormalizeOptional(differenceObservation);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public partial class MovimientoCaja
{
    public static MovimientoCaja Create(
        Guid shiftId,
        Guid userId,
        Guid? operatorSessionId,
        string type,
        string category,
        decimal amount,
        string? concept,
        Guid correlationId,
        DateTimeOffset occurredAtUtc)
    {
        CashMoney.Require(amount, allowNegative: false);
        if (amount == 0) throw new CashMoneyException();

        return new MovimientoCaja
        {
            Id = Guid.CreateVersion7(),
            TurnoCajaId = DomainGuard.RequiredId(shiftId, nameof(shiftId)),
            UsuarioId = DomainGuard.RequiredId(userId, nameof(userId)),
            SesionOperadorId = DomainGuard.OptionalId(operatorSessionId, nameof(operatorSessionId)),
            Tipo = DomainGuard.NormalizeCode(type, nameof(type)),
            CategoriaMovimiento = DomainGuard.NormalizeCode(category, nameof(category)),
            Valor = amount,
            Concepto = string.IsNullOrWhiteSpace(concept) ? null : concept.Trim(),
            FechaHora = DomainGuard.Utc(occurredAtUtc, nameof(occurredAtUtc)).UtcDateTime,
            CorrelacionId = DomainGuard.RequiredId(correlationId, nameof(correlationId))
        };
    }
}

public partial class CredencialUsuario
{
    public void Revoke(Guid revokedByUserId, DateTimeOffset revokedAtUtc, string reason)
    {
        Estado = "REVOCADA";
        RevocadaPorId = DomainGuard.RequiredId(revokedByUserId, nameof(revokedByUserId));
        FechaRevocacion = DomainGuard.Utc(revokedAtUtc, nameof(revokedAtUtc)).UtcDateTime;
        MotivoRevocacion = DomainGuard.RequiredText(reason, nameof(reason));
    }
}

public partial class SesionOperador
{
    public bool IsActive => Estado == CashConstants.ActiveSessionState;

    public void Close(DateTimeOffset closedAtUtc, string reason)
    {
        if (!IsActive) throw new DomainRuleViolationException("Only an active operator session can be closed.");
        FechaUltimoUso = DomainGuard.Utc(closedAtUtc, nameof(closedAtUtc)).UtcDateTime;
        FechaFin = FechaUltimoUso;
        Estado = CashConstants.ClosedSessionState;
        MotivoCierre = DomainGuard.NormalizeCode(reason, nameof(reason));
    }
}
