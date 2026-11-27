using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Credits.Ports;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.Credits;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Credits.Services;

/// <summary>
/// Configura la fecha sugerida para créditos futuros. La fecha de cada crédito sigue siendo obligatoria
/// y la valida el flujo de ventas; este valor nunca modifica créditos existentes.
/// </summary>
public sealed class CreditSettingsService(
    ISalesRepository sales,
    ISalesTransactionManager transactionManager,
    IUserRepository users,
    IAuditRepository audits,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICustomerCreditExposureReader exposureReader,
    ICustomerCreditStages stages) : ICreditSettingsService
{
    // Decisión D1 del 23/11/2026: hasta 180 días o 6 meses calendario (antes 90 días o 3 meses).
    private const int MaximumDays = 180;
    private const int MaximumMonths = 6;

    public async Task<Result<CreditSettingsDto>> GetAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<CreditSettingsDto>(error);
        var installation = await sales.GetActiveInstallationAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<CreditSettingsDto>(ApplicationError.NotFound("la instalación activa"));
        var establishment = await sales.GetActiveEstablishmentAsync(cancellationToken);
        return establishment is null || establishment.Id != installation.EstablecimientoId
            ? Result.Failure<CreditSettingsDto>(ApplicationError.NotFound("el establecimiento activo"))
            : Result.Success(Map(establishment));
    }

    public async Task<Result<CreditRulesDto>> GetSalesRulesAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<CreditRulesDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.CreditRequest, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<CreditRulesDto>(ApplicationError.Forbidden());
        var installation = await sales.GetActiveInstallationAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<CreditRulesDto>(ApplicationError.NotFound("la instalación activa"));
        var establishment = await sales.GetActiveEstablishmentAsync(cancellationToken);
        return establishment is null || establishment.Id != installation.EstablecimientoId
            ? Result.Failure<CreditRulesDto>(ApplicationError.NotFound("el establecimiento activo"))
            : Result.Success(new CreditRulesDto(establishment.DiasCreditoPredeterminado,
                establishment.CreditoBloquearRiesgo, establishment.MesesCreditoPredeterminado));
    }

    public async Task<Result<CreditSettingsDto>> UpdateAsync(
        ActorContext actor, UpdateCreditSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var invalid = Validate(request);
        if (invalid is not null) return Result.Failure<CreditSettingsDto>(invalid);

        await using var transaction = await transactionManager.BeginAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<CreditSettingsDto>(ApplicationError.NotFound("la instalación activa"));
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<CreditSettingsDto>(error);
        var establishment = await sales.GetActiveEstablishmentForUpdateAsync(cancellationToken);
        if (establishment is null || establishment.Id != installation.EstablecimientoId)
            return Result.Failure<CreditSettingsDto>(ApplicationError.NotFound("el establecimiento activo"));
        if (establishment.Version != request.ExpectedVersion)
            return Result.Failure<CreditSettingsDto>(
                ApplicationError.ConcurrencyConflict("La configuración fue modificada."));

        var previous = new
        {
            DefaultDays = establishment.DiasCreditoPredeterminado,
            DefaultMonths = establishment.MesesCreditoPredeterminado,
            BlockRiskyCredit = establishment.CreditoBloquearRiesgo
        };
        establishment.DiasCreditoPredeterminado = request.DefaultDays;
        establishment.MesesCreditoPredeterminado = request.DefaultMonths;
        if (request.BlockRiskyCredit is { } block) establishment.CreditoBloquearRiesgo = block;
        establishment.FechaModificacion = clock.UtcNow.UtcDateTime;
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
            AuditAction.CreditSettingsUpdated, nameof(Establecimiento), establishment.Id,
            new
            {
                Previous = previous,
                New = new { request.DefaultDays, request.DefaultMonths, BlockRiskyCredit = establishment.CreditoBloquearRiesgo }
            },
            Guid.CreateVersion7(), null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(establishment));
    }

    public async Task<Result<CustomerCreditRiskDto>> GetCustomerRiskAsync(
        ActorContext actor, Guid customerId, decimal amount, CancellationToken cancellationToken = default)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<CustomerCreditRiskDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.CreditRequest, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<CustomerCreditRiskDto>(ApplicationError.Forbidden());
        if (customerId == Guid.Empty || amount < 0 || decimal.Truncate(amount) != amount)
            return Result.Failure<CustomerCreditRiskDto>(ApplicationError.Validation(
                "Indique el cliente y un valor en pesos enteros no negativo."));
        var establishment = await sales.GetActiveEstablishmentAsync(cancellationToken);
        if (establishment is null)
            return Result.Failure<CustomerCreditRiskDto>(ApplicationError.NotFound("el establecimiento activo"));
        var customer = await sales.GetCustomerAsync(customerId, cancellationToken);
        if (customer is null) return Result.Failure<CustomerCreditRiskDto>(ApplicationError.NotFound("el cliente"));

        var today = LocalToday(establishment, clock);
        var exposure = await exposureReader.GetAsync(customerId, today, cancellationToken);
        var risk = CreditRiskPolicy.Assess(exposure.OutstandingBalance, exposure.OverdueBalance, customer.CupoCredito, amount);
        return Result.Success(new CustomerCreditRiskDto(customerId, exposure.OutstandingBalance, exposure.OverdueBalance,
            exposure.OverdueCredits, customer.CupoCredito, risk.AvailableCredit, risk.Overdue, risk.ExceedsLimit,
            establishment.CreditoBloquearRiesgo, risk.HasRisk && establishment.CreditoBloquearRiesgo,
            CustomerCreditStagePolicy.Resolve(exposure.OutstandingBalance, exposure.OverdueBalance, exposure.NextDueOn, today),
            exposure.NextDueOn, exposure.OldestOverdueOn));
    }

    public async Task<Result<CustomerCreditSummaryDto>> GetCustomerSummaryAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<CustomerCreditSummaryDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.CreditRequest, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<CustomerCreditSummaryDto>(ApplicationError.Forbidden());
        var all = await stages.ReadAllAsync(cancellationToken);
        if (all is null) return Result.Failure<CustomerCreditSummaryDto>(ApplicationError.NotFound("el establecimiento activo"));
        var overdue = all.Values.Where(x => x.Stage == CustomerCreditStagePolicy.Overdue).ToArray();
        var dueSoon = all.Values.Where(x => x.Stage == CustomerCreditStagePolicy.DueSoon).ToArray();
        return Result.Success(new CustomerCreditSummaryDto(
            all.Count, all.Values.Sum(x => x.Exposure.OutstandingBalance),
            overdue.Length, overdue.Sum(x => x.Exposure.OverdueBalance),
            dueSoon.Length, dueSoon.Sum(x => x.Exposure.OutstandingBalance)));
    }

    public async Task<Result<IReadOnlyCollection<CustomerCreditStageDto>>> GetCustomerStagesAsync(
        ActorContext actor, IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken = default)
    {
        // Auditoría C.14: quien vende o cobra créditos ve en Clientes si cada uno está al día, por vencer o moroso.
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<IReadOnlyCollection<CustomerCreditStageDto>>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.CreditRequest, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<IReadOnlyCollection<CustomerCreditStageDto>>(ApplicationError.Forbidden());
        var ids = (customerIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length > 100)
            return Result.Failure<IReadOnlyCollection<CustomerCreditStageDto>>(ApplicationError.Validation("Consulte hasta 100 clientes a la vez."));
        var establishment = await sales.GetActiveEstablishmentAsync(cancellationToken);
        if (establishment is null)
            return Result.Failure<IReadOnlyCollection<CustomerCreditStageDto>>(ApplicationError.NotFound("el establecimiento activo"));
        var today = LocalToday(establishment, clock);
        var exposures = await exposureReader.GetManyAsync(ids, today, cancellationToken);
        return Result.Success<IReadOnlyCollection<CustomerCreditStageDto>>(ids.Select(id =>
        {
            var exposure = exposures.TryGetValue(id, out var found) ? found : new CustomerCreditExposure(0, 0, 0);
            return new CustomerCreditStageDto(id,
                CustomerCreditStagePolicy.Resolve(exposure.OutstandingBalance, exposure.OverdueBalance, exposure.NextDueOn, today),
                exposure.OutstandingBalance, exposure.OverdueBalance, exposure.NextDueOn, exposure.OldestOverdueOn);
        }).ToArray());
    }

    /// <summary>Fecha local del establecimiento, la misma que usa Créditos para proyectar VENCIDO.</summary>
    public static DateOnly LocalToday(Establecimiento establishment, IClock clock)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(establishment.ZonaHoraria) ? "America/Bogota" : establishment.ZonaHoraria);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    private async Task<ApplicationError?> RequireAdministratorAsync(
        ActorContext actor, CancellationToken cancellationToken)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        return snapshot is null ? ApplicationError.Unauthorized()
            : snapshot.HighestRoleLevel != RoleLevel.Administrador ||
              !snapshot.PermissionCodes.Contains(PermissionCodes.ConfigurationManage,
                  StringComparer.OrdinalIgnoreCase)
                ? ApplicationError.Forbidden()
                : null;
    }

    private static ApplicationError? Validate(UpdateCreditSettingsRequest? request)
    {
        if (request is null || request.ExpectedVersion <= 0)
            return ApplicationError.Validation("La versión esperada debe ser positiva.");
        if (request.DefaultDays is < 1 or > MaximumDays)
            return ApplicationError.Validation(
                $"El plazo predeterminado debe estar entre 1 y {MaximumDays} días, o vacío para elegir la fecha en cada crédito.");
        if (request.DefaultMonths is < 1 or > MaximumMonths)
            return ApplicationError.Validation(
                $"El plazo predeterminado debe estar entre 1 y {MaximumMonths} meses, o vacío para elegir la fecha en cada crédito.");
        return request.DefaultDays is not null && request.DefaultMonths is not null
            ? ApplicationError.Validation("Elija un plazo predeterminado en días o en meses, no ambos.")
            : null;
    }

    private static CreditSettingsDto Map(Establecimiento establishment) =>
        new(establishment.DiasCreditoPredeterminado, establishment.Version,
            establishment.CreditoBloquearRiesgo, establishment.MesesCreditoPredeterminado);
}
