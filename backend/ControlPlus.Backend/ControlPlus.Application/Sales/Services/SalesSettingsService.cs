using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Sales.Services;

/// <summary>
/// Configura cuántas unidades de un mismo producto exige el precio mayorista en ventas, apartados y cambios
/// (decisión del 15 de noviembre de 2026). Solo el Administrador vigente con CONFIGURATION.MANAGE lo cambia;
/// el valor nunca modifica documentos ya emitidos.
/// </summary>
public sealed class SalesSettingsService(
    ISalesRepository sales,
    ISalesTransactionManager transactionManager,
    IUserRepository users,
    IAuditRepository audits,
    IUnitOfWork unitOfWork,
    IClock clock) : ISalesSettingsService
{
    public const int MinimumAllowed = 1;
    public const int MaximumAllowed = 999;

    // Quien vende o cambia productos (SALES.CREATE) o aparta (LAYAWAY.MANAGE) necesita el mínimo para avisar antes.
    private static readonly string[] PricingRulesPermissions = [PermissionCodes.SalesCreate, PermissionCodes.LayawayManage];

    public async Task<Result<SalesSettingsDto>> GetAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<SalesSettingsDto>(error);
        var establishment = await ActiveEstablishmentAsync(cancellationToken);
        return establishment is null
            ? Result.Failure<SalesSettingsDto>(ApplicationError.NotFound("el establecimiento activo"))
            : Result.Success(Map(establishment, await LimitsAsync(false, cancellationToken)));
    }

    public async Task<Result<SalesPricingRulesDto>> GetPricingRulesAsync(ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<SalesPricingRulesDto>(ApplicationError.Unauthorized());
        if (!PricingRulesPermissions.Any(code => snapshot.PermissionCodes.Contains(code, StringComparer.OrdinalIgnoreCase)))
            return Result.Failure<SalesPricingRulesDto>(ApplicationError.Forbidden());
        var establishment = await ActiveEstablishmentAsync(cancellationToken);
        return establishment is null
            ? Result.Failure<SalesPricingRulesDto>(ApplicationError.NotFound("el establecimiento activo"))
            : Result.Success(PricingRules(establishment, await LimitsAsync(false, cancellationToken)));
    }

    public async Task<Result<SalesSettingsDto>> UpdateAsync(ActorContext actor, UpdateSalesSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.ExpectedVersion <= 0)
            return Result.Failure<SalesSettingsDto>(ApplicationError.Validation("La versión esperada debe ser positiva."));
        if (request.WholesaleMinimumQuantity is < MinimumAllowed or > MaximumAllowed)
            return Result.Failure<SalesSettingsDto>(ApplicationError.Validation(
                $"El mínimo de unidades para precio mayorista debe estar entre {MinimumAllowed} y {MaximumAllowed}."));
        foreach (var (value, who) in new[] { (request.CashierMaxDiscountPercent, "del Cajero"), (request.SupervisorMaxDiscountPercent, "del Supervisor") })
            if (value is { } percent && (percent < 0 || percent > 100 || decimal.Round(percent, 2) != percent))
                return Result.Failure<SalesSettingsDto>(ApplicationError.Validation(
                    $"El descuento máximo {who} debe estar entre 0 y 100 %, con hasta dos decimales."));

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<SalesSettingsDto>(ApplicationError.NotFound("la instalación activa"));
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<SalesSettingsDto>(error);
        var establishment = await sales.GetActiveEstablishmentForUpdateAsync(cancellationToken);
        if (establishment is null || establishment.Id != installation.EstablecimientoId)
            return Result.Failure<SalesSettingsDto>(ApplicationError.NotFound("el establecimiento activo"));
        if (establishment.Version != request.ExpectedVersion)
            return Result.Failure<SalesSettingsDto>(ApplicationError.ConcurrencyConflict("La configuración fue modificada."));

        var previous = establishment.MinimoUnidadesMayorista;
        var limits = await LimitsAsync(true, cancellationToken);
        var previousLimits = new { Cashier = Limit(limits, "CAJERO", 5m), Supervisor = Limit(limits, "SUPERVISOR", 20m) };
        var now = clock.UtcNow.UtcDateTime;
        SetLimit(limits, "CAJERO", request.CashierMaxDiscountPercent, now);
        SetLimit(limits, "SUPERVISOR", request.SupervisorMaxDiscountPercent, now);
        establishment.MinimoUnidadesMayorista = request.WholesaleMinimumQuantity;
        establishment.FechaModificacion = now;
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
            AuditAction.SalesSettingsUpdated, nameof(Establecimiento), establishment.Id,
            new
            {
                Previous = new { WholesaleMinimumQuantity = previous, CashierMaxDiscountPercent = previousLimits.Cashier, SupervisorMaxDiscountPercent = previousLimits.Supervisor },
                New = new { request.WholesaleMinimumQuantity, CashierMaxDiscountPercent = Limit(limits, "CAJERO", 5m), SupervisorMaxDiscountPercent = Limit(limits, "SUPERVISOR", 20m) }
            },
            Guid.CreateVersion7(), null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(establishment, limits));
    }

    private async Task<Establecimiento?> ActiveEstablishmentAsync(CancellationToken cancellationToken)
    {
        var installation = await sales.GetActiveInstallationAsync(cancellationToken);
        if (installation is null) return null;
        var establishment = await sales.GetActiveEstablishmentAsync(cancellationToken);
        return establishment is null || establishment.Id != installation.EstablecimientoId ? null : establishment;
    }

    private async Task<ApplicationError?> RequireAdministratorAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        return snapshot is null ? ApplicationError.Unauthorized()
            : snapshot.HighestRoleLevel != RoleLevel.Administrador ||
              !snapshot.PermissionCodes.Contains(PermissionCodes.ConfigurationManage, StringComparer.OrdinalIgnoreCase)
                ? ApplicationError.Forbidden()
                : null;
    }

    private async Task<IReadOnlyCollection<LimiteOperacionRol>> LimitsAsync(bool forUpdate, CancellationToken cancellationToken) =>
        await sales.GetDiscountLimitsAsync(forUpdate, cancellationToken);

    private static decimal Limit(IReadOnlyCollection<LimiteOperacionRol> limits, string role, decimal fallback) =>
        limits.Where(x => string.Equals(x.Rol?.Codigo, role, StringComparison.OrdinalIgnoreCase))
            .Select(x => (decimal?)x.ValorMaximo).FirstOrDefault() ?? fallback;

    /// <summary>Cambia el límite de un rol si se envió y existe su fila (sembrada con la instalación).</summary>
    private static void SetLimit(IReadOnlyCollection<LimiteOperacionRol> limits, string role, decimal? value, DateTime now)
    {
        if (value is not { } percent) return;
        var row = limits.FirstOrDefault(x => string.Equals(x.Rol?.Codigo, role, StringComparison.OrdinalIgnoreCase));
        if (row is null || row.ValorMaximo == percent) return;
        row.ValorMaximo = percent;
        row.FechaModificacion = now;
    }

    private static SalesSettingsDto Map(Establecimiento establishment, IReadOnlyCollection<LimiteOperacionRol> limits) =>
        new(establishment.MinimoUnidadesMayorista, establishment.Version,
            Limit(limits, "CAJERO", 5m), Limit(limits, "SUPERVISOR", 20m));

    private static SalesPricingRulesDto PricingRules(Establecimiento establishment, IReadOnlyCollection<LimiteOperacionRol> limits) =>
        new(establishment.MinimoUnidadesMayorista, Limit(limits, "CAJERO", 5m), Limit(limits, "SUPERVISOR", 20m),
            Limit(limits, "ADMINISTRADOR", 100m));
}
