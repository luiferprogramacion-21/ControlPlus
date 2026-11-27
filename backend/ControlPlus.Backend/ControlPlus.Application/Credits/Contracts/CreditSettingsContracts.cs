using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Credits.Contracts;

/// <summary>
/// Plazo predeterminado de los créditos del establecimiento activo y, desde el 16 de noviembre de 2026, si vender a
/// crédito a un moroso o por encima del cupo se bloquea (con excepción de un Administrador) o solo avisa.
/// </summary>
public sealed record CreditSettingsDto(int? DefaultDays, long Version, bool BlockRiskyCredit = false,
    int? DefaultMonths = null);

/// <summary>Reglas que necesita quien prepara una venta a crédito.</summary>
public sealed record CreditRulesDto(int? DefaultDays, bool BlockRiskyCredit = false,
    int? DefaultMonths = null);

/// <summary><c>BlockRiskyCredit</c> es opcional: si no se envía, se conserva el valor actual.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCreditSettingsRequest(
    [property: JsonRequired] int? DefaultDays,
    [property: JsonRequired] long ExpectedVersion,
    bool? BlockRiskyCredit = null,
    [property: JsonRequired] int? DefaultMonths = null);

/// <summary>
/// Situación de crédito de un cliente frente a una venta nueva. <c>Blocked</c> indica que, con el bloqueo activo,
/// la venta exige la autorización de un Administrador (un Administrador que vende la autoriza él mismo).
/// </summary>
public sealed record CustomerCreditRiskDto(
    Guid CustomerId,
    decimal OutstandingBalance,
    decimal OverdueBalance,
    int OverdueCredits,
    decimal? CreditLimit,
    decimal? AvailableCredit,
    bool Overdue,
    bool ExceedsLimit,
    bool BlockRiskyCredit,
    bool Blocked,
    // Auditoría C.14: SIN_DEUDA, AL_DIA, POR_VENCER o VENCIDO (CustomerCreditStagePolicy), con sus fechas clave.
    string Stage = "SIN_DEUDA",
    DateOnly? NextDueOn = null,
    DateOnly? OldestOverdueOn = null);

/// <summary>Etapa del crédito de un cliente para mostrarla con su color en la lista de Clientes (auditoría C.14).</summary>
public sealed record CustomerCreditStageDto(
    Guid CustomerId,
    string Stage,
    decimal OutstandingBalance,
    decimal OverdueBalance,
    DateOnly? NextDueOn,
    DateOnly? OldestOverdueOn);

/// <summary>
/// Resumen de Clientes (pruebas del usuario del 26/11/2026): cuántos deben y cuánto, cuántos están morosos y cuánto tienen
/// vencido, y cuántos vencen en los próximos días y cuánto deben.
/// </summary>
public sealed record CustomerCreditSummaryDto(
    int Debtors, decimal DebtTotal, int Overdue, decimal OverdueTotal, int DueSoon, decimal DueSoonTotal);

public interface ICreditSettingsService
{
    Task<Result<CreditSettingsDto>> GetAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<CreditRulesDto>> GetSalesRulesAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<CreditSettingsDto>> UpdateAsync(
        ActorContext actor, UpdateCreditSettingsRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<CustomerCreditRiskDto>> GetCustomerRiskAsync(
        ActorContext actor, Guid customerId, decimal amount,
        CancellationToken cancellationToken = default);

    /// <summary>Resumen de los clientes que deben, para la parte de arriba de Clientes.</summary>
    Task<Result<CustomerCreditSummaryDto>> GetCustomerSummaryAsync(ActorContext actor, CancellationToken cancellationToken = default);

    /// <summary>Etapa del crédito de hasta 100 clientes; los que no tienen deuda vienen como SIN_DEUDA.</summary>
    Task<Result<IReadOnlyCollection<CustomerCreditStageDto>>> GetCustomerStagesAsync(
        ActorContext actor, IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken = default);
}
