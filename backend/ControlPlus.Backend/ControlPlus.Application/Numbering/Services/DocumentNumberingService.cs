using System.Data;
using ControlPlus.Application.Common;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Numbering.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Numbering.Services;

public sealed class DocumentNumberingService(
    IDocumentNumberingRepository repository,
    IDocumentNumberingTransactionManager transactionManager,
    IPermissionChecker permissionChecker,
    IAuditRepository auditRepository,
    IUnitOfWork unitOfWork,
    IClock clock) : IDocumentNumberingService
{
    private const string SalesDocumentType = "VENTA";

    public async Task<Result<SalesDocumentNumberingDto>> GetCreditPaymentsAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<SalesDocumentNumberingDto>(error);
        var installation = await repository.GetActiveInstallationAsync(false, cancellationToken);
        if (installation is null) return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("la instalación activa"));
        var numbering = await repository.GetCreditPaymentNumberingAsync(installation.Id, installation.Serie, false, cancellationToken);
        return numbering is null
            ? Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("el consecutivo de abonos"))
            : MapResult(numbering);
    }

    public async Task<Result<SalesDocumentNumberingDto>> CreateCreditPaymentsAsync(
        ActorContext actor, CreateSalesDocumentNumberingRequest request, CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<SalesDocumentNumberingDto>(error);
        var input = NormalizeAndValidate(request.Prefix, request.FirstNumber);
        if (input.Error is not null) return Result.Failure<SalesDocumentNumberingDto>(input.Error);
        if (input.Prefix != "ABO")
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Validation("El prefijo aprobado para abonos es ABO."));
        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationAsync(true, cancellationToken);
        if (installation is null) return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("la instalación activa"));
        if (await repository.GetCreditPaymentNumberingAsync(installation.Id, installation.Serie, false, cancellationToken) is not null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Conflict("Ya existe el consecutivo de abonos."));
        var numbering = new ConsecutivoDocumento
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TipoDocumento = "PAGO_CREDITO",
            Prefijo = input.Prefix!, Serie = installation.Serie, UltimoNumero = input.LastNumber, Version = 1
        };
        await repository.AddAsync(numbering, cancellationToken);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId,
            AuditAction.CreditPaymentNumberingCreated, nameof(ConsecutivoDocumento), numbering.Id,
            new { numbering.TipoDocumento, numbering.Serie, numbering.Prefijo, request.FirstNumber }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapResult(numbering);
    }

    public async Task<Result<SalesDocumentNumberingDto>> UpdateCreditPaymentsAsync(
        ActorContext actor, UpdateSalesDocumentNumberingRequest request, CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<SalesDocumentNumberingDto>(error);
        if (request.Version <= 0) return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Validation("La versión debe ser mayor que cero."));
        var input = NormalizeAndValidate(request.Prefix, request.FirstNumber);
        if (input.Error is not null) return Result.Failure<SalesDocumentNumberingDto>(input.Error);
        if (input.Prefix != "ABO")
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Validation("El prefijo aprobado para abonos es ABO."));
        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationAsync(true, cancellationToken);
        if (installation is null) return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("la instalación activa"));
        var numbering = await repository.GetCreditPaymentNumberingAsync(installation.Id, installation.Serie, true, cancellationToken);
        if (numbering is null) return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("el consecutivo de abonos"));
        if (numbering.Version != request.Version) return Result.Failure<SalesDocumentNumberingDto>(VersionConflict);
        if (await repository.HasCreditPaymentsAsync(installation.Id, installation.Serie, cancellationToken))
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Conflict("El consecutivo de abonos no puede corregirse después del primer pago."));
        numbering.Prefijo = input.Prefix!;
        numbering.UltimoNumero = input.LastNumber;
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId,
            AuditAction.CreditPaymentNumberingUpdated, nameof(ConsecutivoDocumento), numbering.Id,
            new { numbering.TipoDocumento, numbering.Serie, numbering.Prefijo, request.FirstNumber }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapResult(numbering);
    }

    public async Task<Result<SalesDocumentNumberingDto>> GetSalesAsync(
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var permissionError = await RequirePermissionAsync(actor, cancellationToken);
        if (permissionError is not null) return Result.Failure<SalesDocumentNumberingDto>(permissionError);

        var installation = await repository.GetActiveInstallationAsync(false, cancellationToken);
        if (installation is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("la instalación activa"));

        var numbering = await repository.GetSalesNumberingAsync(
            installation.Id, installation.Serie, false, cancellationToken);
        return numbering is null
            ? Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("el consecutivo de VENTAS"))
            : MapResult(numbering);
    }

    public async Task<Result<SalesDocumentNumberingDto>> CreateSalesAsync(
        ActorContext actor,
        CreateSalesDocumentNumberingRequest request,
        CancellationToken cancellationToken = default)
    {
        var permissionError = await RequirePermissionAsync(actor, cancellationToken);
        if (permissionError is not null) return Result.Failure<SalesDocumentNumberingDto>(permissionError);
        var input = NormalizeAndValidate(request.Prefix, request.FirstNumber);
        if (input.Error is not null) return Result.Failure<SalesDocumentNumberingDto>(input.Error);

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationAsync(true, cancellationToken);
        if (installation is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("la instalación activa"));

        var existing = await repository.GetSalesNumberingAsync(
            installation.Id, installation.Serie, false, cancellationToken);
        if (existing is not null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Conflict(
                "Ya existe el consecutivo de VENTAS para la instalación y serie activas."));

        var numbering = new ConsecutivoDocumento
        {
            Id = Guid.CreateVersion7(),
            InstalacionId = installation.Id,
            TipoDocumento = SalesDocumentType,
            Prefijo = input.Prefix!,
            Serie = installation.Serie,
            UltimoNumero = input.LastNumber,
            Version = 1
        };

        await repository.AddAsync(numbering, cancellationToken);
        await AuditWriter.WriteAsync(
            auditRepository,
            clock,
            actor.UserId,
            AuditAction.SalesDocumentNumberingCreated,
            nameof(ConsecutivoDocumento),
            numbering.Id,
            new
            {
                numbering.TipoDocumento,
                numbering.Serie,
                numbering.Prefijo,
                PrimerNumero = request.FirstNumber
            },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapResult(numbering);
    }

    public async Task<Result<SalesDocumentNumberingDto>> UpdateSalesAsync(
        ActorContext actor,
        UpdateSalesDocumentNumberingRequest request,
        CancellationToken cancellationToken = default)
    {
        var permissionError = await RequirePermissionAsync(actor, cancellationToken);
        if (permissionError is not null) return Result.Failure<SalesDocumentNumberingDto>(permissionError);
        if (request.Version <= 0)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Validation(
                "La versión debe ser mayor que cero."));
        var input = NormalizeAndValidate(request.Prefix, request.FirstNumber);
        if (input.Error is not null) return Result.Failure<SalesDocumentNumberingDto>(input.Error);

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationAsync(true, cancellationToken);
        if (installation is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("la instalación activa"));

        var numbering = await repository.GetSalesNumberingAsync(
            installation.Id, installation.Serie, true, cancellationToken);
        if (numbering is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("el consecutivo de VENTAS"));
        if (numbering.Version != request.Version)
            return Result.Failure<SalesDocumentNumberingDto>(VersionConflict);
        if (await repository.HasConfirmedSalesAsync(installation.Id, installation.Serie, cancellationToken))
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Conflict(
                "El consecutivo de VENTAS no puede modificarse después de confirmar la primera venta."));

        var previousPrefix = numbering.Prefijo;
        var previousFirstNumber = checked(numbering.UltimoNumero + 1);
        numbering.Prefijo = input.Prefix!;
        numbering.UltimoNumero = input.LastNumber;

        await AuditWriter.WriteAsync(
            auditRepository,
            clock,
            actor.UserId,
            AuditAction.SalesDocumentNumberingUpdated,
            nameof(ConsecutivoDocumento),
            numbering.Id,
            new
            {
                numbering.TipoDocumento,
                numbering.Serie,
                PrefijoAnterior = previousPrefix,
                PrefijoNuevo = numbering.Prefijo,
                PrimerNumeroAnterior = previousFirstNumber,
                PrimerNumeroNuevo = request.FirstNumber
            },
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapResult(numbering);
    }

    private async Task<ApplicationError?> RequirePermissionAsync(
        ActorContext actor,
        CancellationToken cancellationToken) =>
        await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.NumberingManage, cancellationToken)
            ? null
            : ApplicationError.Forbidden();

    private static (string? Prefix, long LastNumber, ApplicationError? Error) NormalizeAndValidate(
        string? prefix,
        long firstNumber)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return (null, 0, ApplicationError.Validation("El prefijo es obligatorio."));

        var normalized = prefix.Trim().ToUpperInvariant();
        if (normalized.Length > 10)
            return (null, 0, ApplicationError.Validation("El prefijo no puede superar 10 caracteres."));
        if (normalized.Any(char.IsControl))
            return (null, 0, ApplicationError.Validation("El prefijo contiene caracteres no válidos."));
        if (firstNumber < 1)
            return (null, 0, ApplicationError.Validation("El primer número debe ser mayor que cero."));

        return (normalized, checked(firstNumber - 1), null);
    }

    private static Result<SalesDocumentNumberingDto> MapResult(ConsecutivoDocumento numbering)
    {
        if (numbering.UltimoNumero == long.MaxValue)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NumberingExhausted(
                numbering.TipoDocumento == "PAGO_CREDITO"
                    ? "El consecutivo de abonos alcanzó el número máximo admitido."
                    : "El consecutivo de VENTAS alcanzó el número máximo admitido."));
        return Result.Success(new SalesDocumentNumberingDto(
            numbering.TipoDocumento,
            numbering.Serie,
            numbering.Prefijo,
            numbering.UltimoNumero,
            numbering.UltimoNumero + 1,
            numbering.Version));
    }

    private static readonly ApplicationError VersionConflict =
        ApplicationError.ConcurrencyConflict(
            "El registro fue modificado por otro usuario. Recargue los datos e intente nuevamente.");
}
