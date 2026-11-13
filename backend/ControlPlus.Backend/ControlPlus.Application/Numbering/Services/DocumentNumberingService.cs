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

public sealed partial class DocumentNumberingService(
    IDocumentNumberingRepository repository,
    IDocumentNumberingTransactionManager transactionManager,
    IPermissionChecker permissionChecker,
    IUserRepository userRepository,
    IAuditRepository auditRepository,
    IUnitOfWork unitOfWork,
    IClock clock) : IDocumentNumberingService
{
    private const string SalesDocumentType = "VENTA";
    private const string PurchaseDocumentType = "COMPRA";
    private const string PurchasePrefix = "CMP";

    public Task<Result<SalesDocumentNumberingDto>> GetLayawaysAsync(
        ActorContext actor, CancellationToken cancellationToken = default) =>
        GetLayawayNumberingAsync(actor, "apartados", DocumentNumberingDefaults.Layaways,
            repository.GetLayawayNumberingAsync, cancellationToken);

    public Task<Result<SalesDocumentNumberingDto>> CreateLayawaysAsync(
        ActorContext actor, CreateLayawayDocumentNumberingRequest request,
        CancellationToken cancellationToken = default) =>
        CreateLayawayNumberingAsync(actor, request, "APARTADO", "APT", "apartados",
            AuditAction.LayawayNumberingCreated, repository.GetLayawayNumberingAsync,
            cancellationToken);

    public Task<Result<SalesDocumentNumberingDto>> GetLayawayPaymentsAsync(
        ActorContext actor, CancellationToken cancellationToken = default) =>
        GetLayawayNumberingAsync(actor, "pagos de apartados", DocumentNumberingDefaults.LayawayPayments,
            repository.GetLayawayPaymentNumberingAsync, cancellationToken);

    public Task<Result<SalesDocumentNumberingDto>> CreateLayawayPaymentsAsync(
        ActorContext actor, CreateLayawayDocumentNumberingRequest request,
        CancellationToken cancellationToken = default) =>
        CreateLayawayNumberingAsync(actor, request, "PAGO_APARTADO", "PAP",
            "pagos de apartados", AuditAction.LayawayPaymentNumberingCreated,
            repository.GetLayawayPaymentNumberingAsync, cancellationToken);

    /// <summary>
    /// Reads a consecutive and, when it does not exist yet, creates it with its fixed prefix (starting at 1)
    /// and reads it again. The backend creates every consecutive by itself, so the Administrator never has to.
    /// </summary>
    private async Task<ConsecutivoDocumento?> GetOrEnsureAsync(
        Guid installationId, string series, string documentType,
        Func<Guid, string, bool, CancellationToken, Task<ConsecutivoDocumento?>> getNumbering,
        CancellationToken cancellationToken)
    {
        var numbering = await getNumbering(installationId, series, false, cancellationToken);
        if (numbering is not null) return numbering;
        await repository.EnsureNumberingAsync(installationId, series, documentType, cancellationToken);
        return await getNumbering(installationId, series, false, cancellationToken);
    }

    private async Task<Result<SalesDocumentNumberingDto>> GetLayawayNumberingAsync(
        ActorContext actor, string label, string documentType,
        Func<Guid, string, bool, CancellationToken, Task<ConsecutivoDocumento?>> getNumbering,
        CancellationToken cancellationToken)
    {
        var error = await RequirePermissionAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<SalesDocumentNumberingDto>(error);

        var installation = await repository.GetActiveInstallationAsync(false, cancellationToken);
        if (installation is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("la instalación activa"));

        var numbering = await GetOrEnsureAsync(installation.Id, installation.Serie, documentType,
            getNumbering, cancellationToken);
        return numbering is null
            ? Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound($"el consecutivo de {label}"))
            : MapResult(numbering);
    }

    private async Task<Result<SalesDocumentNumberingDto>> CreateLayawayNumberingAsync(
        ActorContext actor, CreateLayawayDocumentNumberingRequest request,
        string documentType, string prefix, string label, AuditAction action,
        Func<Guid, string, bool, CancellationToken, Task<ConsecutivoDocumento?>> getNumbering,
        CancellationToken cancellationToken)
    {
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Unauthorized());
        if (snapshot.HighestRoleLevel != RoleLevel.Administrador ||
            !snapshot.PermissionCodes.Contains(PermissionCodes.NumberingManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Forbidden());

        var input = NormalizeAndValidate(request.Prefix, request.FirstNumber);
        if (input.Error is not null) return Result.Failure<SalesDocumentNumberingDto>(input.Error);
        if (input.Prefix != prefix || request.FirstNumber != 1)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Validation(
                $"La numeración inicial de {label} exige prefijo {prefix} y primer número 1."));

        await using var transaction = await transactionManager.BeginAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationAsync(true, cancellationToken);
        if (installation is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("la instalación activa"));
        snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Unauthorized());
        if (snapshot.HighestRoleLevel != RoleLevel.Administrador ||
            !snapshot.PermissionCodes.Contains(PermissionCodes.NumberingManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Forbidden());
        if (await getNumbering(installation.Id, installation.Serie, false, cancellationToken) is not null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.Conflict(
                $"Ya existe el consecutivo de {label} para la instalación y serie activas."));

        var numbering = new ConsecutivoDocumento
        {
            Id = Guid.CreateVersion7(),
            InstalacionId = installation.Id,
            TipoDocumento = documentType,
            Prefijo = prefix,
            Serie = installation.Serie,
            UltimoNumero = 0,
            Version = 1
        };
        await repository.AddAsync(numbering, cancellationToken);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId,
            action, nameof(ConsecutivoDocumento), numbering.Id,
            new { numbering.TipoDocumento, numbering.Serie, numbering.Prefijo,
                PrimerNumero = request.FirstNumber }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapResult(numbering);
    }

    public async Task<Result<PurchaseDocumentNumberingDto>> GetPurchasesAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        var permissionError = await RequirePermissionAsync(actor, cancellationToken);
        if (permissionError is not null)
            return Result.Failure<PurchaseDocumentNumberingDto>(permissionError);

        var installation = await repository.GetActiveInstallationAsync(false, cancellationToken);
        if (installation is null)
            return Result.Failure<PurchaseDocumentNumberingDto>(
                ApplicationError.NotFound("la instalación activa"));

        var numbering = await GetOrEnsureAsync(installation.Id, installation.Serie, PurchaseDocumentType,
            repository.GetPurchaseNumberingAsync, cancellationToken);
        if (numbering is null)
            return Result.Failure<PurchaseDocumentNumberingDto>(
                ApplicationError.NotFound("el consecutivo de compras"));
        var canCorrect = !await repository.HasNumberedPurchasesAsync(
            installation.Id, installation.Serie, cancellationToken);
        return MapPurchaseResult(numbering, canCorrect);
    }

    public async Task<Result<PurchaseDocumentNumberingDto>> CreatePurchasesAsync(
        ActorContext actor, CreatePurchaseDocumentNumberingRequest request,
        CancellationToken cancellationToken = default)
    {
        var permissionError = await RequirePermissionAsync(actor, cancellationToken);
        if (permissionError is not null)
            return Result.Failure<PurchaseDocumentNumberingDto>(permissionError);

        var input = NormalizeAndValidate(request.Prefix, request.FirstNumber);
        if (input.Error is not null)
            return Result.Failure<PurchaseDocumentNumberingDto>(input.Error);
        if (input.Prefix != PurchasePrefix || request.FirstNumber != 1)
            return Result.Failure<PurchaseDocumentNumberingDto>(ApplicationError.Validation(
                "La numeración inicial de compras exige prefijo CMP y primer número 1."));

        await using var transaction = await transactionManager.BeginAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationAsync(true, cancellationToken);
        if (installation is null)
            return Result.Failure<PurchaseDocumentNumberingDto>(
                ApplicationError.NotFound("la instalación activa"));
        if (await repository.GetPurchaseNumberingAsync(
                installation.Id, installation.Serie, false, cancellationToken) is not null)
            return Result.Failure<PurchaseDocumentNumberingDto>(ApplicationError.Conflict(
                "Ya existe el consecutivo de compras para la instalación y serie activas."));

        var numbering = new ConsecutivoDocumento
        {
            Id = Guid.CreateVersion7(),
            InstalacionId = installation.Id,
            TipoDocumento = PurchaseDocumentType,
            Prefijo = PurchasePrefix,
            Serie = installation.Serie,
            UltimoNumero = 0,
            Version = 1
        };
        await repository.AddAsync(numbering, cancellationToken);
        await AuditWriter.WriteAsync(
            auditRepository, clock, actor.UserId,
            AuditAction.PurchaseDocumentNumberingCreated,
            nameof(ConsecutivoDocumento), numbering.Id,
            new { numbering.TipoDocumento, numbering.Serie, numbering.Prefijo,
                PrimerNumero = request.FirstNumber }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapPurchaseResult(numbering, canCorrect: true);
    }

    public async Task<Result<PurchaseDocumentNumberingDto>> UpdatePurchasesAsync(
        ActorContext actor, UpdatePurchaseDocumentNumberingRequest request,
        CancellationToken cancellationToken = default)
    {
        var permissionError = await RequirePermissionAsync(actor, cancellationToken);
        if (permissionError is not null)
            return Result.Failure<PurchaseDocumentNumberingDto>(permissionError);
        if (request.Version <= 0)
            return Result.Failure<PurchaseDocumentNumberingDto>(ApplicationError.Validation(
                "La versión debe ser mayor que cero."));

        var input = NormalizeAndValidate(request.Prefix, request.FirstNumber);
        if (input.Error is not null)
            return Result.Failure<PurchaseDocumentNumberingDto>(input.Error);
        if (input.Prefix != PurchasePrefix)
            return Result.Failure<PurchaseDocumentNumberingDto>(ApplicationError.Validation(
                "El prefijo aprobado para compras es CMP."));

        await using var transaction = await transactionManager.BeginAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationAsync(true, cancellationToken);
        if (installation is null)
            return Result.Failure<PurchaseDocumentNumberingDto>(
                ApplicationError.NotFound("la instalación activa"));
        var numbering = await repository.GetPurchaseNumberingAsync(
            installation.Id, installation.Serie, true, cancellationToken);
        if (numbering is null)
            return Result.Failure<PurchaseDocumentNumberingDto>(
                ApplicationError.NotFound("el consecutivo de compras"));
        if (numbering.Version != request.Version)
            return Result.Failure<PurchaseDocumentNumberingDto>(VersionConflict);
        if (await repository.HasNumberedPurchasesAsync(
                installation.Id, installation.Serie, cancellationToken))
            return Result.Failure<PurchaseDocumentNumberingDto>(ApplicationError.Conflict(
                "El consecutivo de compras no puede corregirse después de la primera compra confirmada."));

        var previousFirstNumber = checked(numbering.UltimoNumero + 1);
        numbering.UltimoNumero = input.LastNumber;
        await AuditWriter.WriteAsync(
            auditRepository, clock, actor.UserId,
            AuditAction.PurchaseDocumentNumberingUpdated,
            nameof(ConsecutivoDocumento), numbering.Id,
            new { numbering.TipoDocumento, numbering.Serie,
                PrimerNumeroAnterior = previousFirstNumber,
                PrimerNumeroNuevo = request.FirstNumber }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapPurchaseResult(numbering, canCorrect: true);
    }

    public async Task<Result<SalesDocumentNumberingDto>> GetCreditPaymentsAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<SalesDocumentNumberingDto>(error);
        var installation = await repository.GetActiveInstallationAsync(false, cancellationToken);
        if (installation is null) return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("la instalación activa"));
        var numbering = await GetOrEnsureAsync(installation.Id, installation.Serie,
            DocumentNumberingDefaults.CreditPayments, repository.GetCreditPaymentNumberingAsync, cancellationToken);
        if (numbering is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("el consecutivo de abonos"));
        var canCorrect = !await repository.HasCreditPaymentsAsync(
            installation.Id, installation.Serie, cancellationToken);
        return MapResult(numbering, canCorrect);
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
        return MapResult(numbering, canCorrect: true);
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
        return MapResult(numbering, canCorrect: true);
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

        var numbering = await GetOrEnsureAsync(installation.Id, installation.Serie, SalesDocumentType,
            repository.GetSalesNumberingAsync, cancellationToken);
        if (numbering is null)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NotFound("el consecutivo de VENTAS"));
        var canCorrect = !await repository.HasConfirmedSalesAsync(
            installation.Id, installation.Serie, cancellationToken);
        return MapResult(numbering, canCorrect);
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
        return MapResult(numbering, canCorrect: true);
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
        return MapResult(numbering, canCorrect: true);
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

    private static Result<SalesDocumentNumberingDto> MapResult(
        ConsecutivoDocumento numbering, bool canCorrect = false)
    {
        if (numbering.UltimoNumero == long.MaxValue)
            return Result.Failure<SalesDocumentNumberingDto>(ApplicationError.NumberingExhausted(
                numbering.TipoDocumento switch
                {
                    "PAGO_CREDITO" => "El consecutivo de abonos alcanzó el número máximo admitido.",
                    "APARTADO" => "El consecutivo de apartados alcanzó el número máximo admitido.",
                    "PAGO_APARTADO" => "El consecutivo de pagos de apartados alcanzó el número máximo admitido.",
                    _ => "El consecutivo de VENTAS alcanzó el número máximo admitido."
                }));
        return Result.Success(new SalesDocumentNumberingDto(
            numbering.TipoDocumento,
            numbering.Serie,
            numbering.Prefijo,
            numbering.UltimoNumero,
            numbering.UltimoNumero + 1,
            numbering.Version,
            canCorrect));
    }

    private static Result<PurchaseDocumentNumberingDto> MapPurchaseResult(
        ConsecutivoDocumento numbering, bool canCorrect)
    {
        if (numbering.UltimoNumero == long.MaxValue)
            return Result.Failure<PurchaseDocumentNumberingDto>(
                ApplicationError.NumberingExhausted(
                    "El consecutivo de compras alcanzó el número máximo admitido."));
        return Result.Success(new PurchaseDocumentNumberingDto(
            numbering.TipoDocumento,
            numbering.Serie,
            numbering.Prefijo,
            numbering.UltimoNumero,
            numbering.UltimoNumero + 1,
            numbering.Version,
            canCorrect));
    }

    private static readonly ApplicationError VersionConflict =
        ApplicationError.ConcurrencyConflict(
            "El registro fue modificado por otro usuario. Recargue los datos e intente nuevamente.");
}
