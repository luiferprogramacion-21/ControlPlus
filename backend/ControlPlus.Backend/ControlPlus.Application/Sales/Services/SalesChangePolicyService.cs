using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Sales.Services;

public sealed partial class SalesService
{
    public async Task<Result<SaleChangePolicyDto>> GetChangePolicyAsync(ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<SaleChangePolicyDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.ConfigurationManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleChangePolicyDto>(ApplicationError.Forbidden());
        var establishment = await repository.GetActiveEstablishmentAsync(cancellationToken);
        return establishment is null
            ? Result.Failure<SaleChangePolicyDto>(ApplicationError.NotFound("el establecimiento activo"))
            : Result.Success(new SaleChangePolicyDto(establishment.DiasLimiteCambio, establishment.Version));
    }

    public async Task<Result<SaleChangePolicyDto>> UpdateChangePolicyAsync(ActorContext actor,
        UpdateSaleChangePolicyRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.Days <= 0 || request.ExpectedVersion <= 0)
            return Result.Failure<SaleChangePolicyDto>(Validation("El plazo y la versión deben ser positivos."));
        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted,
            cancellationToken);
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<SaleChangePolicyDto>(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<SaleChangePolicyDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.ConfigurationManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleChangePolicyDto>(ApplicationError.Forbidden());
        var establishment = await repository.GetActiveEstablishmentForUpdateAsync(cancellationToken);
        if (establishment is null || establishment.Id != installation.EstablecimientoId)
            return Result.Failure<SaleChangePolicyDto>(ApplicationError.NotFound("el establecimiento activo"));
        if (establishment.Version != request.ExpectedVersion)
            return Result.Failure<SaleChangePolicyDto>(ApplicationError.ConcurrencyConflict(
                "La configuración fue modificada."));
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(establishment.ZonaHoraria)).DateTime);
        if (request.Days > DateOnly.MaxValue.DayNumber - localDate.DayNumber)
            return Result.Failure<SaleChangePolicyDto>(Validation("El plazo excede el rango de fechas disponible."));
        var previousDays = establishment.DiasLimiteCambio;
        establishment.DiasLimiteCambio = request.Days;
        establishment.FechaModificacion = clock.UtcNow.UtcDateTime;
        var correlation = Guid.CreateVersion7();
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId,
            AuditAction.SaleChangePolicyUpdated, nameof(Establecimiento), establishment.Id,
            new { PreviousDays = previousDays, NewDays = request.Days }, correlation,
            null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(new SaleChangePolicyDto(establishment.DiasLimiteCambio,
            establishment.Version));
    }
}
