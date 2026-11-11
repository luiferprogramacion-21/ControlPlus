using ControlPlus.Domain.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Inventory.Contracts;
using ControlPlus.Application.Inventory.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.Common;
using ControlPlus.Domain.Inventory;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Inventory.Services;

public sealed class InventoryService(
    IInventoryRepository repository,
    IUnitOfWork unitOfWork,
    IUserRepository userRepository,
    IPermissionRepository permissionRepository,
    IPermissionChecker permissionChecker,
    IPasswordHasher passwordHasher,
    IAuthorizerCredentialResolver credentialResolver,
    IAuditRepository auditRepository,
    IClock clock,
    ILoginAttemptCoordinator loginAttemptCoordinator) : IInventoryService
{
    private const int MaximumPageSize = 200;
    private const string AdjustmentOperationType = "AJUSTAR_INVENTARIO";
    private static readonly HashSet<string> MovementTypes = new(StringComparer.Ordinal)
    {
        "COMPRA", "ANULACION_COMPRA", "VENTA", "ANULACION_VENTA", "RESERVA_APARTADO",
        "LIBERACION_APARTADO", "ENTREGA_APARTADO", "CAMBIO_DEVOLUCION", "CAMBIO_ENTREGA",
        "AJUSTE_ENTRADA", "AJUSTE_SALIDA", "REVERSO", "INVENTARIO_INICIAL"
    };

    public async Task<Result<PagedResult<InventoryStockDto>>> ListStockAsync(
        ActorContext actor,
        InventoryStockQuery query,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await RequireAsync(actor, PermissionCodes.InventoryRead, cancellationToken);
        if (snapshot.Error is not null) return Result.Failure<PagedResult<InventoryStockDto>>(snapshot.Error);
        if (query.IncludeInactive && !Has(snapshot.Snapshot!, PermissionCodes.ProductsSensitiveUpdate))
            return Result.Failure<PagedResult<InventoryStockDto>>(ApplicationError.Forbidden(
                "Consultar existencias de productos inactivos requiere PRODUCTS.SENSITIVE_UPDATE."));
        var validation = ValidatePage(query.Page, query.PageSize);
        if (validation is not null) return Result.Failure<PagedResult<InventoryStockDto>>(validation);
        if (query.Search is { Length: > 200 })
            return Result.Failure<PagedResult<InventoryStockDto>>(Validation("La busqueda no puede superar 200 caracteres."));

        var canReadCosts = Has(snapshot.Snapshot!, PermissionCodes.ProductCostsRead);
        var result = await repository.ListStockAsync(
            query with { Search = NormalizeOptional(query.Search) }, cancellationToken);
        return Result.Success(new PagedResult<InventoryStockDto>(
            result.Items.Select(x => Map(x, canReadCosts)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    public async Task<Result<PagedResult<InventoryMovementDto>>> ListMovementsAsync(
        ActorContext actor,
        InventoryMovementQuery query,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await RequireAsync(actor, PermissionCodes.InventoryRead, cancellationToken);
        if (snapshot.Error is not null) return Result.Failure<PagedResult<InventoryMovementDto>>(snapshot.Error);
        var validation = ValidatePage(query.Page, query.PageSize);
        if (validation is not null) return Result.Failure<PagedResult<InventoryMovementDto>>(validation);
        if (query.ProductId == Guid.Empty)
            return Result.Failure<PagedResult<InventoryMovementDto>>(Validation("El producto indicado no es valido."));
        if (query.FromUtc is { } from && query.ToUtc is { } to && from > to)
            return Result.Failure<PagedResult<InventoryMovementDto>>(Validation("La fecha inicial no puede ser posterior a la fecha final."));

        var type = NormalizeOptional(query.Type)?.ToUpperInvariant();
        if (type is not null && !MovementTypes.Contains(type))
            return Result.Failure<PagedResult<InventoryMovementDto>>(Validation("El tipo de movimiento no pertenece al catalogo oficial."));

        var normalized = query with
        {
            Type = type,
            FromUtc = query.FromUtc?.ToUniversalTime(),
            ToUtc = query.ToUtc?.ToUniversalTime()
        };
        var canReadCosts = Has(snapshot.Snapshot!, PermissionCodes.ProductCostsRead);
        var result = await repository.ListMovementsAsync(normalized, cancellationToken);
        return Result.Success(new PagedResult<InventoryMovementDto>(
            result.Items.Select(x => Map(x, canReadCosts)).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount));
    }

    public async Task<Result<IReadOnlyCollection<InventoryAdjustmentReasonDto>>> ListAdjustmentReasonsAsync(
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await RequireAsync(actor, PermissionCodes.InventoryAdjust, cancellationToken);
        if (snapshot.Error is not null)
            return Result.Failure<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(snapshot.Error);
        var reasons = await repository.ListActiveAdjustmentReasonsAsync(cancellationToken);
        return Result.Success<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(reasons.Select(Map).ToArray());
    }

    public async Task<Result<InventoryMovementDto>> AdjustAsync(
        ActorContext actor,
        string idempotencyKey,
        CreateInventoryAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request?.Authorization is { } scanned && scanned.UsesCredential())
        {
            if (scanned.MixesCredentialWithPassword())
                return Result.Failure<InventoryMovementDto>(Validation("Envíe la credencial escaneada o usuario y contraseña, no ambos."));
            var (scannedUserName, scannedCredentialId) = await credentialResolver.ResolveOrUnknownAsync(scanned.CredentialToken!, cancellationToken);
            request = request with { Authorization = scanned with { UserName = scannedUserName, ResolvedCredentialId = scannedCredentialId } };
        }
        var input = NormalizeAdjustment(idempotencyKey, request!);
        if (input.Error is not null) return Result.Failure<InventoryMovementDto>(input.Error);

        return await loginAttemptCoordinator.ExecuteAsync(
            input.Authorization?.UserName!.ToUpperInvariant() ?? actor.UserId.ToString("N"),
            token => AdjustWithinTransactionAsync(actor, input, token),
            cancellationToken);
    }

    private async Task<Result<InventoryMovementDto>> AdjustWithinTransactionAsync(
        ActorContext actor,
        (string? Key, Guid ProductId, int QuantityDelta, Guid ReasonId, long ExpectedVersion,
            string? Observation, InventoryAdjustmentAuthorizationRequest? Authorization, string? Canonical,
            ApplicationError? Error) input,
        CancellationToken cancellationToken)
    {
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Result.Failure<InventoryMovementDto>(ApplicationError.NotFound("la instalacion activa"));

        var executor = await RequireAsync(actor, PermissionCodes.InventoryAdjust, cancellationToken);
        if (executor.Error is not null) return Result.Failure<InventoryMovementDto>(executor.Error);
        var administratorSelfAuthorized = executor.Snapshot!.HighestRoleLevel == RoleLevel.Administrador &&
            input.Authorization is null;
        if (!administratorSelfAuthorized && input.Authorization is null)
            return Result.Failure<InventoryMovementDto>(Validation("Las credenciales del autorizador son obligatorias."));

        var requestHash = Hash(input.Canonical! + (administratorSelfAuthorized ? "|ADMINISTRATOR_SELF" : ""));
        var existingOperation = await repository.GetIdempotencyAsync(installation.Id, input.Key!, cancellationToken);
        if (existingOperation is not null)
        {
            if (existingOperation.UsuarioId != actor.UserId ||
                existingOperation.TipoOperacion != AdjustmentOperationType ||
                existingOperation.HashSolicitud != requestHash ||
                existingOperation.EntidadId is not Guid movementId)
            {
                return Result.Failure<InventoryMovementDto>(
                    ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            }

            var existingMovement = await repository.GetMovementAsync(movementId, cancellationToken);
            if (existingMovement is null)
                return Result.Failure<InventoryMovementDto>(ApplicationError.Conflict(
                    "La operacion idempotente no tiene un ajuste recuperable."));
            return Result.Success(Map(existingMovement, Has(executor.Snapshot!, PermissionCodes.ProductCostsRead)));
        }

        var terminal = await repository.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null) return Result.Failure<InventoryMovementDto>(ApplicationError.NotFound("la terminal activa"));

        var correlationId = Guid.CreateVersion7();
        Result<Usuario> authorizerResult;
        if (administratorSelfAuthorized)
        {
            var self = await repository.GetUserForUpdateAsync(actor.UserId, cancellationToken);
            authorizerResult = self is not null && self.IsActive
                ? Result.Success(self) : Result.Failure<Usuario>(ApplicationError.Forbidden());
        }
        else
            authorizerResult = await ValidateAuthorizerAsync(actor, input.Authorization!, cancellationToken);
        if (authorizerResult.IsFailure)
        {
            await AuditWriter.WriteInventoryFailureAsync(
                auditRepository,
                clock,
                actor.UserId,
                input.ProductId,
                new { Failure = "INVALID_AUTHORIZER_CREDENTIALS_OR_PERMISSION" },
                correlationId,
                authorizerResult.Value?.Id,
                terminal.Id,
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<InventoryMovementDto>(authorizerResult.Error!);
        }
        var authorizer = authorizerResult.Value!;

        var reason = await repository.GetActiveAdjustmentReasonAsync(input.ReasonId, cancellationToken);
        if (reason is null)
            return Result.Failure<InventoryMovementDto>(ApplicationError.NotFound("el motivo activo de ajuste de inventario"));
        var product = await repository.GetProductForUpdateAsync(input.ProductId, cancellationToken);
        if (product is null) return Result.Failure<InventoryMovementDto>(ApplicationError.NotFound("el producto"));
        if (product.Version != input.ExpectedVersion)
            return Result.Failure<InventoryMovementDto>(ApplicationError.ConcurrencyConflict(
                "El producto cambio desde la ultima consulta. Actualice sus existencias antes de ajustar."));

        InventoryAdjustmentResult adjustment;
        try
        {
            adjustment = InventoryAdjustmentPolicy.Apply(
                product.Activo, product.StockActual, product.StockReservado, input.QuantityDelta);
        }
        catch (DomainRuleViolationException exception)
        {
            return Result.Failure<InventoryMovementDto>(Validation(exception.Message));
        }

        var permission = await permissionRepository.GetByCodeAsync(PermissionCodes.InventoryAdjust, cancellationToken);
        if (permission is null || !permission.Activo)
            return Result.Failure<InventoryMovementDto>(ApplicationError.Conflict(
                "No existe el permiso activo INVENTORY.ADJUST."));

        var now = clock.UtcNow;
        var authorization = new AutorizacionOperacion
        {
            Id = Guid.CreateVersion7(),
            UsuarioSolicitanteId = actor.UserId,
            UsuarioAutorizadorId = authorizer.Id,
            PermisoId = permission.Id,
            TipoOperacion = AdjustmentOperationType,
            CorrelacionId = correlationId,
            DescripcionOperacion = administratorSelfAuthorized
                ? "Autorización de ajuste de inventario por Administrador sin segundo autorizador."
                : "Autorizacion de ajuste manual de inventario.",
            Motivo = reason.Codigo,
            MetodoAutenticacion = input.Authorization.AuthenticationMethod(),
            CredencialAutorizadorId = input.Authorization?.ResolvedCredentialId,
            Resultado = "EXITOSA",
            Estado = "UTILIZADA",
            FechaSolicitud = now.UtcDateTime,
            FechaExpiracion = now.AddMinutes(5).UtcDateTime,
            FechaUtilizacion = now.UtcDateTime
        };
        product.StockActual = adjustment.NewStock;
        var movement = new MovimientoInventario
        {
            Id = Guid.CreateVersion7(),
            ProductoId = product.Id,
            UsuarioId = actor.UserId,
            AutorizacionOperacionId = authorization.Id,
            MotivoOperacionId = reason.Id,
            TipoMovimiento = adjustment.MovementType,
            CantidadStock = input.QuantityDelta,
            CantidadReservada = 0,
            StockAnterior = adjustment.PreviousStock,
            StockPosterior = adjustment.NewStock,
            ReservadoAnterior = adjustment.ReservedStock,
            ReservadoPosterior = adjustment.ReservedStock,
            CostoPromedioAnterior = product.CostoPromedio,
            CostoPromedioPosterior = product.CostoPromedio,
            FechaHora = now.UtcDateTime,
            Motivo = input.Observation,
            CorrelacionId = correlationId,
            Producto = product,
            MotivoOperacion = reason,
            AutorizacionOperacion = authorization
        };

        if (!administratorSelfAuthorized)
        {
            authorizer.RegisterSuccessfulLogin(now);
            await userRepository.UpdateAsync(authorizer, cancellationToken);
        }
        await repository.AddAuthorizationAsync(authorization, cancellationToken);
        await repository.AddMovementAsync(movement, cancellationToken);
        var operation = new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(),
            InstalacionId = installation.Id,
            TerminalId = terminal.Id,
            UsuarioId = actor.UserId,
            ClaveIdempotencia = input.Key!,
            TipoOperacion = AdjustmentOperationType,
            HashSolicitud = requestHash,
            Entidad = nameof(MovimientoInventario),
            EntidadId = movement.Id,
            CorrelacionId = correlationId,
            FechaRegistro = now.UtcDateTime
        };
        await repository.AddIdempotencyAsync(operation, cancellationToken);
        await AuditWriter.WriteInventoryAsync(
            auditRepository,
            clock,
            actor.UserId,
            authorizer.Id,
            movement.Id,
            reason.Codigo,
            new
            {
                ProductId = product.Id,
                AuthorizationMode = administratorSelfAuthorized ? "ADMINISTRATOR_SELF" : "OTHER_USER",
                movement.TipoMovimiento,
                movement.CantidadStock,
                movement.StockAnterior,
                movement.StockPosterior,
                movement.ReservadoAnterior,
                movement.ReservadoPosterior,
                ReasonId = reason.Id,
                Observation = input.Observation
            },
            correlationId,
            terminal.Id,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var response = Map(movement, Has(executor.Snapshot!, PermissionCodes.ProductCostsRead));
        operation.Resultado = JsonSerializer.Serialize(response);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(response);
    }

    private async Task<Result<Usuario>> ValidateAuthorizerAsync(
        ActorContext actor,
        InventoryAdjustmentAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByUserNameAsync(request.UserName!, cancellationToken);
        if (user is null)
            return Result.Failure<Usuario>(InvalidAuthorizer());
        user = await repository.GetUserForUpdateAsync(user.Id, cancellationToken) ?? user;
        if (!user.IsActive || user.Id == actor.UserId)
            return Result.Failure<Usuario>(InvalidAuthorizer());
        if (!RoleHierarchy.IsHigherThan(user.HighestRoleLevel,
            (await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken))!.HighestRoleLevel))
            return Result.Failure<Usuario>(ApplicationError.AuthorizerRoleInsufficient());
        if (!request.UsesCredential() && !passwordHasher.Verify(request.Password!, user.PasswordHash))
        {
            var now = clock.UtcNow;
            var wasLocked = user.RegisterFailedLogin(now);
            await userRepository.UpdateAsync(user, cancellationToken);
            if (wasLocked)
            {
                await AuditWriter.WriteAsync(
                    auditRepository,
                    clock,
                    user.Id,
                    AuditAction.UserLocked,
                    nameof(Usuario),
                    user.Id,
                    new { user.UserName, FailedLoginAttempts = user.FailedLoginAttempts },
                    cancellationToken);
            }
            return Result.Failure<Usuario>(InvalidAuthorizer());
        }
        if (!await permissionChecker.HasPermissionAsync(user.Id, PermissionCodes.InventoryAdjust, cancellationToken))
            return Result.Failure<Usuario>(InvalidAuthorizer());
        return Result.Success(user);
    }

    private async Task<(UserAuthorizationSnapshot? Snapshot, ApplicationError? Error)> RequireAsync(
        ActorContext actor,
        string permission,
        CancellationToken cancellationToken)
    {
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return (null, ApplicationError.Unauthorized());
        return Has(snapshot, permission) ? (snapshot, null) : (snapshot, ApplicationError.Forbidden());
    }

    private static (string? Key, Guid ProductId, int QuantityDelta, Guid ReasonId, long ExpectedVersion,
        string? Observation, InventoryAdjustmentAuthorizationRequest? Authorization, string? Canonical, ApplicationError? Error)
        NormalizeAdjustment(string idempotencyKey, CreateInventoryAdjustmentRequest request)
    {
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 100)
            return (null, default, default, default, default, null, null, null,
                Validation("Idempotency-Key es obligatorio y no puede superar 100 caracteres."));
        if (request is null || request.ProductId == Guid.Empty || request.ReasonId == Guid.Empty)
            return (null, default, default, default, default, null, null, null,
                Validation("El producto y el motivo son obligatorios."));
        if (request.QuantityDelta == 0)
            return (null, default, default, default, default, null, null, null,
                Validation("La cantidad del ajuste debe ser un entero distinto de cero."));
        if (request.ExpectedVersion <= 0)
            return (null, default, default, default, default, null, null, null,
                Validation("La version esperada debe ser mayor que cero."));
        if (request.Authorization is not null && !request.Authorization.IsComplete())
            return (null, default, default, default, default, null, null, null,
                Validation("Si envía authorization, incluya usuario y contraseña o la credencial escaneada."));

        var observation = NormalizeOptional(request.Observation);
        var authorization = request.Authorization is null ? null : request.Authorization with { UserName = request.Authorization.UserName!.Trim() };
        var canonical = JsonSerializer.Serialize(new
        {
            request.ProductId,
            request.QuantityDelta,
            request.ReasonId,
            request.ExpectedVersion,
            Observation = observation,
            Authorizer = authorization?.UserName!.ToUpperInvariant()
        });
        return (key, request.ProductId, request.QuantityDelta, request.ReasonId, request.ExpectedVersion,
            observation, authorization, canonical, null);
    }

    private static InventoryStockDto Map(InventoryStockRecord value, bool includeCosts) => new(
        value.Product.Id,
        value.Product.CodigoInterno,
        value.Product.CodigoBarras,
        value.Product.Nombre,
        value.MeasurementUnit,
        value.Product.StockActual,
        value.Product.StockReservado,
        value.Product.StockActual - value.Product.StockReservado,
        value.Product.StockMinimo,
        value.Product.StockActual - value.Product.StockReservado <= 0,
        value.Product.Activo,
        includeCosts ? value.Product.CostoPromedio : null,
        value.LastMovementType,
        OptionalUtc(value.LastMovementAtUtc),
        value.Product.Version);

    private static InventoryMovementDto Map(MovimientoInventario value, bool includeCosts) => new(
        value.Id,
        value.ProductoId,
        value.Producto.CodigoInterno,
        value.Producto.Nombre,
        value.UsuarioId,
        value.AutorizacionOperacionId,
        value.MotivoOperacionId,
        value.MotivoOperacion?.Codigo,
        value.MotivoOperacion?.Nombre,
        value.VentaId,
        value.CompraId,
        value.TipoMovimiento,
        value.CantidadStock,
        value.CantidadReservada,
        value.StockAnterior,
        value.StockPosterior,
        value.ReservadoAnterior,
        value.ReservadoPosterior,
        includeCosts ? value.CostoPromedioAnterior : null,
        includeCosts ? value.CostoPromedioPosterior : null,
        value.Motivo,
        Utc(value.FechaHora),
        value.CorrelacionId);

    private static InventoryAdjustmentReasonDto Map(MotivoOperacion value) => new(
        value.Id, value.Codigo, value.Nombre, value.Descripcion, value.OrdenVisual);

    private static ApplicationError? ValidatePage(int page, int pageSize) =>
        page <= 0 || pageSize <= 0 || pageSize > MaximumPageSize
            ? Validation($"La pagina debe ser positiva y pageSize debe estar entre 1 y {MaximumPageSize}.")
            : null;

    private static bool Has(UserAuthorizationSnapshot snapshot, string permission) =>
        snapshot.PermissionCodes.Contains(permission, StringComparer.OrdinalIgnoreCase);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? OptionalUtc(DateTime? value) => value is null ? null : Utc(value.Value);
    private static ApplicationError Validation(string message) => ApplicationError.Validation(message);
    private static ApplicationError InvalidAuthorizer() => ApplicationError.Forbidden(
        "El autorizador debe ser otro usuario activo, no bloqueado, con INVENTORY.ADJUST efectivo y credenciales validas.");
}
