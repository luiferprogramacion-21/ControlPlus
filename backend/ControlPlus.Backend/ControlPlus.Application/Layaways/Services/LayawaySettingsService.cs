using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Layaways.Services;

/// <summary>
/// Reads and updates the layaway parameters stored in the active establishment: the minimum initial
/// payment percentage and the default due-date term. Only changes future layaways; existing ones keep
/// the percentage and date they were created with. Requires CONFIGURATION.MANAGE.
/// </summary>
public sealed class LayawaySettingsService(
    ISalesRepository sales,
    ISalesTransactionManager transactionManager,
    IUserRepository users,
    IAuditRepository audits,
    IUnitOfWork unitOfWork,
    IClock clock) : ILayawaySettingsService
{
    private const int MaximumDays = 365;

    public async Task<Result<LayawaySettingsDto>> GetAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<LayawaySettingsDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.ConfigurationManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<LayawaySettingsDto>(ApplicationError.Forbidden());
        var establishment = await sales.GetActiveEstablishmentAsync(cancellationToken);
        return establishment is null
            ? Result.Failure<LayawaySettingsDto>(ApplicationError.NotFound("el establecimiento activo"))
            : Result.Success(Map(establishment));
    }

    public async Task<Result<LayawaySettingsDto>> UpdateAsync(ActorContext actor, UpdateLayawaySettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var invalid = Validate(request);
        if (invalid is not null) return Result.Failure<LayawaySettingsDto>(invalid);

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Result.Failure<LayawaySettingsDto>(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<LayawaySettingsDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.ConfigurationManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<LayawaySettingsDto>(ApplicationError.Forbidden());
        var establishment = await sales.GetActiveEstablishmentForUpdateAsync(cancellationToken);
        if (establishment is null || establishment.Id != installation.EstablecimientoId)
            return Result.Failure<LayawaySettingsDto>(ApplicationError.NotFound("el establecimiento activo"));
        if (establishment.Version != request.ExpectedVersion)
            return Result.Failure<LayawaySettingsDto>(ApplicationError.ConcurrencyConflict("La configuración fue modificada."));

        var previous = new { Percentage = establishment.PorcentajeMinimoApartado, Days = establishment.DiasLimiteApartado };
        establishment.PorcentajeMinimoApartado = request.MinimumPercentage;
        establishment.DiasLimiteApartado = request.DefaultDays;
        establishment.FechaModificacion = clock.UtcNow.UtcDateTime;
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId, AuditAction.LayawaySettingsUpdated,
            nameof(Establecimiento), establishment.Id,
            new { Previous = previous, New = new { Percentage = request.MinimumPercentage, Days = request.DefaultDays } },
            Guid.CreateVersion7(), null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(establishment));
    }

    private static ApplicationError? Validate(UpdateLayawaySettingsRequest? request)
    {
        if (request is null || request.ExpectedVersion <= 0)
            return ApplicationError.Validation("La versión esperada debe ser positiva.");
        if (request.MinimumPercentage <= 0 || request.MinimumPercentage > 100 ||
            decimal.Round(request.MinimumPercentage, 2) != request.MinimumPercentage)
            return ApplicationError.Validation("El porcentaje mínimo debe ser mayor que cero, no superar 100 y tener hasta dos decimales.");
        if (request.DefaultDays is < 1 or > MaximumDays)
            return ApplicationError.Validation($"El plazo por defecto debe estar entre 1 y {MaximumDays} días, o vacío para elegir la fecha en cada apartado.");
        return null;
    }

    private static LayawaySettingsDto Map(Establecimiento e) => new(e.PorcentajeMinimoApartado, e.DiasLimiteApartado, e.Version);
}
