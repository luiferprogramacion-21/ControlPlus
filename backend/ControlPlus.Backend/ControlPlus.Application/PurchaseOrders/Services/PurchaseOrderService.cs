using ControlPlus.Application.Common;
using ControlPlus.Application.PurchaseOrders.Contracts;
using ControlPlus.Application.PurchaseOrders.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.PurchaseOrders.Services;

public sealed class PurchaseOrderService(IPurchaseOrderRepository repository, IUserRepository users,
    IPermissionRepository permissions, IPasswordHasher passwords, ILoginAttemptCoordinator loginAttempts,
    IAuditRepository audit, IUnitOfWork unitOfWork, IClock clock) : IPurchaseOrderService
{
    private static readonly (string Code, string Name)[] CloseCatalog =
    [
        ("PROVEEDOR_NO_ENTREGARA", "Proveedor no entregará el pendiente"),
        ("PENDIENTE_NO_REQUERIDO", "Pendiente ya no requerido"),
        ("OTRO", "Otro")
    ];
    private static readonly (string Code, string Name)[] CancelCatalog =
    [
        ("ERROR_PEDIDO", "Error en el pedido"),
        ("PROVEEDOR_NO_ATIENDE", "Proveedor no atiende el pedido"),
        ("OTRO", "Otro")
    ];

    public async Task<Result<PagedResult<PurchaseOrderSummaryDto>>> ListAsync(ActorContext actor,
        PurchaseOrderListQuery query, CancellationToken ct = default)
    {
        if (!await AllowedAsync(actor.UserId, PermissionCodes.PurchaseOrdersManage, RoleLevel.Supervisor, ct))
            return Denied<PagedResult<PurchaseOrderSummaryDto>>();
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || query.SupplierId == Guid.Empty)
            return Invalid<PagedResult<PurchaseOrderSummaryDto>>("Filtros de pedido inválidos.");
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset > int.MaxValue) return Invalid<PagedResult<PurchaseOrderSummaryDto>>("La página solicitada es demasiado grande.");
        var status = query.Status?.Trim().ToUpperInvariant();
        if (status is not null and not ("BORRADOR" or "ENVIADO" or "RECIBIDO_PARCIAL" or
            "COMPLETADO" or "CERRADO_CON_PENDIENTES" or "CANCELADO"))
            return Invalid<PagedResult<PurchaseOrderSummaryDto>>("Estado de pedido inválido.");
        var installation = await repository.GetActiveInstallationAsync(ct);
        if (installation is null) return Missing<PagedResult<PurchaseOrderSummaryDto>>("la instalación activa");
        var result = await repository.ListAsync(installation.Id, query with { Status = status }, (int)offset, ct);
        return Result.Success(new PagedResult<PurchaseOrderSummaryDto>(result.Items.Select(x => new PurchaseOrderSummaryDto(
            x.Id, x.ProveedorId, x.Estado, VisibleNumber(x), x.FechaCreacion, x.Version)).ToArray(),
            result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<PurchaseOrderDto>> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        if (!await AllowedAsync(actor.UserId, PermissionCodes.PurchaseOrdersManage, RoleLevel.Supervisor, ct))
            return Denied<PurchaseOrderDto>();
        if (id == Guid.Empty) return Invalid<PurchaseOrderDto>("orderId inválido.");
        var installation = await repository.GetActiveInstallationAsync(ct);
        if (installation is null) return Missing<PurchaseOrderDto>("la instalación activa");
        var order = await repository.GetAsync(id, ct);
        return order is null || order.InstalacionId != installation.Id
            ? Missing<PurchaseOrderDto>("el pedido") : Result.Success(Map(order));
    }

    public async Task<Result<PurchaseOrderNumberingDto>> GetNumberingAsync(ActorContext actor, CancellationToken ct = default)
    {
        if (!await AllowedAsync(actor.UserId, PermissionCodes.NumberingManage, RoleLevel.Administrador, ct))
            return Denied<PurchaseOrderNumberingDto>();
        var installation = await repository.GetActiveInstallationAsync(ct);
        if (installation is null) return Missing<PurchaseOrderNumberingDto>("la instalación activa");
        var sequence = await repository.GetNumberingAsync(installation.Id, installation.Serie, ct);
        return sequence is null ? Missing<PurchaseOrderNumberingDto>("el consecutivo de pedidos")
            : sequence.UltimoNumero == long.MaxValue
                ? Result.Failure<PurchaseOrderNumberingDto>(ApplicationError.NumberingExhausted("El consecutivo de pedidos está agotado."))
                : Result.Success(MapNumbering(sequence));
    }

    public async Task<Result<PurchaseOrderNumberingDto>> InitializeNumberingAsync(ActorContext actor, CancellationToken ct = default)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Missing<PurchaseOrderNumberingDto>("la instalación activa");
        if (!await AllowedAsync(actor.UserId, PermissionCodes.NumberingManage, RoleLevel.Administrador, ct))
            return Denied<PurchaseOrderNumberingDto>();
        if (await repository.GetNumberingForUpdateAsync(installation.Id, installation.Serie, ct) is not null)
            return Conflict<PurchaseOrderNumberingDto>("Ya existe el consecutivo de pedidos para esta instalación y serie.");
        var sequence = new ConsecutivoDocumento { Id = Guid.CreateVersion7(), InstalacionId = installation.Id,
            TipoDocumento = "PEDIDO_COMPRA", Prefijo = "PED", Serie = installation.Serie,
            UltimoNumero = 0, Version = 1 };
        await repository.AddNumberingAsync(sequence, ct);
        await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.PurchaseOrderNumberingCreated,
            nameof(ConsecutivoDocumento), sequence.Id, new { sequence.TipoDocumento, sequence.Prefijo,
                sequence.Serie, FirstNumber = 1 }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(MapNumbering(sequence));
    }

    public async Task<Result<PurchaseOrderDto>> CreateAsync(ActorContext actor,
        CreatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var error = ValidateLines(request?.SupplierId ?? Guid.Empty, request?.Lines);
        if (error is not null) return Result.Failure<PurchaseOrderDto>(error);
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Missing<PurchaseOrderDto>("la instalación activa");
        if (!await AllowedAsync(actor.UserId, PermissionCodes.PurchaseOrdersManage, RoleLevel.Supervisor, ct))
            return Denied<PurchaseOrderDto>();
        var supplier = await repository.GetSupplierForUpdateAsync(request!.SupplierId, ct);
        if (supplier is null || !supplier.Activo) return Missing<PurchaseOrderDto>("el proveedor activo");
        var sequence = await repository.GetNumberingForUpdateAsync(installation.Id, installation.Serie, ct);
        if (sequence is null || sequence.Prefijo != "PED" || sequence.UltimoNumero < 0)
            return Conflict<PurchaseOrderDto>("Configure primero el consecutivo PEDIDO_COMPRA con prefijo PED.");
        if (sequence.UltimoNumero == long.MaxValue)
            return Result.Failure<PurchaseOrderDto>(ApplicationError.NumberingExhausted("El consecutivo de pedidos está agotado."));
        if (await repository.HasDraftForSupplierAsync(installation.Id, supplier.Id, null, ct))
            return Conflict<PurchaseOrderDto>("Ya existe un pedido BORRADOR para este proveedor.");
        var products = await repository.GetProductsForUpdateAsync(request.Lines.Select(x => x.ProductId).ToArray(), ct);
        var prepared = await PrepareLinesAsync(Guid.CreateVersion7(), supplier.Id, request.Lines, products, ct);
        if (prepared.Error is not null) return Result.Failure<PurchaseOrderDto>(prepared.Error);
        var now = clock.UtcNow.UtcDateTime;
        var order = new PedidoCompra { Id = prepared.OrderId, InstalacionId = installation.Id,
            ProveedorId = supplier.Id, UsuarioCreacionId = actor.UserId, Prefijo = sequence.Prefijo,
            Serie = sequence.Serie, Consecutivo = checked(sequence.UltimoNumero + 1), FechaCreacion = now,
            Estado = "BORRADOR", Version = 1 };
        foreach (var line in prepared.Lines!) order.DetallePedidoCompra.Add(line);
        sequence.UltimoNumero = order.Consecutivo;
        await repository.AddAsync(order, ct);
        await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.PurchaseOrderCreated,
            nameof(PedidoCompra), order.Id, new { OrderId = order.Id, State = order.Estado,
                ProductIds = products.Select(x => x.Id).Order().ToArray() }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(order));
    }

    public async Task<Result<PurchaseOrderDto>> UpdateAsync(ActorContext actor, Guid id,
        UpdatePurchaseOrderRequest request, CancellationToken ct = default)
    {
        var error = ValidateLines(id, request?.Lines);
        if (id == Guid.Empty || request is null || request.ExpectedVersion <= 0)
            return Invalid<PurchaseOrderDto>("orderId y expectedVersion deben ser válidos.");
        if (error is not null) return Result.Failure<PurchaseOrderDto>(error);
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Missing<PurchaseOrderDto>("la instalación activa");
        if (!await AllowedAsync(actor.UserId, PermissionCodes.PurchaseOrdersManage, RoleLevel.Supervisor, ct))
            return Denied<PurchaseOrderDto>();
        var order = await repository.GetForUpdateAsync(id, ct);
        if (order is null || order.InstalacionId != installation.Id) return Missing<PurchaseOrderDto>("el pedido");
        if (order.Version != request.ExpectedVersion || order.Version == long.MaxValue) return Stale();
        if (order.Estado != "BORRADOR") return Conflict<PurchaseOrderDto>("Solo puede editarse un pedido BORRADOR.");
        var supplier = await repository.GetSupplierForUpdateAsync(order.ProveedorId, ct);
        if (supplier is null || !supplier.Activo) return Missing<PurchaseOrderDto>("el proveedor activo");
        var products = await repository.GetProductsForUpdateAsync(request.Lines.Select(x => x.ProductId).ToArray(), ct);
        var prepared = await PrepareLinesAsync(id, supplier.Id, request.Lines, products, ct);
        if (prepared.Error is not null) return Result.Failure<PurchaseOrderDto>(prepared.Error);
        await repository.ReplaceLinesAsync(order, prepared.Lines!, ct);
        order.Version = checked(order.Version + 1);
        await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.PurchaseOrderUpdated,
            nameof(PedidoCompra), id, new { OrderId = id, Version = order.Version,
                ProductIds = products.Select(x => x.Id).Order().ToArray() }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(order));
    }

    public async Task<Result<PurchaseOrderDto>> SendAsync(ActorContext actor, Guid id,
        SendPurchaseOrderRequest request, CancellationToken ct = default)
    {
        if (id == Guid.Empty || request is null || request.ExpectedVersion <= 0)
            return Invalid<PurchaseOrderDto>("orderId y expectedVersion deben ser válidos.");
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Missing<PurchaseOrderDto>("la instalación activa");
        if (!await AllowedAsync(actor.UserId, PermissionCodes.PurchaseOrdersManage, RoleLevel.Supervisor, ct))
            return Denied<PurchaseOrderDto>();
        var order = await repository.GetForUpdateAsync(id, ct);
        if (order is null || order.InstalacionId != installation.Id) return Missing<PurchaseOrderDto>("el pedido");
        if (order.Version != request.ExpectedVersion || order.Version == long.MaxValue) return Stale();
        if (order.Estado != "BORRADOR" || order.DetallePedidoCompra.Count == 0)
            return Conflict<PurchaseOrderDto>("Solo puede enviarse un pedido BORRADOR con líneas.");
        var supplier = await repository.GetSupplierForUpdateAsync(order.ProveedorId, ct);
        if (supplier is null || !supplier.Activo) return Missing<PurchaseOrderDto>("el proveedor activo");
        var products = await repository.GetProductsForUpdateAsync(order.DetallePedidoCompra.Select(x => x.ProductoId).ToArray(), ct);
        if (products.Count != order.DetallePedidoCompra.Count ||
            products.Any(x => !x.Activo || x.ProveedorId != supplier.Id) ||
            order.DetallePedidoCompra.Any(x => x.CantidadSolicitada <= 0 || x.CantidadRecibida != 0))
            return Conflict<PurchaseOrderDto>("El proveedor, productos o cantidades ya no son válidos.");
        order.Estado = "ENVIADO";
        order.FechaEnvio = clock.UtcNow.UtcDateTime;
        order.UsuarioEnvioId = actor.UserId;
        order.Version = checked(order.Version + 1);
        await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.PurchaseOrderSent,
            nameof(PedidoCompra), id, new { OrderId = id, State = order.Estado, Version = order.Version }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(order));
    }

    public Task<Result<PurchaseOrderDto>> CloseAsync(ActorContext actor, Guid id,
        ClosePurchaseOrderRequest request, CancellationToken ct = default) =>
        ChangeStatusAsync(actor, id, request?.ExpectedVersion ?? 0, request?.ReasonId ?? Guid.Empty,
            request?.Observation, request?.Authorization, false, ct);

    public Task<Result<PurchaseOrderDto>> CancelAsync(ActorContext actor, Guid id,
        CancelPurchaseOrderRequest request, CancellationToken ct = default) =>
        ChangeStatusAsync(actor, id, request?.ExpectedVersion ?? 0, request?.ReasonId ?? Guid.Empty,
            request?.Observation, request?.Authorization, true, ct);

    private async Task<Result<PurchaseOrderDto>> ChangeStatusAsync(ActorContext actor, Guid id,
        long version, Guid reasonId, string? observation, PurchaseOrderAuthorizationRequest? credentials,
        bool cancel, CancellationToken ct)
    {
        if (id == Guid.Empty || version <= 0 || reasonId == Guid.Empty || observation?.Length > 500 ||
            credentials is null || string.IsNullOrWhiteSpace(credentials.UserName) || string.IsNullOrWhiteSpace(credentials.Password))
            return Invalid<PurchaseOrderDto>("Versión, motivo y credenciales son obligatorios.");
        var permissionCode = cancel ? PermissionCodes.PurchaseOrdersCancel : PermissionCodes.PurchaseOrdersManage;
        if (!await AllowedAsync(actor.UserId, permissionCode,
            cancel ? RoleLevel.Administrador : RoleLevel.Supervisor, ct))
            return Denied<PurchaseOrderDto>();
        return await loginAttempts.ExecuteAsync(credentials.UserName.Trim().ToUpperInvariant(), token =>
            ChangeStatusWithinTransactionAsync(actor, id, version, reasonId, observation?.Trim(),
                credentials, cancel, permissionCode, token), ct);
    }

    private async Task<Result<PurchaseOrderDto>> ChangeStatusWithinTransactionAsync(ActorContext actor,
        Guid id, long version, Guid reasonId, string? observation, PurchaseOrderAuthorizationRequest credentials,
        bool cancel, string permissionCode, CancellationToken ct)
    {
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Missing<PurchaseOrderDto>("la instalación activa");
        if (!await AllowedAsync(actor.UserId, permissionCode,
            cancel ? RoleLevel.Administrador : RoleLevel.Supervisor, ct))
            return Denied<PurchaseOrderDto>();
        var order = await repository.GetForUpdateAsync(id, ct);
        if (order is null || order.InstalacionId != installation.Id) return Missing<PurchaseOrderDto>("el pedido");
        if (order.Version != version || order.Version == long.MaxValue) return Stale();
        if (cancel ? order.Estado is not ("BORRADOR" or "ENVIADO") :
            order.Estado is not ("ENVIADO" or "RECIBIDO_PARCIAL") ||
            order.DetallePedidoCompra.All(x => x.CantidadRecibida >= x.CantidadSolicitada))
            return Conflict<PurchaseOrderDto>("La transición del pedido no está permitida.");
        var type = cancel ? "CANCELACION_PEDIDO" : "CIERRE_PEDIDO_PENDIENTE";
        var reason = await repository.GetReasonAsync(installation.EstablecimientoId, type, reasonId, ct);
        if (reason is null) return Missing<PurchaseOrderDto>("el motivo activo del pedido");
        if (!Approved(reason, type)) return Conflict<PurchaseOrderDto>("El motivo no conserva la configuración aprobada.");
        if (reason.Codigo == "OTRO" && string.IsNullOrWhiteSpace(observation))
            return Invalid<PurchaseOrderDto>("La observación es obligatoria para OTRO.");
        var authorizer = await users.GetByUserNameAsync(credentials.UserName.Trim(), ct);
        var correlation = Guid.CreateVersion7();
        if (authorizer is null)
        {
            await RejectAuthorizationAsync(actor.UserId, id, correlation, ct);
            return Result.Failure<PurchaseOrderDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
        }
        authorizer = await repository.GetUserForUpdateAsync(authorizer.Id, ct) ?? authorizer;
        if (!authorizer.IsActive || !await AllowedAsync(authorizer.Id, permissionCode, RoleLevel.Administrador, ct))
            return Result.Failure<PurchaseOrderDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
        if (!passwords.Verify(credentials.Password, authorizer.PasswordHash))
        {
            var locked = authorizer.RegisterFailedLogin(clock.UtcNow);
            await users.UpdateAsync(authorizer, ct);
            if (locked) await AuditWriter.WriteAsync(audit, clock, authorizer.Id, AuditAction.UserLocked,
                nameof(Usuario), authorizer.Id, new { authorizer.Id, authorizer.FailedLoginAttempts }, ct);
            await RejectAuthorizationAsync(actor.UserId, id, correlation, ct);
            return Result.Failure<PurchaseOrderDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
        }
        var permission = await permissions.GetByCodeAsync(permissionCode, ct);
        if (permission is null) return Conflict<PurchaseOrderDto>("El permiso de autorización no está disponible.");
        var now = clock.UtcNow.UtcDateTime;
        authorizer.RegisterSuccessfulLogin(clock.UtcNow);
        await users.UpdateAsync(authorizer, ct);
        var authorization = new AutorizacionOperacion { Id = Guid.CreateVersion7(),
            UsuarioSolicitanteId = actor.UserId, UsuarioAutorizadorId = authorizer.Id, PermisoId = permission.Id,
            TipoOperacion = type, CorrelacionId = correlation, DescripcionOperacion = "Autorización de pedido de compra.",
            Motivo = reason.Codigo, MetodoAutenticacion = "PASSWORD", Resultado = "EXITOSA",
            Estado = "UTILIZADA", FechaSolicitud = now, FechaExpiracion = now.AddMinutes(5), FechaUtilizacion = now };
        await repository.AddAuthorizationAsync(authorization, ct);
        if (cancel)
        {
            order.Estado = "CANCELADO";
            order.FechaCancelacion = now;
            order.UsuarioCancelacionId = actor.UserId;
            order.AutorizacionCancelacionId = authorization.Id;
            order.MotivoCancelacionId = reason.Id;
            order.ObservacionCancelacion = string.IsNullOrWhiteSpace(observation) ? null : observation;
        }
        else
        {
            order.Estado = "CERRADO_CON_PENDIENTES";
            order.FechaCierre = now;
            order.UsuarioCierreId = actor.UserId;
            order.AutorizacionCierreId = authorization.Id;
            order.MotivoCierreId = reason.Id;
            order.ObservacionCierre = string.IsNullOrWhiteSpace(observation) ? null : observation;
        }
        order.Version = checked(version + 1);
        await AuditWriter.WriteAsync(audit, clock, actor.UserId,
            cancel ? AuditAction.PurchaseOrderCancelled : AuditAction.PurchaseOrderClosed,
            nameof(PedidoCompra), id, new { OrderId = id, State = order.Estado, ReasonId = reason.Id,
                AuthorizationId = authorization.Id, Version = order.Version, CorrelationId = correlation }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Map(order));
    }

    private async Task RejectAuthorizationAsync(Guid userId, Guid orderId, Guid correlation, CancellationToken ct)
    {
        await AuditWriter.WriteCashFailureAsync(audit, clock, userId,
            AuditAction.PurchaseOrderAuthorizationRejected, nameof(PedidoCompra), orderId,
            new { OrderId = orderId, Result = "INVALID_AUTHORIZATION" }, correlation, null, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<Result<IReadOnlyCollection<PurchaseOrderReasonDto>>> ListReasonsAsync(
        ActorContext actor, string kind, CancellationToken ct = default)
    {
        var type = ReasonType(kind);
        if (type is null) return Invalid<IReadOnlyCollection<PurchaseOrderReasonDto>>("Tipo de motivo inválido.");
        if (!await AllowedAsync(actor.UserId, type == "CANCELACION_PEDIDO" ? PermissionCodes.PurchaseOrdersCancel :
            PermissionCodes.PurchaseOrdersManage,
            type == "CANCELACION_PEDIDO" ? RoleLevel.Administrador : RoleLevel.Supervisor, ct))
            return Denied<IReadOnlyCollection<PurchaseOrderReasonDto>>();
        var installation = await repository.GetActiveInstallationAsync(ct);
        if (installation is null) return Missing<IReadOnlyCollection<PurchaseOrderReasonDto>>("la instalación activa");
        var reasons = await repository.ListReasonsAsync(installation.EstablecimientoId, type, ct);
        return Result.Success<IReadOnlyCollection<PurchaseOrderReasonDto>>(reasons.Where(x => Approved(x, type)).Select(MapReason).ToArray());
    }

    public async Task<Result<IReadOnlyCollection<PurchaseOrderReasonDto>>> InitializeReasonsAsync(
        ActorContext actor, string kind, CancellationToken ct = default)
    {
        var type = ReasonType(kind);
        if (type is null) return Invalid<IReadOnlyCollection<PurchaseOrderReasonDto>>("Tipo de motivo inválido.");
        await using var transaction = await repository.BeginAsync(ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Missing<IReadOnlyCollection<PurchaseOrderReasonDto>>("la instalación activa");
        if (!await AllowedAsync(actor.UserId, type == "CANCELACION_PEDIDO" ? PermissionCodes.PurchaseOrdersCancel :
            PermissionCodes.PurchaseOrdersManage, RoleLevel.Administrador, ct)) return Denied<IReadOnlyCollection<PurchaseOrderReasonDto>>();
        var existing = await repository.ListReasonsAsync(installation.EstablecimientoId, type, ct);
        var catalog = type == "CANCELACION_PEDIDO" ? CancelCatalog : CloseCatalog;
        var created = new List<MotivoOperacion>();
        for (var index = 0; index < catalog.Length; index++)
        {
            var (code, name) = catalog[index];
            var found = existing.SingleOrDefault(x => x.Codigo == code);
            if (found is not null)
            {
                if (!Approved(found, type)) return Conflict<IReadOnlyCollection<PurchaseOrderReasonDto>>(
                    "El catálogo del pedido contiene un motivo incompatible.");
                continue;
            }
            created.Add(new MotivoOperacion { Id = Guid.CreateVersion7(),
                EstablecimientoId = installation.EstablecimientoId, TipoOperacion = type, Codigo = code,
                Nombre = name, Descripcion = name, OrdenVisual = (index + 1) * 10,
                Activo = true, FechaCreacion = clock.UtcNow.UtcDateTime, Version = 1 });
        }
        if (created.Count > 0)
        {
            await repository.AddReasonsAsync(created, ct);
            await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.PurchaseOrderReasonsInitialized,
                nameof(MotivoOperacion), null, new { Type = type,
                    Codes = created.Select(x => x.Codigo).ToArray() }, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return Result.Success<IReadOnlyCollection<PurchaseOrderReasonDto>>(existing.Concat(created)
            .Where(x => Approved(x, type)).OrderBy(x => x.OrdenVisual).Select(MapReason).ToArray());
    }

    private async Task<PreparedLines> PrepareLinesAsync(Guid orderId, Guid supplierId,
        IReadOnlyCollection<PurchaseOrderLineRequest> requests, IReadOnlyCollection<Producto> products, CancellationToken ct)
    {
        if (products.Count != requests.Count || products.Any(x => !x.Activo || x.ProveedorId != supplierId ||
            x.StockActual < 0 || x.StockReservado < 0 || x.StockReservado > x.StockActual || x.StockMinimo < 0))
            return new(orderId, null, ApplicationError.Conflict("Los productos deben estar activos, asignados al proveedor y tener inventario válido."));
        var pending = await repository.PendingOtherOrdersAsync(requests.Select(x => x.ProductId).ToArray(), ct);
        var byId = products.ToDictionary(x => x.Id);
        var lines = new List<DetallePedidoCompra>();
        try
        {
            foreach (var request in requests.OrderBy(x => x.ProductId))
            {
                var product = byId[request.ProductId];
                var available = checked(product.StockActual - product.StockReservado);
                var others = pending.GetValueOrDefault(product.Id);
                var suggested = Math.Max(0, checked(product.StockMinimo - checked(available + others)));
                lines.Add(new DetallePedidoCompra { Id = Guid.CreateVersion7(), PedidoCompraId = orderId,
                    ProductoId = product.Id, CodigoProducto = product.CodigoInterno, NombreProducto = product.Nombre,
                    StockDisponibleOrigen = available, StockMinimoOrigen = product.StockMinimo,
                    PendienteOtrosPedidos = others, CantidadSugerida = suggested,
                    CantidadSolicitada = request.RequestedQuantity, CantidadRecibida = 0,
                    CantidadExcedente = 0, CostoEstimado = request.EstimatedUnitCost,
                    Observacion = string.IsNullOrWhiteSpace(request.Observation) ? null : request.Observation.Trim(),
                    OrigenNecesidad = available == 0 ? "AGOTADO" : suggested > 0 ? "BAJO_MINIMO" : "MANUAL", Version = 1 });
            }
        }
        catch (OverflowException)
        {
            return new(orderId, null, ApplicationError.Validation("Las cantidades exceden el rango permitido."));
        }
        return new(orderId, lines, null);
    }

    private static ApplicationError? ValidateLines(Guid supplierId, IReadOnlyCollection<PurchaseOrderLineRequest>? lines)
    {
        if (supplierId == Guid.Empty || lines is null || lines.Count is < 1 or > 200 ||
            lines.Any(x => x is null || x.ProductId == Guid.Empty || x.RequestedQuantity <= 0 ||
                x.EstimatedUnitCost is decimal cost && (cost < 0m || cost > 99999999999999.9999m ||
                    decimal.Round(cost, 4) != cost) || x.Observation?.Length > 500) ||
            lines.Select(x => x.ProductId).Distinct().Count() != lines.Count)
            return ApplicationError.Validation("Proveedor y 1 a 200 líneas únicas con cantidades positivas son obligatorios.");
        return null;
    }

    private async Task<bool> AllowedAsync(Guid userId, string code, RoleLevel level, CancellationToken ct)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(userId, ct);
        return snapshot is not null && snapshot.HighestRoleLevel >= level &&
            snapshot.PermissionCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ReasonType(string kind) => kind?.Trim().ToUpperInvariant() switch
    {
        "CLOSE" => "CIERRE_PEDIDO_PENDIENTE", "CANCEL" => "CANCELACION_PEDIDO", _ => null
    };

    private static bool Approved(MotivoOperacion reason, string type)
    {
        var catalog = type == "CANCELACION_PEDIDO" ? CancelCatalog : CloseCatalog;
        var index = Array.FindIndex(catalog, x => x.Code == reason.Codigo);
        return index >= 0 && reason.Activo && reason.TipoOperacion == type &&
            reason.Nombre == catalog[index].Name && reason.Descripcion == catalog[index].Name &&
            reason.OrdenVisual == (index + 1) * 10;
    }

    private static PurchaseOrderReasonDto MapReason(MotivoOperacion reason) =>
        new(reason.Id, reason.Codigo, reason.Nombre, reason.OrdenVisual);
    private static PurchaseOrderNumberingDto MapNumbering(ConsecutivoDocumento sequence) =>
        new(sequence.Prefijo, sequence.Serie, sequence.UltimoNumero, checked(sequence.UltimoNumero + 1), sequence.Version);
    private static string VisibleNumber(PedidoCompra order) => $"{order.Prefijo}-{order.Serie}-{order.Consecutivo}";
    private static PurchaseOrderDto Map(PedidoCompra order) => new(order.Id, order.ProveedorId,
        order.Estado, order.Prefijo, order.Serie, order.Consecutivo, VisibleNumber(order), order.Version,
        order.FechaCreacion, order.FechaEnvio, order.FechaCierre, order.DetallePedidoCompra
            .OrderBy(x => x.ProductoId).Select(x => new PurchaseOrderLineDto(x.Id, x.ProductoId,
                x.CodigoProducto, x.NombreProducto, x.CantidadSolicitada, x.CantidadRecibida,
                Math.Max(0, x.CantidadSolicitada - x.CantidadRecibida), x.CantidadExcedente,
                x.CantidadSugerida, x.CostoEstimado, x.Observacion,
                x.StockDisponibleOrigen, x.StockMinimoOrigen, x.PendienteOtrosPedidos,
                x.OrigenNecesidad)).ToArray(), order.FechaCancelacion,
        order.MotivoCierreId, order.MotivoCancelacionId,
        order.AutorizacionCierreId, order.AutorizacionCancelacionId);

    private static Result<T> Denied<T>() => Result.Failure<T>(ApplicationError.Forbidden());
    private static Result<T> Invalid<T>(string message) => Result.Failure<T>(ApplicationError.Validation(message));
    private static Result<T> Conflict<T>(string message) => Result.Failure<T>(ApplicationError.Conflict(message));
    private static Result<T> Missing<T>(string resource) => Result.Failure<T>(ApplicationError.NotFound(resource));
    private static Result<PurchaseOrderDto> Stale() => Result.Failure<PurchaseOrderDto>(
        ApplicationError.ConcurrencyConflict("El pedido cambió. Recargue los datos."));
    private sealed record PreparedLines(Guid OrderId, IReadOnlyCollection<DetallePedidoCompra>? Lines,
        ApplicationError? Error);
}
