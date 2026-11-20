using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Establishments.Contracts;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Establishments.Services;

/// <summary>
/// Mantiene los datos públicos del único establecimiento de V1. Cualquier sesión vigente puede leerlos para mostrar
/// el nombre correcto; solo el Administrador con CONFIGURATION.MANAGE puede modificarlos.
/// </summary>
public sealed class EstablishmentService(
    ISalesRepository sales,
    ISalesTransactionManager transactionManager,
    IUserRepository users,
    IAuditRepository audits,
    IUnitOfWork unitOfWork,
    IClock clock) : IEstablishmentService
{
    private const int MaximumHeaderLength = 1_000;
    private const int MaximumFooterLength = 1_000;

    public async Task<Result<EstablishmentDto>> GetAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        if (await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken) is null)
            return Result.Failure<EstablishmentDto>(ApplicationError.Unauthorized());

        var establishment = await ActiveEstablishmentAsync(cancellationToken);
        return establishment is null
            ? Result.Failure<EstablishmentDto>(ApplicationError.NotFound("el establecimiento activo"))
            : Result.Success(Map(establishment));
    }

    public async Task<Result<EstablishmentDto>> UpdateAsync(
        ActorContext actor, UpdateEstablishmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (validation is not null) return Result.Failure<EstablishmentDto>(validation);

        await using var transaction = await transactionManager.BeginAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<EstablishmentDto>(ApplicationError.NotFound("la instalación activa"));

        var authorization = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (authorization is null) return Result.Failure<EstablishmentDto>(ApplicationError.Unauthorized());
        if (authorization.HighestRoleLevel != RoleLevel.Administrador ||
            !authorization.PermissionCodes.Contains(PermissionCodes.ConfigurationManage,
                StringComparer.OrdinalIgnoreCase))
            return Result.Failure<EstablishmentDto>(ApplicationError.Forbidden());

        var establishment = await sales.GetActiveEstablishmentForUpdateAsync(cancellationToken);
        if (establishment is null || establishment.Id != installation.EstablecimientoId)
            return Result.Failure<EstablishmentDto>(ApplicationError.NotFound("el establecimiento activo"));
        if (establishment.Version != request.ExpectedVersion)
            return Result.Failure<EstablishmentDto>(
                ApplicationError.ConcurrencyConflict("Los datos del establecimiento fueron modificados."));

        var previous = Map(establishment);
        establishment.NombreComercial = request.CommercialName.Trim();
        establishment.Identificacion = Optional(request.Identification);
        establishment.Telefono = Optional(request.Phone);
        establishment.Direccion = Optional(request.Address);
        establishment.EncabezadoComprobante = Optional(request.ReceiptHeader);
        establishment.PieComprobante = Optional(request.ReceiptFooter);
        establishment.FechaModificacion = clock.UtcNow.UtcDateTime;

        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
            AuditAction.EstablishmentUpdated, nameof(Establecimiento), establishment.Id,
            new
            {
                Previous = PublicAuditData(previous),
                New = PublicAuditData(Map(establishment))
            },
            Guid.CreateVersion7(), null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(establishment));
    }

    private async Task<Establecimiento?> ActiveEstablishmentAsync(CancellationToken cancellationToken)
    {
        var installation = await sales.GetActiveInstallationAsync(cancellationToken);
        if (installation is null) return null;
        var establishment = await sales.GetActiveEstablishmentAsync(cancellationToken);
        return establishment is null || establishment.Id != installation.EstablecimientoId
            ? null
            : establishment;
    }

    private static ApplicationError? Validate(UpdateEstablishmentRequest? request)
    {
        if (request is null || request.ExpectedVersion <= 0)
            return ApplicationError.Validation("La versión esperada debe ser positiva.");
        if (InvalidRequired(request.CommercialName, 200))
            return ApplicationError.Validation("El nombre comercial es obligatorio y admite hasta 200 caracteres.");
        if (InvalidOptional(request.Identification, 30))
            return ApplicationError.Validation("El NIT o documento admite hasta 30 caracteres.");
        if (InvalidOptional(request.Phone, 30))
            return ApplicationError.Validation("El teléfono admite hasta 30 caracteres.");
        if (InvalidOptional(request.Address, 300))
            return ApplicationError.Validation("La dirección admite hasta 300 caracteres.");
        if (InvalidMultiline(request.ReceiptHeader, MaximumHeaderLength))
            return ApplicationError.Validation($"El encabezado del comprobante admite hasta {MaximumHeaderLength} caracteres.");
        return InvalidMultiline(request.ReceiptFooter, MaximumFooterLength)
            ? ApplicationError.Validation($"El pie del comprobante admite hasta {MaximumFooterLength} caracteres.")
            : null;
    }

    private static bool InvalidRequired(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum || value.Any(char.IsControl);

    private static bool InvalidOptional(string? value, int maximum) =>
        value is not null && (value.Trim().Length > maximum || value.Any(char.IsControl));

    private static bool InvalidMultiline(string? value, int maximum) =>
        value is not null && (value.Trim().Length > maximum || value.Any(InvalidMultilineControl));

    private static bool InvalidMultilineControl(char value) =>
        char.IsControl(value) && value is not '\r' and not '\n' and not '\t';

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EstablishmentDto Map(Establecimiento establishment) =>
        new(establishment.NombreComercial, establishment.Identificacion, establishment.Telefono,
            establishment.Direccion, establishment.EncabezadoComprobante,
            establishment.PieComprobante, establishment.Version);

    private static object PublicAuditData(EstablishmentDto establishment) => new
    {
        establishment.CommercialName,
        establishment.Identification,
        establishment.Phone,
        establishment.Address,
        establishment.ReceiptHeader,
        establishment.ReceiptFooter
    };
}
