using ControlPlus.Domain.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Purchases.Contracts;
using ControlPlus.Application.Purchases.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Purchases;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Purchases.Services;

public sealed class PurchaseService(IPurchaseRepository repository, IUserRepository users,
    IAuditRepository audit, IUnitOfWork unitOfWork, IClock clock,
    IPermissionRepository permissions, IPasswordHasher passwords, IAuthorizerCredentialResolver credentialResolver,
    ILoginAttemptCoordinator loginAttempts) : IPurchaseService
{
    private const string InternalDocumentPrefix = "CP-INTERNO-";
    private static readonly (string Code, string Name)[] CancellationCatalog =
    [
        ("ERROR_REGISTRO", "Error de registro"),
        ("COMPRA_DUPLICADA", "Compra duplicada"),
        ("DOCUMENTO_PROVEEDOR_ANULADO", "Documento de proveedor anulado"),
        ("MERCANCIA_NO_RECIBIDA", "Mercancía no recibida"),
        ("OTRO", "Otro")
    ];

    public async Task<Result<IReadOnlyCollection<PurchaseCancellationReasonDto>>> ListCancellationReasonsAsync(
        ActorContext actor, CancellationToken ct = default)
    {
        if (!await CanCancelAsync(actor, ct)) return Denied<IReadOnlyCollection<PurchaseCancellationReasonDto>>();
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<IReadOnlyCollection<PurchaseCancellationReasonDto>>(ApplicationError.NotFound("la instalación activa"));
        var reasons = await repository.ListCancellationReasonsAsync(installation.EstablecimientoId, ct);
        return Result.Success<IReadOnlyCollection<PurchaseCancellationReasonDto>>(reasons
            .Where(x => x.Activo && IsApprovedCancellationReason(x))
            .Select(MapReason).ToArray());
    }

    public async Task<Result<IReadOnlyCollection<PurchaseCancellationReasonDto>>> InitializeCancellationReasonsAsync(
        ActorContext actor, CancellationToken ct = default)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<IReadOnlyCollection<PurchaseCancellationReasonDto>>(ApplicationError.NotFound("la instalación activa"));
        if (!await CanCancelAsync(actor, ct)) return Denied<IReadOnlyCollection<PurchaseCancellationReasonDto>>();
        var existing = await repository.ListCancellationReasonsAsync(installation.EstablecimientoId, ct);
        var now = clock.UtcNow.UtcDateTime;
        var created = new List<MotivoOperacion>();
        for (var index = 0; index < CancellationCatalog.Length; index++)
        {
            var (code, name) = CancellationCatalog[index];
            var found = existing.SingleOrDefault(x => x.Codigo == code);
            if (found is not null)
            {
                if (!IsApprovedCancellationReason(found))
                    return Result.Failure<IReadOnlyCollection<PurchaseCancellationReasonDto>>(ApplicationError.Conflict("El catálogo de anulación de compras contiene una configuración incompatible."));
                continue;
            }
            created.Add(new MotivoOperacion { Id = Guid.CreateVersion7(), EstablecimientoId = installation.EstablecimientoId,
                TipoOperacion = "ANULACION_COMPRA", Codigo = code, Nombre = name,
                Descripcion = name, OrdenVisual = (index + 1) * 10, Activo = true,
                FechaCreacion = now, Version = 1 });
        }
        if (created.Count > 0)
        {
            await repository.AddCancellationReasonsAsync(created, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return Result.Success<IReadOnlyCollection<PurchaseCancellationReasonDto>>(existing.Concat(created)
            .Where(IsApprovedCancellationReason).OrderBy(x => x.OrdenVisual).Select(MapReason).ToArray());
    }

    public async Task<Result<PurchaseDto>> CancelAsync(ActorContext actor, Guid id, string key,
        CancelPurchaseRequest request, CancellationToken ct = default)
    {
        if (!await CanCancelAsync(actor, ct)) return Denied<PurchaseDto>();
        key = key?.Trim() ?? "";
        if (id == Guid.Empty || request is null || request.ReasonId == Guid.Empty || request.ExpectedVersion <= 0 ||
            key.Length is < 1 or > 100 || key.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':')) ||
            request.Observation?.Length > 500)
            return Invalid<PurchaseDto>("La solicitud de anulación o Idempotency-Key es inválida.");
        if (request.ExpectedVersion == long.MaxValue)
            return Result.Failure<PurchaseDto>(ApplicationError.ConcurrencyConflict("La versión de la compra está agotada."));
        if (request?.Authorization is { } scanned && scanned.UsesCredential())
        {
            if (scanned.MixesCredentialWithPassword())
                return Invalid<PurchaseDto>("Envíe la credencial escaneada o usuario y contraseña, no ambos.");
            var (scannedUserName, scannedCredentialId) = await credentialResolver.ResolveOrUnknownAsync(scanned.CredentialToken!, ct);
            request = request with { Authorization = scanned with { UserName = scannedUserName, ResolvedCredentialId = scannedCredentialId } };
        }
        var observation = string.IsNullOrWhiteSpace(request!.Observation) ? null : request.Observation.Trim();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { PurchaseId = id, request.ExpectedVersion, request.ReasonId, Observation = observation, Actor = actor.UserId }))));
        // Replays without credentials still enter the same installation/purchase lock order.
        // The coordinator owns the only transaction and COMMIT for a new cancellation.
        var accountLockKey = string.IsNullOrWhiteSpace(request.Authorization?.UserName)
            ? $"PURCHASE-CANCEL-REPLAY:{actor.UserId:N}"
            : request.Authorization.UserName!.Trim().ToUpperInvariant();
        return await loginAttempts.ExecuteAsync(accountLockKey,
            token => CancelWithinTransactionAsync(actor, id, key, request, observation, fingerprint, token), ct);
    }

    private async Task<Result<PurchaseDto>> CancelWithinTransactionAsync(ActorContext actor, Guid id, string key,
        CancelPurchaseRequest request, string? observation, string fingerprint, CancellationToken ct)
    {
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la instalación activa"));
        if (!await CanCancelAsync(actor, ct)) return Denied<PurchaseDto>();
        var previous = await repository.GetIdempotencyAsync(installation.Id, key, ct);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "ANULAR_COMPRA" ||
                previous.HashSolicitud != fingerprint || previous.EntidadId != id)
                return Result.Failure<PurchaseDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            PurchaseDto? saved;
            try
            {
                saved = previous.Resultado is null ? null : JsonSerializer.Deserialize<PurchaseDto>(previous.Resultado);
            }
            catch (JsonException)
            {
                saved = null;
            }
            return saved is null ? Result.Failure<PurchaseDto>(ApplicationError.Conflict("La anulación original no es recuperable.")) : Result.Success(saved);
        }
        if (request.Authorization is not null && !request.Authorization.IsComplete())
            return Invalid<PurchaseDto>("Si envía authorization, incluya usuario y contraseña o la credencial escaneada.");
        var administratorSelfAuthorized = request.Authorization is null;
        var purchase = await repository.GetForUpdateAsync(id, ct);
        if (purchase is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la compra"));
        if (purchase.Version != request.ExpectedVersion) return Stale();
        if (purchase.PedidoCompraId is not null || purchase.Estado != "CONFIRMADA")
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("Solo puede anularse una compra independiente confirmada."));
        var reason = await repository.GetCancellationReasonAsync(installation.EstablecimientoId, request.ReasonId, ct);
        if (reason is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("el motivo activo de anulación de compra"));
        if (!IsApprovedCancellationReason(reason))
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El motivo no conserva la configuración aprobada."));
        if (reason.Codigo == "OTRO" && observation is null)
            return Invalid<PurchaseDto>("La observación es obligatoria para OTRO.");
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la terminal activa"));
        var originals = await repository.GetPurchaseMovementsAsync(id, ct);
        var details = purchase.DetalleCompra.OrderBy(x => x.ProductoId).ToArray();
        if (details.Length == 0 || originals.Count != details.Length ||
            details.Any(x => originals.Count(m => m.ProductoId == x.ProductoId && m.CantidadStock == x.Cantidad) != 1))
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("Los movimientos originales de compra son inconsistentes."));
        var products = await repository.GetProductsForUpdateAsync(details.Select(x => x.ProductoId).ToArray(), ct);
        if (products.Count != details.Length) return Result.Failure<PurchaseDto>(ApplicationError.Conflict("Falta un producto de la compra."));
        var reverse = new List<MovimientoInventario>();
        var now = clock.UtcNow.UtcDateTime;
        var correlation = Guid.CreateVersion7();
        foreach (var detail in details)
        {
            var product = products.Single(x => x.Id == detail.ProductoId);
            var original = originals.Single(x => x.ProductoId == detail.ProductoId);
            if (original.OrdenRegistro is not long originalOrder)
                return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El movimiento histórico no tiene orden de registro certificable."));
            if (await repository.HasLaterMovementAsync(product.Id, originalOrder, ct) ||
                await repository.HasReversalAsync(original.Id, ct) ||
                product.StockActual != original.StockPosterior || product.CostoPromedio != detail.CostoPromedioResultante ||
                original.StockPosterior - original.CantidadStock != original.StockAnterior ||
                original.CostoPromedioAnterior != detail.CostoPromedioAnterior ||
                original.CostoPromedioPosterior != detail.CostoPromedioResultante ||
                product.StockActual - detail.Cantidad < product.StockReservado ||
                product.StockActual - detail.Cantidad < 0)
                return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El inventario de la compra ya cambió o no admite reversión."));
            var nextStock = product.StockActual - detail.Cantidad;
            reverse.Add(new MovimientoInventario { Id = Guid.CreateVersion7(), ProductoId = product.Id,
                UsuarioId = actor.UserId, MovimientoRevertidoId = original.Id,
                TipoMovimiento = "ANULACION_COMPRA", CantidadStock = -detail.Cantidad, CantidadReservada = 0,
                StockAnterior = product.StockActual, StockPosterior = nextStock,
                ReservadoAnterior = product.StockReservado, ReservadoPosterior = product.StockReservado,
                CostoPromedioAnterior = product.CostoPromedio, CostoPromedioPosterior = detail.CostoPromedioAnterior,
                FechaHora = now, CorrelacionId = correlation });
        }
        Usuario? authorizer;
        if (administratorSelfAuthorized)
        {
            authorizer = await repository.GetUserForUpdateAsync(actor.UserId, ct);
            if (authorizer is null || !authorizer.IsActive)
                return Denied<PurchaseDto>();
        }
        else
        {
            authorizer = await users.GetByUserNameAsync(request.Authorization!.UserName!.Trim(), ct);
            if (authorizer is null)
            {
                await WriteRejectedAuthorizationAsync(actor, id, correlation, terminal.Id, ct);
                return Result.Failure<PurchaseDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            }
            authorizer = await repository.GetUserForUpdateAsync(authorizer.Id, ct) ?? authorizer;
            if (!authorizer.IsActive || !await CanCancelUserAsync(authorizer.Id, ct) ||
                !RoleHierarchy.IsHigherThan(authorizer.HighestRoleLevel,
                    (await users.GetAuthorizationSnapshotAsync(actor.UserId, ct))!.HighestRoleLevel))
                return Result.Failure<PurchaseDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            if (!request.Authorization.UsesCredential() && !passwords.Verify(request.Authorization.Password!, authorizer.PasswordHash))
            {
                var locked = authorizer.RegisterFailedLogin(clock.UtcNow);
                await users.UpdateAsync(authorizer, ct);
                if (locked)
                    await AuditWriter.WriteAsync(audit, clock, authorizer.Id, AuditAction.UserLocked,
                        nameof(Usuario), authorizer.Id,
                        new { authorizer.Id, authorizer.FailedLoginAttempts }, ct);
                await WriteRejectedAuthorizationAsync(actor, id, correlation, terminal.Id, ct);
                return Result.Failure<PurchaseDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            }
        }
        var permission = await permissions.GetByCodeAsync(PermissionCodes.PurchasesCancel, ct);
        if (permission is null || !permission.Activo)
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El permiso de anulación no está disponible."));
        if (!administratorSelfAuthorized)
        {
            authorizer.RegisterSuccessfulLogin(clock.UtcNow);
            await users.UpdateAsync(authorizer, ct);
        }
        var authorization = new AutorizacionOperacion { Id = Guid.CreateVersion7(),
            UsuarioSolicitanteId = actor.UserId, UsuarioAutorizadorId = authorizer.Id, PermisoId = permission.Id,
            TipoOperacion = "ANULAR_COMPRA", CorrelacionId = correlation,
            DescripcionOperacion = administratorSelfAuthorized
                ? "Autorización de anulación de compra por Administrador sin segundo autorizador."
                : "Autorización de anulación de compra.", Motivo = reason.Codigo,
            MetodoAutenticacion = request.Authorization.AuthenticationMethod(),
            CredencialAutorizadorId = request.Authorization?.ResolvedCredentialId, Resultado = "EXITOSA", Estado = "UTILIZADA",
            FechaSolicitud = now, FechaExpiracion = now.AddMinutes(5), FechaUtilizacion = now };
        await repository.AddAuthorizationAsync(authorization, ct);
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var movement in reverse)
        {
            var product = products.Single(x => x.Id == movement.ProductoId);
            product.StockActual = movement.StockPosterior;
            product.CostoPromedio = movement.CostoPromedioPosterior;
        }
        purchase.Estado = "ANULADA";
        purchase.UsuarioAnulacionId = actor.UserId;
        purchase.AutorizacionAnulacionId = authorization.Id;
        purchase.MotivoAnulacionId = reason.Id;
        purchase.FechaAnulacion = now;
        purchase.ObservacionAnulacion = observation;
        purchase.Version = checked(request.ExpectedVersion + 1);
        foreach (var movement in reverse) movement.AutorizacionOperacionId = authorization.Id;
        await repository.AddMovementsAsync(reverse, ct);
        var response = Map(purchase);
        await repository.AddIdempotencyAsync(new OperacionIdempotente { Id = Guid.CreateVersion7(),
            InstalacionId = installation.Id, TerminalId = terminal.Id, UsuarioId = actor.UserId,
            ClaveIdempotencia = key, TipoOperacion = "ANULAR_COMPRA", HashSolicitud = fingerprint,
            Entidad = nameof(Compra), EntidadId = id, Resultado = JsonSerializer.Serialize(response),
            CorrelacionId = correlation, FechaRegistro = now }, ct);
        await AuditWriter.WriteCashAsync(audit, clock, actor.UserId, AuditAction.PurchaseCancelled,
            nameof(Compra), id, new { PurchaseId = id, From = "CONFIRMADA", To = "ANULADA",
                ReasonId = reason.Id, AuthorizationId = authorization.Id, Version = purchase.Version,
                ProductIds = details.Select(x => x.ProductoId).ToArray(), CorrelationId = correlation,
                Result = "SUCCESS", AuthorizationMode = administratorSelfAuthorized ? "ADMINISTRATOR_SELF" : "OTHER_USER" },
            correlation, authorizer.Id, null, terminal.Id, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(response);
    }

    private async Task<bool> CanCancelAsync(ActorContext actor, CancellationToken ct) =>
        await CanCancelUserAsync(actor.UserId, ct);

    private async Task<bool> CanCancelUserAsync(Guid userId, CancellationToken ct)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(userId, ct);
        return snapshot is not null && snapshot.HighestRoleLevel >= RoleLevel.Administrador &&
            snapshot.PermissionCodes.Contains(PermissionCodes.PurchasesCancel, StringComparer.OrdinalIgnoreCase);
    }

    private static PurchaseCancellationReasonDto MapReason(MotivoOperacion reason) =>
        new(reason.Id, reason.Codigo, reason.Nombre, reason.Descripcion, reason.OrdenVisual);

    private static bool IsApprovedCancellationReason(MotivoOperacion reason)
    {
        var index = Array.FindIndex(CancellationCatalog, x => x.Code == reason.Codigo);
        return index >= 0 && reason.Activo && reason.TipoOperacion == "ANULACION_COMPRA" &&
            reason.Nombre == CancellationCatalog[index].Name &&
            reason.Descripcion == CancellationCatalog[index].Name &&
            reason.OrdenVisual == (index + 1) * 10;
    }

    private async Task WriteRejectedAuthorizationAsync(ActorContext actor, Guid purchaseId,
        Guid correlationId, Guid terminalId, CancellationToken ct)
    {
        await AuditWriter.WriteCashFailureAsync(audit, clock, actor.UserId,
            AuditAction.PurchaseCancellationAuthorizationRejected, nameof(Compra), purchaseId,
            new { PurchaseId = purchaseId, Result = "INVALID_AUTHORIZATION" },
            correlationId, terminalId, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<Result<PagedResult<PurchaseSummaryDto>>> ListAsync(ActorContext actor,
        PurchaseListQuery query, CancellationToken ct = default)
    {
        if (!await AllowedAsync(actor, ct)) return Denied<PagedResult<PurchaseSummaryDto>>();
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            return Invalid<PagedResult<PurchaseSummaryDto>>("La página debe ser positiva y pageSize debe estar entre 1 y 100.");
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset > int.MaxValue) return Invalid<PagedResult<PurchaseSummaryDto>>("La página solicitada supera el límite permitido.");
        if (query.SupplierId == Guid.Empty || query.InvoiceDate == DateOnly.MinValue)
            return Invalid<PagedResult<PurchaseSummaryDto>>("Los filtros de proveedor o fecha son inválidos.");
        var status = query.Status?.Trim().ToUpperInvariant();
        if (status is not null and not ("BORRADOR" or "CONFIRMADA" or "ANULADA"))
            return Invalid<PagedResult<PurchaseSummaryDto>>("El estado solicitado es inválido.");
        var document = NormalizeDocument(query.SupplierDocumentNumber);
        if (document?.Length > 100) return Invalid<PagedResult<PurchaseSummaryDto>>("El filtro de documento supera 100 caracteres.");
        var result = await repository.ListAsync(query with { Status = status, SupplierDocumentNumber = document }, (int)offset, ct);
        return Result.Success(new PagedResult<PurchaseSummaryDto>(result.Items.Select(MapSummary).ToArray(),
            result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<PurchaseDto>> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseDto>();
        if (id == Guid.Empty) return Invalid<PurchaseDto>("purchaseId debe ser un UUID válido.");
        var purchase = await repository.GetAsync(id, ct);
        return purchase is null ? Result.Failure<PurchaseDto>(ApplicationError.NotFound("la compra")) : Result.Success(Map(purchase));
    }

    public async Task<Result<PurchaseDto>> CreateAsync(ActorContext actor, CreatePurchaseRequest request,
        CancellationToken ct = default)
    {
        var input = Validate(request.SupplierId, request.SupplierDocumentNumber, request.InvoiceDate,
            request.VatMode, request.Lines);
        if (input.Error is not null) return Result.Failure<PurchaseDto>(input.Error);
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la instalación activa"));
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseDto>();
        var supplier = await repository.GetSupplierForUpdateAsync(request.SupplierId, ct);
        if (supplier is null || !supplier.Activo) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("el proveedor activo"));
        if (input.Document is not null && await repository.DocumentExistsAsync(supplier.Id, input.Document, null, ct))
            return DuplicateDocument();
        var products = await repository.GetProductsForUpdateAsync(input.Lines!.Select(x => x.ProductId).ToArray(), ct);
        if (products.Count != input.Lines!.Count || products.Any(x => !x.Activo))
            return Result.Failure<PurchaseDto>(ApplicationError.NotFound("uno o más productos activos"));
        var prepared = Prepare(input.Lines, input.VatMode!, products);
        if (prepared.Error is not null) return Result.Failure<PurchaseDto>(prepared.Error);
        var now = clock.UtcNow.UtcDateTime;
        var purchase = new Compra
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, ProveedorId = supplier.Id,
            PedidoCompraId = null, UsuarioId = actor.UserId, NumeroDocumentoProveedor = input.Document,
            FechaDocumento = request.InvoiceDate, FechaHoraRegistro = now, ModoIva = input.VatMode!,
            SubtotalBruto = prepared.Gross, ImpuestoTotal = prepared.Vat, Total = prepared.Total,
            Estado = "BORRADOR", Version = 1
        };
        foreach (var line in prepared.Lines!) purchase.DetalleCompra.Add(ToDetail(purchase.Id, line));
        await repository.AddAsync(purchase, ct);
        var correlationId = Guid.CreateVersion7();
        await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.PurchaseDraftCreated,
            nameof(Compra), purchase.Id,
            new { PurchaseId = purchase.Id, State = purchase.Estado, purchase.Version,
                ModifiedFields = new[] { "supplierId", "supplierDocumentNumber", "invoiceDate", "vatMode", "lines" },
                CorrelationId = correlationId }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(purchase));
    }

    public async Task<Result<PurchaseDto>> UpdateAsync(ActorContext actor, Guid id,
        UpdatePurchaseRequest request, CancellationToken ct = default)
    {
        if (id == Guid.Empty) return Invalid<PurchaseDto>("purchaseId debe ser un UUID válido.");
        if (request.ExpectedVersion <= 0) return Invalid<PurchaseDto>("expectedVersion debe ser positivo.");
        if (request.ExpectedVersion == long.MaxValue)
            return Result.Failure<PurchaseDto>(ApplicationError.ConcurrencyConflict("La versión de la compra está agotada."));
        var input = Validate(request.SupplierId, request.SupplierDocumentNumber, request.InvoiceDate,
            request.VatMode, request.Lines);
        if (input.Error is not null) return Result.Failure<PurchaseDto>(input.Error);
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la instalación activa"));
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseDto>();
        var purchase = await repository.GetForUpdateAsync(id, ct);
        if (purchase is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la compra"));
        if (purchase.Version != request.ExpectedVersion) return Stale();
        if (purchase.Estado != "BORRADOR") return Result.Failure<PurchaseDto>(ApplicationError.Conflict("Solo se puede editar una compra BORRADOR."));
        var supplier = await repository.GetSupplierForUpdateAsync(request.SupplierId, ct);
        if (supplier is null || !supplier.Activo) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("el proveedor activo"));
        if (input.Document is not null && await repository.DocumentExistsAsync(supplier.Id, input.Document, id, ct))
            return DuplicateDocument();
        var products = await repository.GetProductsForUpdateAsync(input.Lines!.Select(x => x.ProductId).ToArray(), ct);
        if (products.Count != input.Lines!.Count || products.Any(x => !x.Activo))
            return Result.Failure<PurchaseDto>(ApplicationError.NotFound("uno o más productos activos"));
        var prepared = Prepare(input.Lines, input.VatMode!, products);
        if (prepared.Error is not null) return Result.Failure<PurchaseDto>(prepared.Error);
        purchase.ProveedorId = supplier.Id;
        purchase.NumeroDocumentoProveedor = input.Document;
        purchase.FechaDocumento = request.InvoiceDate;
        purchase.ModoIva = input.VatMode!;
        purchase.SubtotalBruto = prepared.Gross;
        purchase.ImpuestoTotal = prepared.Vat;
        purchase.Total = prepared.Total;
        purchase.Version = checked(request.ExpectedVersion + 1);
        await repository.ReplaceDetailsAsync(purchase, prepared.Lines!.Select(x => ToDetail(id, x)).ToArray(), ct);
        var correlationId = Guid.CreateVersion7();
        await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.PurchaseDraftUpdated,
            nameof(Compra), purchase.Id,
            new { PurchaseId = purchase.Id, State = purchase.Estado, Version = purchase.Version,
                ModifiedFields = new[] { "supplierId", "supplierDocumentNumber", "invoiceDate", "vatMode", "lines" },
                CorrelationId = correlationId }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(purchase));
    }

    public async Task<Result<PurchaseDto>> ConfirmAsync(ActorContext actor, Guid id, string key,
        ConfirmPurchaseRequest request, CancellationToken ct = default)
    {
        if (id == Guid.Empty) return Invalid<PurchaseDto>("purchaseId debe ser un UUID válido.");
        if (request.ExpectedVersion <= 0) return Invalid<PurchaseDto>("expectedVersion debe ser positivo.");
        if (request.ExpectedVersion == long.MaxValue)
            return Result.Failure<PurchaseDto>(ApplicationError.ConcurrencyConflict("La versión de la compra está agotada."));
        key = key?.Trim() ?? "";
        if (key.Length is < 1 or > 100 || key.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':')))
            return Invalid<PurchaseDto>("Idempotency-Key debe tener 1 a 100 caracteres ASCII alfanuméricos o - _ . :.");
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la instalación activa"));
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseDto>();
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:N}|{request.ExpectedVersion}")));
        var previous = await repository.GetIdempotencyAsync(installation.Id, key, ct);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "CONFIRMAR_COMPRA" ||
                previous.HashSolicitud != requestHash || previous.EntidadId != id)
                return Result.Failure<PurchaseDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            var original = await repository.GetAsync(id, ct);
            return original is null ? Result.Failure<PurchaseDto>(ApplicationError.Conflict("La confirmación original no es recuperable."))
                : Result.Success(Map(original));
        }
        var purchase = await repository.GetForUpdateAsync(id, ct);
        if (purchase is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la compra"));
        if (purchase.Version != request.ExpectedVersion) return Stale();
        if (purchase.Estado != "BORRADOR") return Result.Failure<PurchaseDto>(ApplicationError.Conflict("La compra ya fue confirmada o no es un borrador."));
        if (purchase.DetalleCompra.Count == 0) return Result.Failure<PurchaseDto>(ApplicationError.Conflict("La compra no contiene líneas."));
        var supplier = await repository.GetSupplierForUpdateAsync(purchase.ProveedorId, ct);
        if (supplier is null || !supplier.Activo) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("el proveedor activo"));
        var sequence = await repository.GetSequenceForUpdateAsync(installation.Id, installation.Serie, ct);
        if (sequence is null) return Result.Failure<PurchaseDto>(ApplicationError.Conflict("Debe configurar el consecutivo COMPRA antes de confirmar."));
        if (sequence.Prefijo != "CMP" || sequence.UltimoNumero < 0)
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El consecutivo COMPRA no conserva la configuración aprobada."));
        if (sequence.UltimoNumero == long.MaxValue)
            return Result.Failure<PurchaseDto>(ApplicationError.NumberingExhausted("El consecutivo COMPRA está agotado."));
        var lineIds = purchase.DetalleCompra.Select(x => x.ProductoId).ToArray();
        if (lineIds.Distinct().Count() != lineIds.Length)
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("La compra contiene productos repetidos."));
        var products = await repository.GetProductsForUpdateAsync(lineIds, ct);
        if (products.Count != lineIds.Length || products.Any(x => !x.Activo))
            return Result.Failure<PurchaseDto>(ApplicationError.NotFound("uno o más productos activos"));
        var lines = purchase.DetalleCompra.Select(x => new PurchaseLineRequest(x.ProductoId, x.Cantidad,
            x.CostoUnitarioDocumento, x.PorcentajeIva)).ToArray();
        if (Validate(purchase.ProveedorId, purchase.NumeroDocumentoProveedor, purchase.FechaDocumento,
                purchase.ModoIva, lines).Error is not null ||
            purchase.DetalleCompra.Any(x => x.ModoIva != purchase.ModoIva))
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El borrador contiene cantidades, costos, documento o IVA inválidos."));
        var prepared = Prepare(lines, purchase.ModoIva, products);
        if (prepared.Error is not null || prepared.Lines is null ||
            prepared.Gross != purchase.SubtotalBruto || prepared.Vat != purchase.ImpuestoTotal || prepared.Total != purchase.Total ||
            purchase.DetalleCompra.Any(d => prepared.Lines.All(x => x.Product.Id != d.ProductoId ||
                x.Gross != d.SubtotalBruto || x.TaxBase != d.BaseGravable || x.Vat != d.ValorIva || x.Total != d.TotalLinea)))
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El borrador ya no cumple el costo o IVA oficial."));
        var number = checked(sequence.UltimoNumero + 1);
        var externalDocument = NormalizeDocument(purchase.NumeroDocumentoProveedor);
        var document = externalDocument ?? $"{InternalDocumentPrefix}{installation.Id:N}-{sequence.Prefijo}-{sequence.Serie}-{number}";
        if (document.Length > 100 || await repository.DocumentExistsAsync(supplier.Id, document, purchase.Id, ct))
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El documento de proveedor ya existe o supera el límite permitido."));
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null) return Result.Failure<PurchaseDto>(ApplicationError.NotFound("la terminal activa"));
        var now = clock.UtcNow.UtcDateTime;
        var correlationId = Guid.CreateVersion7();
        var byId = prepared.Lines.ToDictionary(x => x.Product.Id);
        var movements = new List<MovimientoInventario>(lineIds.Length);
        try
        {
            foreach (var detail in purchase.DetalleCompra.OrderBy(x => x.ProductoId))
            {
                var line = byId[detail.ProductoId];
                var product = line.Product;
                var previousStock = product.StockActual;
                var previousAverage = product.CostoPromedio;
                var effective = PurchaseCostPolicy.EffectiveUnitCost(detail.CostoUnitarioDocumento,
                    purchase.ModoIva, detail.PorcentajeIva);
                var nextStock = checked(previousStock + detail.Cantidad);
                var average = PurchaseCostPolicy.Average(previousAverage, previousStock, detail.Cantidad, effective);
                detail.CostoPromedioAnterior = previousAverage;
                detail.CostoPromedioResultante = average;
                detail.PrecioVentaAnterior = product.PrecioMinorista;
                detail.PrecioVentaNuevo = product.PrecioMinorista;
                product.StockActual = nextStock;
                product.CostoPromedio = average;
                movements.Add(new MovimientoInventario
                {
                    Id = Guid.CreateVersion7(), ProductoId = product.Id, UsuarioId = actor.UserId,
                    CompraId = purchase.Id, TipoMovimiento = "COMPRA", CantidadStock = detail.Cantidad,
                    CantidadReservada = 0, StockAnterior = previousStock, StockPosterior = nextStock,
                    ReservadoAnterior = product.StockReservado, ReservadoPosterior = product.StockReservado,
                    CostoPromedioAnterior = previousAverage, CostoPromedioPosterior = average,
                    FechaHora = now, CorrelacionId = correlationId
                });
            }
        }
        catch (OverflowException)
        {
            return Result.Failure<PurchaseDto>(ApplicationError.Conflict("El stock o costo resultante excede el rango oficial."));
        }
        // El trigger oficial de detalle_compra permite actualizar los costos solo mientras
        // la cabecera sigue en BORRADOR. Este flush permanece dentro de la misma transaccion.
        await unitOfWork.SaveChangesAsync(ct);
        sequence.UltimoNumero = number;
        purchase.Prefijo = sequence.Prefijo;
        purchase.Serie = sequence.Serie;
        purchase.Consecutivo = number;
        purchase.NumeroDocumentoProveedor = document;
        purchase.Estado = "CONFIRMADA";
        purchase.FechaHoraConfirmacion = now;
        purchase.Version = checked(request.ExpectedVersion + 1);
        await repository.AddMovementsAsync(movements, ct);
        await repository.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, ClaveIdempotencia = key, TipoOperacion = "CONFIRMAR_COMPRA",
            HashSolicitud = requestHash, Entidad = nameof(Compra), EntidadId = purchase.Id,
            Resultado = JsonSerializer.Serialize(new { State = "CONFIRMADA", PurchaseId = purchase.Id }),
            CorrelacionId = correlationId, FechaRegistro = now
        }, ct);
        await AuditWriter.WriteCashAsync(audit, clock, actor.UserId, AuditAction.PurchaseConfirmed,
            nameof(Compra), purchase.Id,
            new { PurchaseId = purchase.Id, State = purchase.Estado, Version = purchase.Version,
                ModifiedFields = new[] { "status", "prefix", "series", "consecutive", "supplierDocumentNumber", "stock", "averageCost" },
                ProductIds = lineIds.Order().ToArray(), CorrelationId = correlationId },
            correlationId, null, null, terminal.Id, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(purchase));
    }

    public async Task<Result<PurchaseReceiptDto>> ReceiveOrderAsync(ActorContext actor, Guid orderId,
        string key, ReceivePurchaseOrderRequest request, CancellationToken ct = default)
    {
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseReceiptDto>();
        key = key?.Trim() ?? "";
        if (orderId == Guid.Empty || request is null || request.ExpectedOrderVersion <= 0 ||
            key.Length is < 1 or > 100 || key.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':')) ||
            request.Lines is null || request.Lines.Count is < 1 or > 200 ||
            request.Lines.Any(x => x is null || x.OrderLineId == Guid.Empty || x.ProductId == Guid.Empty) ||
            request.Lines.Select(x => x.OrderLineId).Distinct().Count() != request.Lines.Count)
            return Invalid<PurchaseReceiptDto>("Pedido, versión, líneas únicas e Idempotency-Key válidos son obligatorios.");
        if (request?.Authorization is { } scanned && scanned.UsesCredential())
        {
            if (scanned.MixesCredentialWithPassword())
                return Invalid<PurchaseReceiptDto>("Envíe la credencial escaneada o usuario y contraseña, no ambos.");
            var (scannedUserName, scannedCredentialId) = await credentialResolver.ResolveOrUnknownAsync(scanned.CredentialToken!, ct);
            request = request with { Authorization = scanned with { UserName = scannedUserName, ResolvedCredentialId = scannedCredentialId } };
        }
        var input = Validate(orderId, request!.SupplierDocumentNumber, request.InvoiceDate,
            request.VatMode, request.Lines.Select(x => new PurchaseLineRequest(x.ProductId, x.Quantity,
                x.UnitCost, x.VatPercentage)).ToArray());
        if (input.Error is not null) return Result.Failure<PurchaseReceiptDto>(input.Error);
        var normalized = new { OrderId = orderId, request.ExpectedOrderVersion,
            Document = input.Document, request.InvoiceDate, VatMode = input.VatMode,
            Lines = request.Lines.OrderBy(x => x.OrderLineId).Select(x => new
            { x.OrderLineId, x.ProductId, x.Quantity, x.UnitCost, x.VatPercentage }).ToArray(), Actor = actor.UserId };
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized))));
        var lockKey = string.IsNullOrWhiteSpace(request.Authorization?.UserName)
            ? $"PURCHASE-RECEIPT:{actor.UserId:N}" : request.Authorization.UserName!.Trim().ToUpperInvariant();
        return await loginAttempts.ExecuteAsync(lockKey, token =>
            ReceiveOrderWithinTransactionAsync(actor, orderId, key, request, input, fingerprint, token), ct);
    }

    private async Task<Result<PurchaseReceiptDto>> ReceiveOrderWithinTransactionAsync(ActorContext actor,
        Guid orderId, string key, ReceivePurchaseOrderRequest request, ValidatedInput input,
        string fingerprint, CancellationToken ct)
    {
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<PurchaseReceiptDto>(ApplicationError.NotFound("la instalación activa"));
        if (!await AllowedAsync(actor, ct)) return Denied<PurchaseReceiptDto>();
        var previous = await repository.GetIdempotencyAsync(installation.Id, key, ct);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "RECIBIR_PEDIDO_COMPRA" ||
                previous.HashSolicitud != fingerprint)
                return Result.Failure<PurchaseReceiptDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            try
            {
                var savedReceipt = previous.Resultado is null ? null : JsonSerializer.Deserialize<PurchaseReceiptDto>(previous.Resultado);
                return savedReceipt is null ? Result.Failure<PurchaseReceiptDto>(ApplicationError.Conflict("La recepción original no es recuperable."))
                    : Result.Success(savedReceipt);
            }
            catch (JsonException)
            {
                return Result.Failure<PurchaseReceiptDto>(ApplicationError.Conflict("La recepción original no es recuperable."));
            }
        }
        var order = await repository.GetOrderForUpdateAsync(orderId, ct);
        if (order is null || order.InstalacionId != installation.Id)
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.NotFound("el pedido"));
        if (order.Version != request.ExpectedOrderVersion || order.Version == long.MaxValue)
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.ConcurrencyConflict("El pedido cambió. Recargue los datos."));
        if (order.Estado is not ("ENVIADO" or "RECIBIDO_PARCIAL"))
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.Conflict("Solo puede recibirse un pedido enviado o parcial."));
        var supplier = await repository.GetSupplierForUpdateAsync(order.ProveedorId, ct);
        if (supplier is null || !supplier.Activo)
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.NotFound("el proveedor activo"));
        var sequence = await repository.GetSequenceForUpdateAsync(installation.Id, installation.Serie, ct);
        if (sequence is null || sequence.Prefijo != "CMP" || sequence.UltimoNumero < 0)
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.Conflict("Configure primero el consecutivo COMPRA con prefijo CMP."));
        if (sequence.UltimoNumero == long.MaxValue)
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.NumberingExhausted("El consecutivo COMPRA está agotado."));
        var byLine = order.DetallePedidoCompra.ToDictionary(x => x.Id);
        if (request.Lines.Any(x => !byLine.ContainsKey(x.OrderLineId)))
            return Invalid<PurchaseReceiptDto>("Todas las líneas recibidas deben pertenecer al pedido.");
        if (request.Lines.Any(x => (long)byLine[x.OrderLineId].CantidadRecibida + x.Quantity > int.MaxValue))
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.Conflict("La cantidad acumulada supera el rango oficial."));
        var products = await repository.GetProductsForUpdateAsync(request.Lines.Select(x => x.ProductId).ToArray(), ct);
        if (products.Count != request.Lines.Count || products.Any(x => !x.Activo))
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.NotFound("uno o más productos activos"));
        var prepared = Prepare(input.Lines!, input.VatMode!, products);
        if (prepared.Error is not null) return Result.Failure<PurchaseReceiptDto>(prepared.Error);
        var number = checked(sequence.UltimoNumero + 1);
        var document = input.Document ?? $"{InternalDocumentPrefix}{installation.Id:N}-{sequence.Prefijo}-{sequence.Serie}-{number}";
        if (document.Length > 100 || await repository.DocumentExistsAsync(supplier.Id, document, null, ct))
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.Conflict("El documento del proveedor ya existe o supera el límite permitido."));
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null) return Result.Failure<PurchaseReceiptDto>(ApplicationError.NotFound("la terminal activa"));
        var exceptional = request.Lines.Any(x => x.ProductId != byLine[x.OrderLineId].ProductoId ||
            (long)byLine[x.OrderLineId].CantidadRecibida + x.Quantity > byLine[x.OrderLineId].CantidadSolicitada);
        var correlation = Guid.CreateVersion7();
        AutorizacionOperacion? authorization = null;
        var administratorSelfAuthorized = false;
        if (exceptional)
        {
            administratorSelfAuthorized = request.Authorization is null &&
                await CanReceiveExceptionAsync(actor.UserId, ct);
            if (!administratorSelfAuthorized &&
                (request.Authorization is null || !request.Authorization.IsComplete()))
                return Invalid<PurchaseReceiptDto>("La recepción excepcional exige credenciales de Administrador.");
            var authorizer = administratorSelfAuthorized
                ? await repository.GetUserForUpdateAsync(actor.UserId, ct)
                : await users.GetByUserNameAsync(request.Authorization!.UserName!.Trim(), ct);
            if (authorizer is null)
            {
                await RejectReceiptAuthorizationAsync(actor.UserId, orderId, correlation, terminal.Id, ct);
                return Result.Failure<PurchaseReceiptDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            }
            authorizer = await repository.GetUserForUpdateAsync(authorizer.Id, ct) ?? authorizer;
            if (!authorizer.IsActive || !await CanReceiveExceptionAsync(authorizer.Id, ct) ||
                (!administratorSelfAuthorized && !RoleHierarchy.IsHigherThan(authorizer.HighestRoleLevel,
                    (await users.GetAuthorizationSnapshotAsync(actor.UserId, ct))!.HighestRoleLevel)))
                return Result.Failure<PurchaseReceiptDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            if (!administratorSelfAuthorized && !request.Authorization!.UsesCredential() && !passwords.Verify(request.Authorization!.Password!, authorizer.PasswordHash))
            {
                var locked = authorizer.RegisterFailedLogin(clock.UtcNow);
                await users.UpdateAsync(authorizer, ct);
                if (locked) await AuditWriter.WriteAsync(audit, clock, authorizer.Id, AuditAction.UserLocked,
                    nameof(Usuario), authorizer.Id, new { authorizer.Id, authorizer.FailedLoginAttempts }, ct);
                await RejectReceiptAuthorizationAsync(actor.UserId, orderId, correlation, terminal.Id, ct);
                return Result.Failure<PurchaseReceiptDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            }
            var permission = await permissions.GetByCodeAsync(PermissionCodes.PurchasesReceiveExceptions, ct);
            if (permission is null)
                return Result.Failure<PurchaseReceiptDto>(ApplicationError.Conflict("El permiso de excepciones no está disponible."));
            if (!administratorSelfAuthorized)
            {
                authorizer.RegisterSuccessfulLogin(clock.UtcNow);
                await users.UpdateAsync(authorizer, ct);
            }
            var authTime = clock.UtcNow.UtcDateTime;
            authorization = new AutorizacionOperacion { Id = Guid.CreateVersion7(),
                UsuarioSolicitanteId = actor.UserId, UsuarioAutorizadorId = authorizer.Id, PermisoId = permission.Id,
                TipoOperacion = "EXCEPCION_RECEPCION_COMPRA", CorrelacionId = correlation,
                DescripcionOperacion = administratorSelfAuthorized
                    ? "Autorización de recepción excepcional por Administrador sin segundo autorizador."
                    : "Autorización de exceso o sustitución en recepción de pedido.",
                Motivo = "EXCESO_O_SUSTITUCION", MetodoAutenticacion = request.Authorization.AuthenticationMethod(),
                CredencialAutorizadorId = request.Authorization?.ResolvedCredentialId, Resultado = "EXITOSA",
                Estado = "UTILIZADA", FechaSolicitud = authTime, FechaExpiracion = authTime.AddMinutes(5),
                FechaUtilizacion = authTime };
            await repository.AddAuthorizationAsync(authorization, ct);
        }
        var now = clock.UtcNow.UtcDateTime;
        var purchase = new Compra { Id = Guid.CreateVersion7(), InstalacionId = installation.Id,
            ProveedorId = order.ProveedorId, PedidoCompraId = order.Id, UsuarioId = actor.UserId,
            NumeroDocumentoProveedor = document, FechaDocumento = request.InvoiceDate, FechaHoraRegistro = now,
            ModoIva = input.VatMode!, SubtotalBruto = prepared.Gross, ImpuestoTotal = prepared.Vat,
            Total = prepared.Total, Estado = "BORRADOR", Version = 1 };
        var requestsByProduct = request.Lines.ToDictionary(x => x.ProductId);
        var movements = new List<MovimientoInventario>();
        try
        {
            foreach (var line in prepared.Lines!.OrderBy(x => x.Product.Id))
            {
                var receiptLine = requestsByProduct[line.Product.Id];
                var orderLine = byLine[receiptLine.OrderLineId];
                var substitute = line.Product.Id != orderLine.ProductoId;
                var oldStock = line.Product.StockActual;
                var oldAverage = line.Product.CostoPromedio;
                var effective = PurchaseCostPolicy.EffectiveUnitCost(line.UnitCost, input.VatMode!, line.Rate);
                var newStock = checked(oldStock + line.Quantity);
                var average = PurchaseCostPolicy.Average(oldAverage, oldStock, line.Quantity, effective);
                var detail = ToDetail(purchase.Id, line);
                detail.DetallePedidoCompraId = orderLine.Id;
                detail.EsSustituto = substitute;
                detail.AutorizacionExcepcionId = substitute ? authorization?.Id : null;
                detail.CostoPromedioAnterior = oldAverage;
                detail.CostoPromedioResultante = average;
                purchase.DetalleCompra.Add(detail);
                movements.Add(new MovimientoInventario { Id = Guid.CreateVersion7(), ProductoId = line.Product.Id,
                    UsuarioId = actor.UserId, CompraId = purchase.Id, TipoMovimiento = "COMPRA",
                    CantidadStock = line.Quantity, CantidadReservada = 0, StockAnterior = oldStock,
                    StockPosterior = newStock, ReservadoAnterior = line.Product.StockReservado,
                    ReservadoPosterior = line.Product.StockReservado, CostoPromedioAnterior = oldAverage,
                    CostoPromedioPosterior = average, FechaHora = now, CorrelacionId = correlation });
            }
        }
        catch (OverflowException)
        {
            return Result.Failure<PurchaseReceiptDto>(ApplicationError.Conflict("El stock o costo resultante excede el rango oficial."));
        }
        // The official detail trigger accepts inserts only while the purchase header is BORRADOR.
        await repository.AddAsync(purchase, ct);
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var movement in movements)
        {
            var product = products.Single(x => x.Id == movement.ProductoId);
            product.StockActual = movement.StockPosterior;
            product.CostoPromedio = movement.CostoPromedioPosterior;
        }
        foreach (var receiptLine in request.Lines)
        {
            var orderLine = byLine[receiptLine.OrderLineId];
            orderLine.CantidadRecibida = checked(orderLine.CantidadRecibida + receiptLine.Quantity);
            orderLine.CantidadExcedente = Math.Max(0, orderLine.CantidadRecibida - orderLine.CantidadSolicitada);
            if (orderLine.CantidadExcedente > 0) orderLine.AutorizacionExcesoId = authorization!.Id;
        }
        order.Estado = order.DetallePedidoCompra.All(x => x.CantidadRecibida >= x.CantidadSolicitada)
            ? "COMPLETADO" : "RECIBIDO_PARCIAL";
        if (order.Estado == "COMPLETADO")
        {
            order.FechaCierre = now;
            order.UsuarioCierreId = actor.UserId;
        }
        order.Version = checked(order.Version + 1);
        sequence.UltimoNumero = number;
        purchase.Prefijo = sequence.Prefijo;
        purchase.Serie = sequence.Serie;
        purchase.Consecutivo = number;
        purchase.Estado = "CONFIRMADA";
        purchase.FechaHoraConfirmacion = now;
        purchase.Version = 2;
        await repository.AddMovementsAsync(movements, ct);
        var response = new PurchaseReceiptDto(Map(purchase), order.Id, order.Estado, order.Version);
        await repository.AddIdempotencyAsync(new OperacionIdempotente { Id = Guid.CreateVersion7(),
            InstalacionId = installation.Id, TerminalId = terminal.Id, UsuarioId = actor.UserId,
            ClaveIdempotencia = key, TipoOperacion = "RECIBIR_PEDIDO_COMPRA", HashSolicitud = fingerprint,
            Entidad = nameof(Compra), EntidadId = purchase.Id, Resultado = JsonSerializer.Serialize(response),
            CorrelacionId = correlation, FechaRegistro = now }, ct);
        await AuditWriter.WriteCashAsync(audit, clock, actor.UserId, AuditAction.PurchaseOrderReceived,
            nameof(Compra), purchase.Id, new { PurchaseId = purchase.Id, OrderId = order.Id,
                OrderState = order.Estado, OrderVersion = order.Version, ProductIds = products.Select(x => x.Id).Order().ToArray(),
                AuthorizationId = authorization?.Id, CorrelationId = correlation,
                AuthorizationMode = authorization is null ? "NONE" :
                    administratorSelfAuthorized ? "ADMINISTRATOR_SELF" : "OTHER_USER" }, correlation,
            authorization?.UsuarioAutorizadorId, null, terminal.Id, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(response);
    }

    private async Task<bool> CanReceiveExceptionAsync(Guid userId, CancellationToken ct)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(userId, ct);
        return snapshot is not null && snapshot.HighestRoleLevel >= RoleLevel.Administrador &&
            snapshot.PermissionCodes.Contains(PermissionCodes.PurchasesReceiveExceptions, StringComparer.OrdinalIgnoreCase);
    }

    private async Task RejectReceiptAuthorizationAsync(Guid actorId, Guid orderId, Guid correlation,
        Guid terminalId, CancellationToken ct)
    {
        await AuditWriter.WriteCashFailureAsync(audit, clock, actorId,
            AuditAction.PurchaseReceiptAuthorizationRejected, nameof(PedidoCompra), orderId,
            new { OrderId = orderId, Result = "INVALID_AUTHORIZATION" }, correlation, terminalId, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<bool> AllowedAsync(ActorContext actor, CancellationToken ct)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        return snapshot is not null && snapshot.HighestRoleLevel >= RoleLevel.Supervisor &&
            snapshot.PermissionCodes.Contains(PermissionCodes.PurchasesReceive, StringComparer.OrdinalIgnoreCase);
    }

    private static ValidatedInput Validate(Guid supplierId, string? document, DateOnly date,
        string? vatMode, IReadOnlyCollection<PurchaseLineRequest>? lines)
    {
        if (supplierId == Guid.Empty) return ValidatedInput.Fail("supplierId debe ser un UUID válido.");
        if (date == DateOnly.MinValue) return ValidatedInput.Fail("invoiceDate es obligatoria y debe ser válida.");
        vatMode = vatMode?.Trim().ToUpperInvariant();
        if (vatMode is not ("NO_DISCRIMINADO" or "INCLUIDO" or "SEPARADO"))
            return ValidatedInput.Fail("vatMode debe ser NO_DISCRIMINADO, INCLUIDO o SEPARADO.");
        document = NormalizeDocument(document);
        if (document?.Length > 100 || document?.StartsWith(InternalDocumentPrefix, StringComparison.Ordinal) == true)
            return ValidatedInput.Fail("El documento de proveedor es inválido o usa el prefijo interno reservado.");
        if (lines is null || lines.Count is < 1 or > 200 || lines.Any(x => x is null))
            return ValidatedInput.Fail("La compra debe incluir entre 1 y 200 líneas válidas.");
        if (lines.Select(x => x.ProductId).Distinct().Count() != lines.Count)
            return ValidatedInput.Fail("No se permite repetir un producto en la compra.");
        foreach (var line in lines)
        {
            if (line.ProductId == Guid.Empty || line.Quantity <= 0 || line.UnitCost <= 0 ||
                line.UnitCost > PurchaseCostPolicy.MaximumFourPlaceAmount ||
                decimal.Round(line.UnitCost, 4) != line.UnitCost ||
                (line.VatPercentage is decimal rate &&
                 (rate is < 0m or > 100m || decimal.Round(rate, 2) != rate)))
                return ValidatedInput.Fail("Cada línea requiere producto, cantidad entera positiva y costo/IVA dentro de la precisión oficial.");
            if (vatMode == "NO_DISCRIMINADO" && line.VatPercentage is not null)
                return ValidatedInput.Fail("NO_DISCRIMINADO no admite vatPercentage.");
        }
        return new ValidatedInput(document, vatMode, lines.ToArray(), null);
    }

    private static PreparedPurchase Prepare(IReadOnlyCollection<PurchaseLineRequest> requests, string mode,
        IReadOnlyCollection<Producto> products)
    {
        var byId = products.ToDictionary(x => x.Id);
        var lines = new List<PreparedLine>(requests.Count);
        try
        {
            foreach (var request in requests.OrderBy(x => x.ProductId))
            {
                var product = byId[request.ProductId];
                if (!product.Activo || product.PrecioMinorista <= 0 || product.StockActual < 0 ||
                    product.StockReservado < 0 || product.StockReservado > product.StockActual)
                    return PreparedPurchase.Fail("El producto no conserva un estado de inventario o precio válido.");
                var rate = mode == "NO_DISCRIMINADO" ? null : product.PorcentajeIva;
                if (mode != "NO_DISCRIMINADO" &&
                    (rate is null || rate is < 0m or > 100m || decimal.Round(rate.Value, 2) != rate.Value ||
                     request.VatPercentage is decimal supplied && supplied != rate.Value))
                    return PreparedPurchase.Fail("El IVA de la línea debe coincidir con la tasa oficial del producto.");
                var gross = PurchaseCostPolicy.CheckedFour(checked(request.UnitCost * request.Quantity));
                decimal? baseAmount = null;
                decimal? tax = null;
                var total = gross;
                if (mode == "SEPARADO")
                {
                    baseAmount = gross;
                    tax = PurchaseCostPolicy.CheckedFour(PurchaseCostPolicy.Round4(gross * rate!.Value / 100m));
                    total = PurchaseCostPolicy.CheckedFour(checked(gross + tax.Value));
                }
                else if (mode == "INCLUIDO")
                {
                    baseAmount = PurchaseCostPolicy.CheckedFour(PurchaseCostPolicy.Round4(gross * 100m / (100m + rate!.Value)));
                    tax = gross - baseAmount.Value;
                }
                lines.Add(new PreparedLine(product, request.Quantity, request.UnitCost, mode, rate,
                    gross, baseAmount, tax, total));
            }
            var grossHeader = PurchaseCostPolicy.CheckedTwo(PurchaseCostPolicy.Round2(lines.Sum(x => x.Gross)));
            var vatHeader = mode == "NO_DISCRIMINADO" ? null :
                (decimal?)PurchaseCostPolicy.CheckedTwo(PurchaseCostPolicy.Round2(lines.Sum(x => x.Vat!.Value)));
            var totalHeader = mode == "SEPARADO" ?
                PurchaseCostPolicy.CheckedTwo(checked(grossHeader + vatHeader!.Value)) : grossHeader;
            return new PreparedPurchase(lines, grossHeader, vatHeader, totalHeader, null);
        }
        catch (OverflowException)
        {
            return PreparedPurchase.Fail("El costo, IVA o importe excede la precisión oficial.");
        }
    }

    private static DetalleCompra ToDetail(Guid purchaseId, PreparedLine line) => new()
    {
        Id = Guid.CreateVersion7(), CompraId = purchaseId, ProductoId = line.Product.Id,
        Cantidad = line.Quantity, ModoIva = line.Mode, CostoUnitarioDocumento = line.UnitCost,
        PorcentajeIva = line.Rate, SubtotalBruto = line.Gross, BaseGravable = line.TaxBase,
        ValorIva = line.Vat, TotalLinea = line.Total,
        CostoPromedioAnterior = line.Product.CostoPromedio,
        CostoPromedioResultante = line.Product.CostoPromedio ?? 0m,
        PrecioVentaAnterior = line.Product.PrecioMinorista, PrecioVentaNuevo = line.Product.PrecioMinorista,
        EsSustituto = false
    };

    private static PurchaseDto Map(Compra purchase) => new(purchase.Id, purchase.ProveedorId,
        purchase.NumeroDocumentoProveedor, purchase.FechaDocumento, purchase.ModoIva, purchase.Estado,
        purchase.Prefijo, purchase.Serie, purchase.Consecutivo, VisibleNumber(purchase),
        purchase.SubtotalBruto, purchase.ImpuestoTotal, purchase.Total, purchase.Version,
        purchase.DetalleCompra.OrderBy(x => x.ProductoId).Select(x => new PurchaseLineDto(x.Id, x.ProductoId,
            x.Cantidad, x.CostoUnitarioDocumento, x.ModoIva, x.PorcentajeIva, x.SubtotalBruto,
            x.BaseGravable, x.ValorIva, x.TotalLinea, x.CostoPromedioAnterior, x.CostoPromedioResultante)).ToArray(),
        purchase.PedidoCompraId);

    private static PurchaseSummaryDto MapSummary(Compra purchase) => new(purchase.Id, purchase.ProveedorId,
        purchase.NumeroDocumentoProveedor, purchase.FechaDocumento, purchase.Estado, VisibleNumber(purchase),
        purchase.Total, purchase.Version);

    private static string? VisibleNumber(Compra purchase) => purchase.Consecutivo is long number
        ? $"{purchase.Prefijo}-{purchase.Serie}-{number}" : null;

    private static string? NormalizeDocument(string? value) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim().ToUpperInvariant();

    private static Result<PurchaseDto> DuplicateDocument() => Result.Failure<PurchaseDto>(
        ApplicationError.Conflict("El documento ya existe para este proveedor."));
    private static Result<PurchaseDto> Stale() => Result.Failure<PurchaseDto>(
        ApplicationError.ConcurrencyConflict("La compra fue modificada. Recargue los datos."));
    private static Result<T> Denied<T>() => Result.Failure<T>(ApplicationError.Forbidden());
    private static Result<T> Invalid<T>(string message) => Result.Failure<T>(ApplicationError.Validation(message));

    private sealed record ValidatedInput(string? Document, string? VatMode,
        IReadOnlyCollection<PurchaseLineRequest>? Lines, ApplicationError? Error)
    {
        public static ValidatedInput Fail(string message) => new(null, null, null, ApplicationError.Validation(message));
    }

    private sealed record PreparedLine(Producto Product, int Quantity, decimal UnitCost, string Mode,
        decimal? Rate, decimal Gross, decimal? TaxBase, decimal? Vat, decimal Total);

    private sealed record PreparedPurchase(IReadOnlyCollection<PreparedLine>? Lines, decimal Gross,
        decimal? Vat, decimal Total, ApplicationError? Error)
    {
        public static PreparedPurchase Fail(string message) => new(null, 0m, null, 0m, ApplicationError.Validation(message));
    }
}
