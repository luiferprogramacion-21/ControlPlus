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
    public async Task<Result<VatSettingDto>> GetVatSettingAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<VatSettingDto>(ApplicationError.Unauthorized());
        if (snapshot.HighestRoleLevel != RoleLevel.Administrador ||
            !snapshot.PermissionCodes.Contains(PermissionCodes.TaxesManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<VatSettingDto>(ApplicationError.Forbidden());
        var installation = await repository.GetActiveInstallationAsync(cancellationToken);
        if (installation is null) return Result.Failure<VatSettingDto>(ApplicationError.NotFound("la instalación activa"));
        var establishment = await repository.GetActiveEstablishmentAsync(cancellationToken);
        return establishment is null || establishment.Id != installation.EstablecimientoId
            ? Result.Failure<VatSettingDto>(ApplicationError.NotFound("el establecimiento activo"))
            : Result.Success(MapVat(establishment));
    }

    public async Task<Result<VatSettingDto>> UpdateVatSettingAsync(ActorContext actor,
        UpdateVatSettingRequest request, CancellationToken cancellationToken = default)
    {
        // Decisión del 18 de noviembre de 2026: el IVA se quitó del sistema y no se puede volver a encender.
        if (request is { Enabled: true })
            return Result.Failure<VatSettingDto>(Validation("El IVA no se usa en ControlPlus: se quitó del sistema."));
        if (request is null || request.ExpectedVersion < 0 ||
            request.Percentage is decimal percentage && (percentage <= 0 || percentage > 100 ||
                decimal.Round(percentage, 2) != percentage) ||
            request.Enabled && request.Percentage is null)
            return Result.Failure<VatSettingDto>(Validation("El porcentaje de IVA debe tener hasta dos decimales y estar entre 0 y 100 cuando está habilitado."));
        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Result.Failure<VatSettingDto>(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<VatSettingDto>(ApplicationError.Unauthorized());
        if (snapshot.HighestRoleLevel != RoleLevel.Administrador ||
            !snapshot.PermissionCodes.Contains(PermissionCodes.TaxesManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<VatSettingDto>(ApplicationError.Forbidden());
        var establishment = await repository.GetActiveEstablishmentForUpdateAsync(cancellationToken);
        if (establishment is null || establishment.Id != installation.EstablecimientoId)
            return Result.Failure<VatSettingDto>(ApplicationError.NotFound("el establecimiento activo"));
        if (establishment.VatVersion != request.ExpectedVersion)
            return Result.Failure<VatSettingDto>(ApplicationError.ConcurrencyConflict("La configuración de IVA fue modificada."));
        var previous = MapVat(establishment);
        establishment.VatEnabled = request.Enabled;
        establishment.VatPercentage = request.Percentage ?? (request.Enabled ? null : establishment.VatPercentage);
        establishment.VatVersion++;
        establishment.VatUpdatedAtUtc = clock.UtcNow.UtcDateTime;
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId,
            AuditAction.VatSettingUpdated, nameof(Establecimiento), establishment.Id,
            new { PreviousEnabled = previous.Enabled, PreviousPercentage = previous.Percentage,
                NewEnabled = establishment.VatEnabled, NewPercentage = establishment.VatPercentage },
            Guid.CreateVersion7(), null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(MapVat(establishment));
    }

    private static VatSettingDto MapVat(Establecimiento e) =>
        new(e.VatEnabled, e.VatPercentage, e.VatVersion, e.VatUpdatedAtUtc);
}
