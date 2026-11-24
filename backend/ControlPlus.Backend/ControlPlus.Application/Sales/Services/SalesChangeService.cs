using ControlPlus.Domain.Security;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Sales;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Sales.Services;

public sealed partial class SalesService
{
    public async Task<Result<IReadOnlyCollection<SaleChangeDto>>> ListChangesAsync(ActorContext actor,
        Guid saleId, CancellationToken cancellationToken = default)
    {
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<IReadOnlyCollection<SaleChangeDto>>(ApplicationError.Unauthorized());
        var sale = await repository.GetSaleAsync(saleId, cancellationToken);
        if (sale is null) return Result.Failure<IReadOnlyCollection<SaleChangeDto>>(ApplicationError.NotFound("la venta"));
        if (!CanReadSale(snapshot.PermissionCodes, actor.UserId, sale))
            return Result.Failure<IReadOnlyCollection<SaleChangeDto>>(ApplicationError.Forbidden());
        var changes = await repository.GetChangesAsync(saleId, cancellationToken);
        return Result.Success<IReadOnlyCollection<SaleChangeDto>>(changes.Select(MapChange).ToArray());
    }

    /// <summary>
    /// Auditoría del 23 de noviembre de 2026: el mensaje dice quién tiene la caja y qué hacer, en vez de «pertenece a otro
    /// usuario». Una operación que mueve dinero solo la hace quien abrió el turno individual.
    /// </summary>
    private async Task<ApplicationError> OtherShiftOwnerErrorAsync(Guid? responsibleId, string operation, CancellationToken ct)
    {
        var name = responsibleId is Guid id ? (await userRepository.GetByIdAsync(id, ct))?.DisplayName : null;
        var who = string.IsNullOrWhiteSpace(name) ? "otro usuario" : name.Trim();
        return ApplicationError.Forbidden(
            $"La caja está abierta en turno individual por {who}. {operation} mueve dinero de esa caja, así que debe hacerlo {who} " +
            "con su usuario. Si varias personas atienden la caja, ciérrenla y ábranla en turno compartido.");
    }

    public async Task<Result<SaleChangeDto>> ConfirmChangeAsync(ActorContext actor, Guid saleId,
        string idempotencyKey, ConfirmSaleChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var input = NormalizeChange(saleId, idempotencyKey, request);
        if (input.Error is not null) return Result.Failure<SaleChangeDto>(input.Error);
        var scanned = await ResolveScannedAuthorizationAsync(request.Authorization, cancellationToken);
        if (scanned.Error is not null) return Result.Failure<SaleChangeDto>(scanned.Error);
        request = request with { Authorization = scanned.Authorization };
        var account = string.IsNullOrWhiteSpace(request.Authorization?.UserName)
            ? $"SALE-CHANGE-REPLAY:{actor.UserId:N}"
            : request.Authorization.UserName.Trim().ToUpperInvariant();
        return await loginAttemptCoordinator.ExecuteAsync(account,
            token => ConfirmChangeWithinTransactionAsync(actor, saleId, request, input, token),
            cancellationToken);
    }

