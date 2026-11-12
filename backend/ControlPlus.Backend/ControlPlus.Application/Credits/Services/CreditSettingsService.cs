using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
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
    IClock clock) : ICreditSettingsService
{
    private const int MaximumDays = 90;

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
            : Result.Success(new CreditRulesDto(establishment.DiasCreditoPredeterminado));
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

        var previous = new { DefaultDays = establishment.DiasCreditoPredeterminado };
        establishment.DiasCreditoPredeterminado = request.DefaultDays;
        establishment.FechaModificacion = clock.UtcNow.UtcDateTime;
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
            AuditAction.CreditSettingsUpdated, nameof(Establecimiento), establishment.Id,
            new
            {
                Previous = previous,
                New = new { request.DefaultDays }
            },
            Guid.CreateVersion7(), null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(establishment));
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
        return request.DefaultDays is < 1 or > MaximumDays
            ? ApplicationError.Validation(
                $"El plazo predeterminado debe estar entre 1 y {MaximumDays} días, o vacío para elegir la fecha en cada crédito.")
            : null;
    }

    private static CreditSettingsDto Map(Establecimiento establishment) =>
        new(establishment.DiasCreditoPredeterminado, establishment.Version);
}