    private async Task<Result<SaleChangeDto>> ConfirmChangeWithinTransactionAsync(ActorContext actor,
        Guid saleId, ConfirmSaleChangeRequest request, ChangeInput input, CancellationToken ct)
    {
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<SaleChangeDto>(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        if (snapshot is null) return Result.Failure<SaleChangeDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.SalesCreate, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleChangeDto>(ApplicationError.Forbidden());
        var administratorSelfAuthorized = snapshot.HighestRoleLevel == RoleLevel.Administrador &&
            request.Authorization is null;
        if (administratorSelfAuthorized &&
            !snapshot.PermissionCodes.Contains(PermissionCodes.SalesChangeAuthorize, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleChangeDto>(ApplicationError.Forbidden());
        var requestHash = administratorSelfAuthorized ? Hash(input.Hash + "|ADMINISTRATOR_SELF") : input.Hash;
        var previous = await repository.GetIdempotencyAsync(installation.Id, input.Key, ct);
        if (previous is not null)
        {
            if (previous.TipoOperacion != "CAMBIAR_VENTA" || previous.UsuarioId != actor.UserId ||
                previous.EntidadId != saleId || previous.HashSolicitud != requestHash || previous.Resultado is null)
                return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            SaleChangeDto? saved;
            try { saved = JsonSerializer.Deserialize<SaleChangeDto>(previous.Resultado); }
            catch (JsonException) { saved = null; }
            return saved is null
                ? Result.Failure<SaleChangeDto>(ApplicationError.Conflict("El cambio anterior no puede recuperarse."))
                : Result.Success(saved);
        }
        if (!administratorSelfAuthorized &&
            (request.Authorization is null || !request.Authorization.IsComplete()))
            return Result.Failure<SaleChangeDto>(Validation("Se requieren credenciales de otro Supervisor o Administrador."));
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null) return Result.Failure<SaleChangeDto>(ApplicationError.NotFound("la terminal activa"));
        var shift = await repository.GetOpenShiftForUpdateAsync(ct);
        if (shift is null || shift.TerminalId != terminal.Id)
            return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("Se requiere un turno abierto en la terminal activa."));
        SesionOperador? session = null;
        if (shift.ModoOperacion == CashConstants.IndividualMode)
        {
            if (shift.UsuarioResponsableId != actor.UserId)
                return Result.Failure<SaleChangeDto>(await OtherShiftOwnerErrorAsync(shift.UsuarioResponsableId, "Este cambio", ct));
        }
        else if (shift.ModoOperacion == CashConstants.SharedMode)
        {
            session = await repository.GetActiveOperatorSessionForUpdateAsync(shift.Id, terminal.Id,
                actor.UserId, ct);
            if (session is null)
                return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("Se requiere una sesión activa del operador."));
        }
        else return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("La modalidad de Caja es inválida."));
        var sequence = await repository.GetChangeSequenceForUpdateAsync(installation.Id, installation.Serie, ct);
        if (sequence is null || sequence.Prefijo != "CAM")
            return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("Configure el consecutivo CAMBIO_VENTA aprobado."));
        if (sequence.UltimoNumero == long.MaxValue)
            return Result.Failure<SaleChangeDto>(ApplicationError.NumberingExhausted("El consecutivo de cambios está agotado."));
        var sale = await repository.GetSaleForUpdateAsync(saleId, ct);
        if (sale is null || sale.InstalacionId != installation.Id)
            return Result.Failure<SaleChangeDto>(ApplicationError.NotFound("la venta"));
        if (sale.Version != request.ExpectedSaleVersion)
            return Result.Failure<SaleChangeDto>(ApplicationError.ConcurrencyConflict("La venta fue modificada."));
        if (sale.Estado != "CONFIRMADA" || sale.TipoVenta is not ("CONTADO" or "CREDITO"))
            return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("Solo puede cambiarse una venta confirmada."));
        var establishment = await repository.GetActiveEstablishmentAsync(ct);
        if (establishment is null) return Result.Failure<SaleChangeDto>(ApplicationError.NotFound("el establecimiento activo"));
        var now = clock.UtcNow;
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,
            TimeZoneInfo.FindSystemTimeZoneById(establishment.ZonaHoraria)).DateTime);
        if (!SaleChangeAvailability.IsWithinDeadline(localDate, sale.FechaLimiteCambio))
            return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("La venta está fuera del plazo de cambio."));

        var returnsByLine = request.Returned.ToDictionary(x => x.SaleLineId);
        var sold = sale.DetalleVenta.ToDictionary(x => x.Id);
        if (returnsByLine.Keys.Any(x => !sold.ContainsKey(x)))
            return Result.Failure<SaleChangeDto>(Validation("El producto devuelto no pertenece a esta venta."));
        var returnedBefore = await repository.GetConfirmedReturnedQuantitiesAsync(saleId, ct);
        foreach (var returned in request.Returned)
        {
            if (returned.Quantity > SaleChangeAvailability.RemainingQuantity(
                sold[returned.SaleLineId].Cantidad, returnedBefore.GetValueOrDefault(returned.SaleLineId)))
                return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("La cantidad devuelta supera la disponible."));
        }
        var productIds = request.Returned.Select(x => sold[x.SaleLineId].ProductoId)
            .Concat(request.Delivered.Select(x => x.ProductId)).Distinct().Order().ToArray();
        var products = (await repository.GetProductsForUpdateAsync(productIds, ct)).ToDictionary(x => x.Id);
        if (products.Count != productIds.Length)
            return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("Falta un producto del cambio."));
        if (request.Returned.Any(x => !products[sold[x.SaleLineId].ProductoId].Activo))
            return Result.Failure<SaleChangeDto>(ApplicationError.Conflict(
                "Un producto inactivo no puede volver a existencias vendibles."));
        var returnedByProduct = request.Returned.GroupBy(x => sold[x.SaleLineId].ProductoId)
            .ToDictionary(x => x.Key, x => x.Sum(y => (long)y.Quantity));
        if (returnedByProduct.Any(x => products[x.Key].StockActual + x.Value > int.MaxValue))
            return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("La reposición supera el límite de existencias."));
        var unitNames = await repository.GetMeasurementUnitNamesAsync(
            request.Delivered.Select(x => products[x.ProductId].UnidadMedidaId).Distinct().ToArray(), ct);
        if (request.Delivered.Any(x => !unitNames.ContainsKey(products[x.ProductId].UnidadMedidaId)))
            return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("Falta una unidad de medida."));

        var detailRows = new List<DetalleCambioVenta>();
        decimal totalReturned = 0, totalDelivered = 0;
        foreach (var returned in request.Returned.OrderBy(x => x.SaleLineId))
        {
            var original = sold[returned.SaleLineId];
            var previousQuantity = returnedBefore.GetValueOrDefault(original.Id);
            var net = SaleChangeAvailability.HistoricalReturnValue(original.PrecioUnitario,
                original.ValorDescuento, original.Cantidad, previousQuantity, returned.Quantity);
            var gross = checked(original.PrecioUnitario * returned.Quantity);
            detailRows.Add(NewChangeDetail(original.ProductoId, original.Id, "DEVUELTO",
                original.CodigoProducto, original.NombreProducto, original.UnidadMedida,
                returned.Quantity, original.PrecioUnitario, gross - net,
                sale.IvaDiscriminadoCompleto && original.PorcentajeIva is > 0
                    ? original.PorcentajeIva : null, net));
            totalReturned = CashMoney.Add(totalReturned, net);
        }
        foreach (var delivered in request.Delivered.OrderBy(x => x.ProductId))
        {
            var product = products[delivered.ProductId];
            var price = NormalizeCode(delivered.PriceType) == "MAYORISTA"
                ? product.PrecioMayorista : product.PrecioMinorista;
            // Decisión del 15 de noviembre de 2026: cuentan solo las unidades de ese producto que se entregan en el cambio.
            if (NormalizeCode(delivered.PriceType) == "MAYORISTA" && delivered.Quantity < establishment.MinimoUnidadesMayorista)
                return Result.Failure<SaleChangeDto>(ApplicationError.Validation(
                    $"El precio mayorista exige entregar al menos {establishment.MinimoUnidadesMayorista} unidades de {product.CodigoInterno}."));
            if (!product.Activo || price is null || product.CostoPromedio is null ||
                price.Value < product.CostoPromedio || price.Value <= 0 ||
                (long)product.StockActual + returnedByProduct.GetValueOrDefault(product.Id) -
                    product.StockReservado < delivered.Quantity ||
                price.Value > CashMoney.Maximum / delivered.Quantity)
                return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("El producto entregado no tiene precio, costo o stock disponible."));
            var net = CashMoney.Require(checked(price.Value * delivered.Quantity), false);
            detailRows.Add(NewChangeDetail(product.Id, null, "ENTREGADO", product.CodigoInterno,
                product.Nombre, unitNames[product.UnidadMedidaId], delivered.Quantity,
                price.Value, 0m, sale.IvaDiscriminadoCompleto &&
                    sale.DetalleVenta.FirstOrDefault()?.PorcentajeIva is > 0
                    ? sale.DetalleVenta.First().PorcentajeIva : null, net));
            totalDelivered = CashMoney.Add(totalDelivered, net);
        }
        var difference = CashMoney.Subtract(totalDelivered, totalReturned);
        var credit = sale.TipoVenta == "CREDITO"
            ? await repository.GetSaleCreditForUpdateAsync(sale.Id, ct) : null;
        if (sale.TipoVenta == "CREDITO" && (credit is null || credit.Estado is not ("PENDIENTE" or "VENCIDO") ||
            request.ExpectedCreditVersion != credit.Version))
            return Result.Failure<SaleChangeDto>(ApplicationError.ConcurrencyConflict("El crédito fue modificado o no admite cambios."));
        if (sale.TipoVenta == "CONTADO" && request.ExpectedCreditVersion is not null)
            return Result.Failure<SaleChangeDto>(Validation("Una venta de contado no admite versión de crédito."));
        var treatment = ResolveChangeTreatment(sale.TipoVenta, difference, input.Treatment, credit);
        if (treatment.Error is not null) return Result.Failure<SaleChangeDto>(treatment.Error);
        var paymentCheck = await ValidateChangePaymentsAsync(sale, shift.Id, difference,
            treatment.CashAmount, input.Payments, ct);
        if (paymentCheck.Error is not null) return Result.Failure<SaleChangeDto>(paymentCheck.Error);

        Usuario? authorizer;
        if (administratorSelfAuthorized)
        {
            authorizer = await repository.GetUserForUpdateAsync(actor.UserId, ct);
            if (authorizer is null || !authorizer.IsActive)
                return Result.Failure<SaleChangeDto>(ApplicationError.Forbidden());
        }
        else
        {
            var candidate = await userRepository.GetByUserNameAsync(request.Authorization!.UserName!.Trim(), ct);
            if (candidate is null)
                return Result.Failure<SaleChangeDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            authorizer = await repository.GetUserForUpdateAsync(candidate.Id, ct);
            var authorizerSnapshot = authorizer is null ? null
                : await userRepository.GetAuthorizationSnapshotAsync(authorizer.Id, ct);
            if (authorizer is null || !authorizer.IsActive || authorizer.Id == actor.UserId ||
                authorizerSnapshot is null ||
                !RoleHierarchy.IsHigherThan(authorizerSnapshot.HighestRoleLevel, snapshot.HighestRoleLevel) ||
                !authorizerSnapshot.PermissionCodes.Contains(PermissionCodes.SalesChangeAuthorize,
                    StringComparer.OrdinalIgnoreCase))
                return Result.Failure<SaleChangeDto>(ApplicationError.Forbidden("El autorizador debe tener un rol estrictamente superior al del ejecutor."));
            if (!request.Authorization.UsesCredential() && !passwordHasher.Verify(request.Authorization.Password!, authorizer.PasswordHash))
            {
                var locked = authorizer.RegisterFailedLogin(now);
                await userRepository.UpdateAsync(authorizer, ct);
                if (locked)
                    await AuditWriter.WriteAsync(auditRepository, clock, authorizer.Id, AuditAction.UserLocked,
                        nameof(Usuario), authorizer.Id, new { authorizer.Id, authorizer.FailedLoginAttempts }, ct);
                await AuditWriter.WriteCashFailureAsync(auditRepository, clock, actor.UserId,
                    AuditAction.SaleChangeAuthorizationRejected, nameof(Venta), sale.Id,
                    new { SaleId = sale.Id, Result = "INVALID_AUTHORIZATION" }, Guid.CreateVersion7(),
                    terminal.Id, null, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Failure<SaleChangeDto>(ApplicationError.Unauthorized("Credenciales de autorización inválidas."));
            }
        }
        // Un Cajero solo entrega a precio mayorista si su autorizador también puede autorizar descuentos.
        var wholesaleNeedsAuthorization = snapshot.HighestRoleLevel == RoleLevel.Cajero &&
            request.Delivered.Any(x => NormalizeCode(x.PriceType) == "MAYORISTA");
        if (wholesaleNeedsAuthorization)
        {
            var wholesaleAuthorizer = await userRepository.GetAuthorizationSnapshotAsync(authorizer.Id, ct);
            if (wholesaleAuthorizer is null || !wholesaleAuthorizer.PermissionCodes.Contains(
                    PermissionCodes.SalesDiscountAuthorize, StringComparer.OrdinalIgnoreCase))
                return Result.Failure<SaleChangeDto>(ApplicationError.Forbidden(
                    "El precio mayorista de un Cajero requiere un autorizador con permiso para autorizar descuentos."));
        }
        var permission = await permissionRepository.GetByCodeAsync(PermissionCodes.SalesChangeAuthorize, ct);
        if (permission is null || !permission.Activo)
            return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("El permiso de autorización no está disponible."));

        var correlation = Guid.CreateVersion7();
        if (!administratorSelfAuthorized)
        {
            authorizer.RegisterSuccessfulLogin(now);
            await userRepository.UpdateAsync(authorizer, ct);
        }
        var authorization = new AutorizacionOperacion
        {
            Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
            UsuarioAutorizadorId = authorizer.Id, PermisoId = permission.Id,
            TipoOperacion = "CAMBIO_VENTA", CorrelacionId = correlation,
            DescripcionOperacion = administratorSelfAuthorized
                ? "Autorización de cambio de venta por Administrador sin segundo autorizador."
                : "Autorización de cambio de productos de venta.",
            Motivo = input.Reason, MetodoAutenticacion = request.Authorization.AuthenticationMethod(),
            CredencialAutorizadorId = request.Authorization?.ResolvedCredentialId, Resultado = "EXITOSA",
            Estado = "UTILIZADA", FechaSolicitud = now.UtcDateTime,
            FechaExpiracion = now.UtcDateTime.AddMinutes(5), FechaUtilizacion = now.UtcDateTime
        };
        await repository.AddAuthorizationAsync(authorization, ct);
        if (wholesaleNeedsAuthorization)
        {
            var wholesalePermission = await permissionRepository.GetByCodeAsync(PermissionCodes.SalesDiscountAuthorize, ct);
            if (wholesalePermission is null || !wholesalePermission.Activo)
                return Result.Failure<SaleChangeDto>(ApplicationError.Conflict("El permiso de autorización de descuentos no está activo."));
            await repository.AddAuthorizationAsync(new AutorizacionOperacion
            {
                Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
                UsuarioAutorizadorId = authorizer.Id, PermisoId = wholesalePermission.Id,
                TipoOperacion = "PRECIO_MAYORISTA", CorrelacionId = correlation,
                DescripcionOperacion = "Autorización de precio mayorista de un Cajero en un cambio de venta.",
                MetodoAutenticacion = request.Authorization.AuthenticationMethod(),
                CredencialAutorizadorId = request.Authorization?.ResolvedCredentialId, Resultado = "EXITOSA",
                Estado = "UTILIZADA", FechaSolicitud = now.UtcDateTime,
                FechaExpiracion = now.UtcDateTime.AddMinutes(5), FechaUtilizacion = now.UtcDateTime
            }, ct);
        }
        sequence.UltimoNumero++;
        var change = new CambioVenta
        {
            Id = Guid.CreateVersion7(), VentaId = sale.Id, CreditoId = credit?.Id,
            InstalacionId = installation.Id, TurnoCajaId = shift.Id,
            AutorizacionOperacionId = authorization.Id, UsuarioId = actor.UserId,
            SesionOperadorId = session?.Id, Prefijo = sequence.Prefijo, Serie = sequence.Serie,
            Consecutivo = sequence.UltimoNumero, FechaHora = now.UtcDateTime,
            Motivo = input.Reason, TotalDevuelto = totalReturned, TotalEntregado = totalDelivered,
            Diferencia = difference, TratamientoDiferencia = treatment.Name,
            ValorAjusteCredito = treatment.CreditAmount,
            ValorPagoODevolucion = treatment.CashAmount, Estado = "CONFIRMADO",
            CorrelacionId = correlation
        };
        foreach (var detail in detailRows) detail.CambioVentaId = change.Id;
        await repository.AddChangeAsync(change, ct);
        await repository.AddChangeDetailsAsync(detailRows, ct);
        repository.MarkSaleChanged(sale);
        await ApplyChangeInventoryAsync(actor.UserId, authorization.Id, change.Id,
            session?.Id, correlation, now.UtcDateTime, request, sold, products, ct);
        // The official immediate trigger validates the credit's pending state when the
        // change header is inserted. Persist it before a reduction can mark the credit paid.
        // The coordinator still owns the transaction; deferred checks run at COMMIT.
        if (credit is not null && treatment.CreditAmount > 0)
            await unitOfWork.SaveChangesAsync(ct);
        if (credit is not null && treatment.CreditAmount > 0)
            await ApplyChangeCreditAsync(actor.UserId, change.Id, correlation, now.UtcDateTime,
                localDate, credit, difference, treatment.CreditAmount, ct);
        var paymentRows = await ApplyChangePaymentsAsync(actor.UserId, shift.Id, session?.Id,
            change.Id, correlation, now, difference, input.Payments, paymentCheck.Methods, ct);
        var response = new SaleChangeDto(change.Id, sale.Id,
            $"{change.Prefijo}-{change.Serie}-{change.Consecutivo}", change.Estado, now,
            totalReturned, totalDelivered, difference, treatment.Name,
            treatment.CreditAmount, treatment.CashAmount, correlation,
            detailRows.Select(MapChangeDetail).ToArray(), paymentRows.Select(x =>
                MapChangePayment(x, paymentCheck.Methods[x.MetodoPagoId])).ToArray(), input.Reason);
        await repository.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, SesionOperadorId = session?.Id,
            ClaveIdempotencia = input.Key, TipoOperacion = "CAMBIAR_VENTA", HashSolicitud = requestHash,
            Entidad = nameof(Venta), EntidadId = sale.Id,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlation,
            FechaRegistro = now.UtcDateTime
        }, ct);
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId,
            AuditAction.SaleChanged, nameof(CambioVenta), change.Id,
            new { SaleId = sale.Id, ChangeId = change.Id, change.Diferencia,
                change.TratamientoDiferencia, AuthorizationId = authorization.Id, CorrelationId = correlation,
                ResellableConfirmed = true,
                AuthorizationMode = administratorSelfAuthorized ? "ADMINISTRATOR_SELF" : "OTHER_USER" },
            correlation, authorizer.Id, session?.Id, terminal.Id, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
        if (session is not null)
        {
            session.Close(now, "CAMBIO_OPERADOR");
            await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId,
                AuditAction.OperatorSessionClosed, nameof(SesionOperador), session.Id,
                new { session.Estado, ChangeId = change.Id, SessionConsumedBySaleChange = true },
                correlation, null, session.Id, terminal.Id, null, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        return Result.Success(response);
    }

    private static DetalleCambioVenta NewChangeDetail(Guid productId, Guid? originalLineId,
        string type, string code, string name, string unit, int quantity, decimal unitPrice,
        decimal discount, decimal? taxRate, decimal net)
    {
        decimal? taxBase = taxRate is decimal percentage
            ? decimal.Round(net * 100m / (100m + percentage), 0, MidpointRounding.AwayFromZero)
            : null;
        return new DetalleCambioVenta
        {
            Id = Guid.CreateVersion7(), DetalleVentaOrigenId = originalLineId, ProductoId = productId,
            TipoDetalle = type, CodigoProducto = code, NombreProducto = name, UnidadMedida = unit,
            Cantidad = quantity, PrecioUnitario = unitPrice, PorcentajeIva = taxRate,
            ValorDescuento = discount, BaseGravable = taxBase,
            ValorIva = taxBase is decimal basis ? net - basis : null, SubtotalNeto = net
        };
    }
}
