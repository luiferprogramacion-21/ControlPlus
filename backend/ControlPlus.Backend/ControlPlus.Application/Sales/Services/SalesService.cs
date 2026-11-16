using ControlPlus.Domain.Security;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Application.Credits.Ports;
using ControlPlus.Application.Credits.Services;
using ControlPlus.Domain.Common;
using ControlPlus.Domain.Credits;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Sales;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Sales.Services;

public sealed partial class SalesService(
    ISalesRepository repository,
    ISalesTransactionManager transactionManager,
    IUnitOfWork unitOfWork,
    IUserRepository userRepository,
    IAuditRepository auditRepository,
    ICashRepository cashRepository,
    ILoginAttemptCoordinator loginAttemptCoordinator,
    IPasswordHasher passwordHasher,
    IPermissionChecker permissionChecker,
    IPermissionRepository permissionRepository,
    IAuthorizerCredentialResolver credentialResolver,
    IClock clock,
    ICustomerCreditExposureReader creditExposure) : ISalesService
{
    private const int MaximumPageSize = 200;
    private static readonly HashSet<string> ApprovedPaymentMethods =
        new(["EFECTIVO", "TRANSFERENCIA", "NEQUI"], StringComparer.Ordinal);

    public async Task<Result<SaleDto>> ConfirmAsync(
        ActorContext actor,
        string idempotencyKey,
        ConfirmSaleRequest request,
        CancellationToken cancellationToken = default)
    {
        var input = NormalizeInput(idempotencyKey, request);
        if (input.Error is not null) return Result.Failure<SaleDto>(input.Error);

        // Credentials of a second user (credit, or a Cajero's discount without a reason) serialize per account.
        if (input.SaleType == "CREDITO" || request.Authorization is not null)
            return await loginAttemptCoordinator.ExecuteAsync(
                request.Authorization?.IdentityKey ?? actor.UserId.ToString("N"),
                token => ConfirmWithinTransactionAsync(actor, request, input, token), cancellationToken);

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var result = await ConfirmWithinTransactionAsync(actor, request, input, cancellationToken);
        if (result.IsSuccess) await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<Result<SaleDto>> ConfirmWithinTransactionAsync(
        ActorContext actor, ConfirmSaleRequest request, NormalizedInput input, CancellationToken cancellationToken)
    {
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Result.Failure<SaleDto>(ApplicationError.NotFound("la instalación activa"));

        var authorization = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (authorization is null) return Result.Failure<SaleDto>(ApplicationError.Unauthorized());
        if (!authorization.PermissionCodes.Contains(PermissionCodes.SalesCreate, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleDto>(ApplicationError.Forbidden());
        if (input.SaleType == "CREDITO" &&
            !authorization.PermissionCodes.Contains(PermissionCodes.CreditRequest, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleDto>(ApplicationError.Forbidden());
        var creditSelfMode = input.SaleType == "CREDITO" && request.Authorization is null
            ? authorization.HighestRoleLevel switch
            {
                RoleLevel.Administrador => "ADMINISTRATOR_SELF",
                RoleLevel.Supervisor => "SUPERVISOR_SELF",
                _ => null
            }
            : null;
        if (input.SaleType == "CREDITO" && request.Authorization is null && creditSelfMode is null)
            return Result.Failure<SaleDto>(Validation("El Cajero debe enviar authorization para una venta a crédito."));
        if (creditSelfMode is not null &&
            !authorization.PermissionCodes.Contains(PermissionCodes.CreditAuthorize, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleDto>(ApplicationError.Forbidden());

                var hasDiscountRequest = input.GeneralDiscount is not null || input.Lines!.Any(x => x.Discount is not null);
        var hasWholesaleRequest = input.Lines!.Any(x => x.PriceType == "MAYORISTA");
        // A Cajero always needs the authorization of a higher role to discount or to sell at the wholesale price.
        var concessionNeedsAuthorization = (hasDiscountRequest || hasWholesaleRequest) &&
            authorization.HighestRoleLevel == RoleLevel.Cajero;
        if (input.SaleType == "CONTADO" && request.Authorization is not null && !concessionNeedsAuthorization)
            return Result.Failure<SaleDto>(Validation(
                "Una venta de contado solo admite authorization de un Cajero que aplica descuentos o precio mayorista."));
        if (input.SaleType == "CONTADO" && request.Authorization is null && concessionNeedsAuthorization)
            return Result.Failure<SaleDto>(Validation(
                "El Cajero debe enviar authorization de un Supervisor o Administrador para aplicar descuentos o precio mayorista."));

        var requestHash = Hash(input.Canonical! + (creditSelfMode is null ? "" : "|" + creditSelfMode));
        var existingOperation = await repository.GetIdempotencyAsync(installation.Id, input.Key!, cancellationToken);
        if (existingOperation is not null)
        {
            if (existingOperation.UsuarioId != actor.UserId || existingOperation.TipoOperacion != "REGISTRAR_VENTA" ||
                existingOperation.HashSolicitud != requestHash ||
                existingOperation.EntidadId is not Guid existingSaleId)
                return Result.Failure<SaleDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            var existingSale = await repository.GetSaleAsync(existingSaleId, cancellationToken);
            return existingSale is null
                ? Result.Failure<SaleDto>(ApplicationError.Conflict("La operación idempotente no tiene una venta recuperable."))
                : Result.Success(Map(existingSale));
        }

        var establishment = await repository.GetActiveEstablishmentAsync(cancellationToken);
        if (establishment is null) return Result.Failure<SaleDto>(ApplicationError.NotFound("el establecimiento activo"));
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null) return Result.Failure<SaleDto>(ApplicationError.NotFound("la terminal activa"));
        var shift = await repository.GetOpenShiftForUpdateAsync(cancellationToken);
        if (shift is null) return Result.Failure<SaleDto>(ApplicationError.Conflict("Debe existir un turno de caja abierto para confirmar la venta."));
        if (shift.TerminalId != terminal.Id)
            return Result.Failure<SaleDto>(ApplicationError.Conflict("El turno abierto no corresponde a la terminal activa."));

        SesionOperador? operatorSession = null;
        if (shift.ModoOperacion == CashConstants.IndividualMode)
        {
            if (shift.UsuarioResponsableId != actor.UserId)
                return Result.Failure<SaleDto>(ApplicationError.Forbidden("El turno individual pertenece a otro responsable."));
        }
        else if (shift.ModoOperacion == CashConstants.SharedMode)
        {
            operatorSession = await repository.GetActiveOperatorSessionAsync(shift.Id, terminal.Id, actor.UserId, cancellationToken);
            if (operatorSession is null)
                return Result.Failure<SaleDto>(ApplicationError.Conflict("La modalidad compartida exige una sesión activa del operador."));
        }
        else
        {
            return Result.Failure<SaleDto>(ApplicationError.Conflict("El turno abierto tiene una modalidad no admitida."));
        }

        if (input.SaleType == "CREDITO" &&
            (request.DueDate is null || request.DueDate.Value.DayNumber <= shift.FechaOperativa.DayNumber ||
             request.DueDate.Value.DayNumber > shift.FechaOperativa.DayNumber + 90))
            return Result.Failure<SaleDto>(Validation("El vencimiento debe estar entre mañana y 90 días desde la fecha operativa."));

        Cliente? receiptCustomer = null;
        if (request.CustomerId is Guid customerId)
        {
            var customer = await repository.GetCustomerAsync(customerId, cancellationToken);
            if (customer is null || !customer.Activo)
                return Result.Failure<SaleDto>(ApplicationError.NotFound("el cliente activo"));
            receiptCustomer = customer;
            if (input.SaleType == "CREDITO" &&
                (string.IsNullOrWhiteSpace(customer.TipoDocumento) || string.IsNullOrWhiteSpace(customer.NumeroDocumento)) &&
                string.IsNullOrWhiteSpace(customer.Telefono))
                return Result.Failure<SaleDto>(Validation("El cliente requiere documento completo o teléfono para crédito."));
        }
        else if (input.SaleType == "CREDITO")
            return Result.Failure<SaleDto>(Validation("El cliente es obligatorio para una venta a crédito."));

        Usuario? creditAuthorizer = null;
        Guid? creditCredentialId = null;
        Guid? concessionCredentialId = null;
        if (input.SaleType == "CREDITO")
        {
            if (creditSelfMode is not null)
            {
                creditAuthorizer = await repository.GetUserForUpdateAsync(actor.UserId, cancellationToken);
                if (creditAuthorizer is null || !creditAuthorizer.IsActive)
                    return Result.Failure<SaleDto>(ApplicationError.Forbidden());
            }
            else
            {
                (creditAuthorizer, creditCredentialId) = await VerifyAuthorizerAsync(request.Authorization!, actor.UserId,
                    authorization.HighestRoleLevel, terminal.Id, true, cancellationToken);
                if (creditAuthorizer is null) return Result.Failure<SaleDto>(ApplicationError.Forbidden());
                if (!await permissionChecker.HasPermissionAsync(creditAuthorizer.Id, PermissionCodes.CreditAuthorize, cancellationToken))
                    return Result.Failure<SaleDto>(ApplicationError.Forbidden());
                // The same credentials also authorize a Cajero's discount or wholesale price.
                if (concessionNeedsAuthorization &&
                    !await permissionChecker.HasPermissionAsync(creditAuthorizer.Id, PermissionCodes.SalesDiscountAuthorize, cancellationToken))
                    return Result.Failure<SaleDto>(ApplicationError.Forbidden());
            }
        }

        // Cash sale: a Cajero's discount or wholesale price needs a second user of a strictly higher role.
        Usuario? concessionAuthorizer = null;
        if (input.SaleType == "CONTADO" && concessionNeedsAuthorization)
        {
            (concessionAuthorizer, concessionCredentialId) = await VerifyAuthorizerAsync(request.Authorization!, actor.UserId,
                authorization.HighestRoleLevel, terminal.Id, false, cancellationToken);
            if (concessionAuthorizer is null) return Result.Failure<SaleDto>(ApplicationError.Forbidden());
            if (!await permissionChecker.HasPermissionAsync(concessionAuthorizer.Id, PermissionCodes.SalesDiscountAuthorize, cancellationToken))
                return Result.Failure<SaleDto>(ApplicationError.Forbidden());
        }

        var sequence = await repository.GetSaleSequenceForUpdateAsync(installation.Id, installation.Serie, cancellationToken);
        if (sequence is null)
            return Result.Failure<SaleDto>(ApplicationError.Conflict("Debe configurar el consecutivo VENTA para la serie activa antes de vender."));
        if (sequence.UltimoNumero == long.MaxValue)
            return Result.Failure<SaleDto>(ApplicationError.NumberingExhausted(
                "El consecutivo de VENTAS alcanzó el número máximo admitido."));

        var normalizedLines = input.Lines!;
        var products = await repository.GetProductsForUpdateAsync(normalizedLines.Select(x => x.ProductId).ToArray(), cancellationToken);
        if (products.Count != normalizedLines.Count)
            return Result.Failure<SaleDto>(ApplicationError.NotFound("uno o más productos"));
        var productsById = products.ToDictionary(x => x.Id);
        var unitNames = await repository.GetMeasurementUnitNamesAsync(products.Select(x => x.UnidadMedidaId).Distinct().ToArray(), cancellationToken);

        var maximumDiscount = SalePricingPolicy.MaximumDiscountPercentage(authorization.HighestRoleLevel);
        var hasAnyDiscount = input.GeneralDiscount is not null || normalizedLines.Any(x => x.Discount is not null);
        if (hasAnyDiscount && !authorization.PermissionCodes.Contains(PermissionCodes.SalesDiscountApply, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleDto>(ApplicationError.Forbidden("No tiene permiso efectivo para aplicar descuentos."));

        PreparedSale prepared;
        try
        {
            prepared = PrepareSale(normalizedLines, input.GeneralDiscount, productsById, maximumDiscount,
                establishment.VatEnabled ? establishment.VatPercentage : 0m, establishment.MinimoUnidadesMayorista);
        }
        catch (DomainRuleViolationException exception)
        {
            return Result.Failure<SaleDto>(Validation(exception.Message));
        }
        catch (CashMoneyException)
        {
            return Result.Failure<SaleDto>(Validation("Los importes deben ser pesos COP enteros dentro del rango permitido."));
        }
        catch (OverflowException)
        {
            return Result.Failure<SaleDto>(Validation("La cantidad o el importe acumulado excede el rango permitido."));
        }

        if (prepared.Total <= 0)
            return Result.Failure<SaleDto>(Validation("La venta debe tener un total mayor que cero."));

        // Morosos y cupo (decisión del 16 de noviembre de 2026): siempre se avisa en pantalla; con el bloqueo activo,
        // solo un Administrador autoriza la excepción (el propio vendedor si es Administrador, o quien da sus credenciales).
        CreditRiskAssessment? creditRisk = null;
        string? creditRiskException = null;
        if (input.SaleType == "CREDITO" && receiptCustomer is not null)
        {
            var exposure = await creditExposure.GetAsync(receiptCustomer.Id,
                CreditSettingsService.LocalToday(establishment, clock), cancellationToken);
            creditRisk = CreditRiskPolicy.Assess(exposure.OutstandingBalance, exposure.OverdueBalance,
                receiptCustomer.CupoCredito, prepared.Total);
            if (creditRisk.HasRisk && establishment.CreditoBloquearRiesgo)
            {
                var authorizerLevel = creditSelfMode switch
                {
                    "ADMINISTRATOR_SELF" => RoleLevel.Administrador,
                    "SUPERVISOR_SELF" => RoleLevel.Supervisor,
                    _ => (await userRepository.GetAuthorizationSnapshotAsync(creditAuthorizer!.Id, cancellationToken))?.HighestRoleLevel
                };
                if (authorizerLevel != RoleLevel.Administrador)
                {
                    var why = creditRisk.Overdue && creditRisk.ExceedsLimit ? "tiene saldo vencido y supera su cupo"
                        : creditRisk.Overdue ? "tiene saldo vencido" : "supera su cupo de crédito";
                    return Result.Failure<SaleDto>(ApplicationError.Forbidden(
                        $"El cliente {why} y el bloqueo de créditos está activo: la venta requiere la autorización de un Administrador."));
                }
                creditRiskException = creditSelfMode ?? "ADMINISTRATOR_AUTHORIZER";
            }
        }

        var normalizedPayments = input.Payments!;
        var paymentCodes = normalizedPayments.Select(x => x.MethodCode).ToArray();
        var methods = await repository.GetPaymentMethodsAsync(paymentCodes, cancellationToken);
        var methodsByCode = methods.ToDictionary(x => x.Codigo, StringComparer.Ordinal);
        if (methods.Count != paymentCodes.Length || methods.Any(x => !x.Activo))
            return Result.Failure<SaleDto>(Validation("Todos los métodos de pago deben existir y estar activos."));
        if ((methodsByCode.TryGetValue("EFECTIVO", out var cashMethod) && !cashMethod.AfectaEfectivo) ||
            methods.Where(x => x.Codigo is "TRANSFERENCIA" or "NEQUI").Any(x => x.AfectaEfectivo))
            return Result.Failure<SaleDto>(ApplicationError.Conflict(
                "El catálogo de métodos de pago no conserva la clasificación oficial de efectivo y electrónicos."));

        ApplicationError? paymentValidation;
        try
        {
            paymentValidation = ValidatePayments(normalizedPayments, methodsByCode, prepared.Total);
        }
        catch (CashMoneyException)
        {
            paymentValidation = Validation("Los importes deben ser pesos COP enteros dentro del rango permitido.");
        }
        if (input.SaleType == "CONTADO" && paymentValidation is not null) return Result.Failure<SaleDto>(paymentValidation);

        if (establishment.DiasLimiteCambio > DateOnly.MaxValue.DayNumber - shift.FechaOperativa.DayNumber)
            return Result.Failure<SaleDto>(ApplicationError.Conflict(
                "El plazo de cambio configurado excede el rango de fechas de la venta."));

        var now = clock.UtcNow;
        var correlationId = Guid.CreateVersion7();
        sequence.UltimoNumero++;
        var sale = new Venta
        {
            Id = Guid.CreateVersion7(),
            InstalacionId = installation.Id,
            TurnoCajaId = shift.Id,
            UsuarioId = actor.UserId,
            SesionOperadorId = operatorSession?.Id,
            ClienteId = request.CustomerId,
            Prefijo = sequence.Prefijo,
            Serie = sequence.Serie,
            Consecutivo = sequence.UltimoNumero,
            FechaHora = now.UtcDateTime,
            FechaOperativa = shift.FechaOperativa,
            FechaLimiteCambio = shift.FechaOperativa.AddDays(establishment.DiasLimiteCambio),
            TipoVenta = input.SaleType!,
            AmbitoDescuento = prepared.DiscountScope,
            TipoDescuento = input.GeneralDiscount?.Type,
            ValorDescuentoSolicitado = input.GeneralDiscount?.Value,
            MotivoDescuento = hasAnyDiscount ? NormalizeOptional(request.DiscountReason) : null,
            SubtotalBruto = prepared.GrossSubtotal,
            DescuentoTotal = prepared.DiscountTotal,
            BaseGravableTotal = prepared.HasCompleteTax ? prepared.Lines.Sum(x => x.TaxBase!.Value) : null,
            ImpuestoIncluidoTotal = prepared.HasCompleteTax ? prepared.Lines.Sum(x => x.TaxAmount!.Value) : null,
            IvaDiscriminadoCompleto = prepared.HasCompleteTax,
            Total = prepared.Total,
            Estado = "CONFIRMADA",
            Version = 1
        };

        var cashMovement = input.SaleType == "CONTADO" ? MovimientoCaja.Create(
            shift.Id,
            actor.UserId,
            operatorSession?.Id,
            CashConstants.IncomeMovement,
            "VENTA",
            sale.Total,
            $"Venta {VisibleNumber(sale)}",
            correlationId,
            now) : null;

        var details = new List<DetalleVenta>(prepared.Lines.Count);
        var inventoryMovements = new List<MovimientoInventario>(prepared.Lines.Count);
        foreach (var line in prepared.Lines.OrderBy(x => x.Product.Id))
        {
            var product = line.Product;
            var previousStock = product.StockActual;
            product.StockActual = checked(product.StockActual - line.Quantity);
            details.Add(new DetalleVenta
            {
                Id = Guid.CreateVersion7(),
                VentaId = sale.Id,
                ProductoId = product.Id,
                CodigoProducto = product.CodigoInterno,
                NombreProducto = product.Nombre,
                UnidadMedida = unitNames[product.UnidadMedidaId],
                Cantidad = line.Quantity,
                TipoPrecio = line.PriceType,
                PrecioUnitario = line.UnitPrice,
                CostoUnitario = product.CostoPromedio,
                PorcentajeIva = establishment.VatEnabled ? establishment.VatPercentage : 0m,
                SubtotalBruto = line.GrossAmount,
                OrigenDescuento = line.DiscountOrigin,
                TipoDescuento = line.DiscountType,
                PorcentajeDescuento = line.DiscountPercentage,
                ValorDescuento = line.DiscountAmount,
                BaseGravable = line.TaxBase,
                ValorIva = line.TaxAmount,
                SubtotalNeto = line.NetAmount
            });
            inventoryMovements.Add(new MovimientoInventario
            {
                Id = Guid.CreateVersion7(),
                ProductoId = product.Id,
                UsuarioId = actor.UserId,
                SesionOperadorId = operatorSession?.Id,
                VentaId = sale.Id,
                TipoMovimiento = "VENTA",
                CantidadStock = -line.Quantity,
                CantidadReservada = 0,
                StockAnterior = previousStock,
                StockPosterior = product.StockActual,
                ReservadoAnterior = product.StockReservado,
                ReservadoPosterior = product.StockReservado,
                CostoPromedioAnterior = product.CostoPromedio,
                CostoPromedioPosterior = product.CostoPromedio,
                FechaHora = now.UtcDateTime,
                CorrelacionId = correlationId
            });
        }

        var payments = normalizedPayments.Select(payment =>
        {
            var method = methodsByCode[payment.MethodCode];
            var received = payment.ReceivedAmount;
            return new PagoVenta
            {
                Id = Guid.CreateVersion7(),
                VentaId = sale.Id,
                MetodoPagoId = method.Id,
                MovimientoCajaId = cashMovement!.Id,
                UsuarioId = actor.UserId,
                SesionOperadorId = operatorSession?.Id,
                ValorAplicado = payment.AppliedAmount,
                ValorRecibido = received,
                Cambio = received is null ? 0m : received.Value - payment.AppliedAmount,
                Referencia = NormalizeOptional(payment.Reference),
                FechaHora = now.UtcDateTime,
                Estado = "APLICADO"
            };
        }).ToArray();

        Credito? credit = null;
        if (input.SaleType == "CREDITO")
        {
            var permission = await permissionRepository.GetByCodeAsync(PermissionCodes.CreditAuthorize, cancellationToken);
            if (permission is null || !permission.Activo)
                return Result.Failure<SaleDto>(ApplicationError.Conflict("El permiso de autorización de crédito no está activo."));
            var operationAuthorization = new AutorizacionOperacion
            {
                Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
                UsuarioAutorizadorId = creditAuthorizer!.Id, PermisoId = permission.Id,
                TipoOperacion = "CREAR_CREDITO", CorrelacionId = correlationId,
                DescripcionOperacion = creditSelfMode switch
                {
                    "ADMINISTRATOR_SELF" => "Autorización de venta a crédito por Administrador sin segundo autorizador.",
                    "SUPERVISOR_SELF" => "Autorización de venta a crédito por Supervisor sin segundo autorizador.",
                    _ => "Autorización de venta a crédito."
                }, MetodoAutenticacion = creditCredentialId is null ? "PASSWORD" : "CODIGO_BARRAS_PIN", CredencialAutorizadorId = creditCredentialId,
                Resultado = "EXITOSA", Estado = "UTILIZADA", FechaSolicitud = now.UtcDateTime,
                FechaExpiracion = now.AddMinutes(5).UtcDateTime, FechaUtilizacion = now.UtcDateTime
            };
            credit = new Credito
            {
                Id = Guid.CreateVersion7(), VentaId = sale.Id, AutorizacionOperacionId = operationAuthorization.Id,
                MontoOriginal = sale.Total, MontoAjustado = sale.Total, SaldoPendiente = sale.Total,
                FechaOtorgamiento = shift.FechaOperativa, FechaVencimiento = request.DueDate!.Value,
                Estado = "PENDIENTE", Version = 1
            };
            if (creditSelfMode is null)
            {
                creditAuthorizer.RegisterSuccessfulLogin(now);
                await userRepository.UpdateAsync(creditAuthorizer, cancellationToken);
            }
            await repository.AddAuthorizationAsync(operationAuthorization, cancellationToken);
            await repository.AddCreditAsync(credit, cancellationToken);
        }

        // Record who authorized a Cajero's discount and/or wholesale price (same user as the credit authorizer on a credit).
        var concessionAuthorizerUser = concessionAuthorizer ?? (concessionNeedsAuthorization ? creditAuthorizer : null);
        if (concessionAuthorizerUser is not null)
        {
            var concessionPermission = await permissionRepository.GetByCodeAsync(PermissionCodes.SalesDiscountAuthorize, cancellationToken);
            if (concessionPermission is null || !concessionPermission.Activo)
                return Result.Failure<SaleDto>(ApplicationError.Conflict("El permiso de autorización de descuentos no está activo."));
            var concessionKinds = new List<(string Type, string Description)>();
            if (hasDiscountRequest) concessionKinds.Add(("DESCUENTO_VENTA", "Autorización de descuento de un Cajero."));
            if (hasWholesaleRequest) concessionKinds.Add(("PRECIO_MAYORISTA", "Autorización de precio mayorista de un Cajero."));
            foreach (var (type, description) in concessionKinds)
                await repository.AddAuthorizationAsync(new AutorizacionOperacion
                {
                    Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
                    UsuarioAutorizadorId = concessionAuthorizerUser.Id, PermisoId = concessionPermission.Id,
                    TipoOperacion = type, CorrelacionId = correlationId, DescripcionOperacion = description,
                    MetodoAutenticacion = (concessionCredentialId ?? creditCredentialId) is null ? "PASSWORD" : "CODIGO_BARRAS_PIN",
                    CredencialAutorizadorId = concessionCredentialId ?? creditCredentialId, Resultado = "EXITOSA", Estado = "UTILIZADA",
                    FechaSolicitud = now.UtcDateTime, FechaExpiracion = now.AddMinutes(5).UtcDateTime, FechaUtilizacion = now.UtcDateTime
                }, cancellationToken);
            if (concessionAuthorizer is not null)
            {
                concessionAuthorizer.RegisterSuccessfulLogin(now);
                await userRepository.UpdateAsync(concessionAuthorizer, cancellationToken);
            }
        }

        await repository.AddSaleAsync(sale, cancellationToken);
        await repository.AddSaleDetailsAsync(details, cancellationToken);
        if (cashMovement is not null) await repository.AddCashMovementAsync(cashMovement, cancellationToken);
        if (payments.Length > 0) await repository.AddPaymentsAsync(payments, cancellationToken);
        await repository.AddInventoryMovementsAsync(inventoryMovements, cancellationToken);

        var response = Map(sale, details, payments, methodsByCode) with { CreditId = credit?.Id };
        var receiptVatPercentage = ReceiptVatPercentage(sale, details);
        var (snapshotData, snapshotHash) = SaleReceiptSnapshotCodec.Encode(
            ToReceipt(response, sale.TipoVenta) with
            {
                OperatorName = authorization.DisplayName,
                CustomerName = receiptCustomer?.NombreCompleto,
                VatPercentage = receiptVatPercentage,
                TaxableBaseTotal = receiptVatPercentage is > 0 ? sale.BaseGravableTotal : null,
                VatTotal = receiptVatPercentage is > 0 ? sale.ImpuestoIncluidoTotal : null
            }, response.Payments);
        await repository.AddReceiptSnapshotAsync(new DocumentoEmitido
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            EmitidoPorId = actor.UserId, TipoDocumento = "COMPROBANTE_VENTA",
            EntidadTipo = nameof(Venta), EntidadId = sale.Id, NumeroVisible = response.VisibleNumber,
            VersionDocumento = 1, DatosSnapshot = snapshotData, PlantillaSnapshot = null,
            HashSnapshot = snapshotHash, FechaEmision = now.UtcDateTime
        }, cancellationToken);
        await repository.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(),
            InstalacionId = installation.Id,
            TerminalId = terminal.Id,
            UsuarioId = actor.UserId,
            SesionOperadorId = operatorSession?.Id,
            ClaveIdempotencia = input.Key!,
            TipoOperacion = "REGISTRAR_VENTA",
            HashSolicitud = requestHash,
            Entidad = nameof(Venta),
            EntidadId = sale.Id,
            Resultado = JsonSerializer.Serialize(response),
            CorrelacionId = correlationId,
            FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteSaleAsync(
            auditRepository,
            clock,
            actor.UserId,
            sale.Id,
            new
            {
                sale.Prefijo,
                sale.Serie,
                sale.Consecutivo,
                sale.ClienteId,
                sale.AmbitoDescuento,
                sale.TipoDescuento,
                DiscountReasonPresent = !string.IsNullOrWhiteSpace(sale.MotivoDescuento),
                ConcessionAuthorizedByUserId = concessionAuthorizerUser?.Id,
                sale.TipoVenta,
                ProductCount = details.Count,
                Lines = prepared.Lines.Select(x => new
                {
                    ProductId = x.Product.Id,
                    x.Quantity,
                    x.PriceType,
                    x.DiscountOrigin,
                    x.DiscountType,
                    x.DiscountPercentage
                }).ToArray(),
                PaymentMethods = normalizedPayments.Select(x => x.MethodCode).ToArray()
            },
            correlationId,
            operatorSession?.Id,
            terminal.Id,
            cancellationToken);
        if (credit is not null)
            await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId,
                AuditAction.CreditCreated, nameof(Credito), credit.Id,
                new { credit.Id, credit.VentaId, credit.Estado, CorrelationId = correlationId,
                    AuthorizationMode = creditSelfMode ?? "OTHER_USER",
                    Risk = creditRisk is null || !creditRisk.HasRisk ? null : new
                    {
                        creditRisk.Overdue,
                        creditRisk.ExceedsLimit,
                        Blocked = establishment.CreditoBloquearRiesgo,
                        Exception = creditRiskException
                    } }, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (operatorSession is not null)
        {
            operatorSession.Close(now, "CAMBIO_OPERADOR");
            await AuditWriter.WriteCashAsync(
                auditRepository,
                clock,
                actor.UserId,
                AuditAction.OperatorSessionClosed,
                nameof(SesionOperador),
                operatorSession.Id,
                new { operatorSession.Estado, SaleId = sale.Id, SessionConsumedBySale = true },
                correlationId,
                null,
                operatorSession.Id,
                terminal.Id,
                null,
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result.Success(response);
    }

    public async Task<Result<PagedResult<SaleSummaryDto>>> ListOwnAsync(
        ActorContext actor, SaleListQuery query, CancellationToken cancellationToken = default)
    {
        var authorization = await RequireAsync(actor, PermissionCodes.SalesOwnRead, cancellationToken);
        if (authorization.Error is not null) return Result.Failure<PagedResult<SaleSummaryDto>>(authorization.Error);
        var validation = ValidatePage(query);
        if (validation is not null) return Result.Failure<PagedResult<SaleSummaryDto>>(validation);
        return Result.Success(Map(await repository.ListAsync(actor.UserId, query, cancellationToken)));
    }

    public async Task<Result<PagedResult<SaleSummaryDto>>> ListAllAsync(
        ActorContext actor, SaleListQuery query, CancellationToken cancellationToken = default)
    {
        var authorization = await RequireAsync(actor, PermissionCodes.SalesAllRead, cancellationToken);
        if (authorization.Error is not null) return Result.Failure<PagedResult<SaleSummaryDto>>(authorization.Error);
        var validation = ValidatePage(query);
        if (validation is not null) return Result.Failure<PagedResult<SaleSummaryDto>>(validation);
        return Result.Success(Map(await repository.ListAsync(null, query, cancellationToken)));
    }

    public async Task<Result<SaleDto>> GetAsync(
        ActorContext actor, Guid saleId, CancellationToken cancellationToken = default)
    {
        var authorization = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (authorization is null) return Result.Failure<SaleDto>(ApplicationError.Unauthorized());
        var sale = await repository.GetSaleAsync(saleId, cancellationToken);
        if (sale is null) return Result.Failure<SaleDto>(ApplicationError.NotFound("la venta"));
        var canReadAll = authorization.PermissionCodes.Contains(PermissionCodes.SalesAllRead, StringComparer.OrdinalIgnoreCase);
        var canReadOwn = authorization.PermissionCodes.Contains(PermissionCodes.SalesOwnRead, StringComparer.OrdinalIgnoreCase);
        if (!canReadAll && !(canReadOwn && sale.UsuarioId == actor.UserId))
            return Result.Failure<SaleDto>(ApplicationError.Forbidden());
        return Result.Success(Map(sale));
    }

    public async Task<Result<SaleReceiptDto>> GetReceiptAsync(ActorContext actor, Guid saleId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (authorization is null) return Result.Failure<SaleReceiptDto>(ApplicationError.Unauthorized());
        var sale = await repository.GetReceiptSaleHeaderAsync(saleId, cancellationToken);
        if (sale is null) return Result.Failure<SaleReceiptDto>(ApplicationError.NotFound("la venta"));
        var canReadAll = authorization.PermissionCodes.Contains(PermissionCodes.SalesAllRead, StringComparer.OrdinalIgnoreCase);
        var canReadOwn = authorization.PermissionCodes.Contains(PermissionCodes.SalesOwnRead, StringComparer.OrdinalIgnoreCase);
        if (!canReadAll && !(canReadOwn && sale.UsuarioId == actor.UserId))
            return Result.Failure<SaleReceiptDto>(ApplicationError.Forbidden());
        if (sale.Estado != "CONFIRMADA")
            return Result.Failure<SaleReceiptDto>(ApplicationError.Conflict("La venta no está confirmada."));
        var snapshot = await repository.GetReceiptSnapshotAsync(sale.InstalacionId, sale.Id, cancellationToken);
        if (snapshot is not null)
            return SaleReceiptSnapshotCodec.TryDecode(snapshot, sale.Id, out var historicalReceipt)
                ? Result.Success(historicalReceipt!)
                : Result.Failure<SaleReceiptDto>(ApplicationError.Conflict("El comprobante histórico no está disponible."));
        var legacySale = await repository.GetSaleAsync(saleId, cancellationToken);
        return legacySale is null
            ? Result.Failure<SaleReceiptDto>(ApplicationError.NotFound("la venta"))
            : legacySale.Estado != "CONFIRMADA"
                ? Result.Failure<SaleReceiptDto>(ApplicationError.Conflict("La venta no está confirmada."))
                : Result.Success(ToReceipt(legacySale));
    }

    public async Task<Result<SaleReprintRequestDto>> RequestReprintAsync(ActorContext actor, Guid saleId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            return Result.Failure<SaleReprintRequestDto>(Validation("Idempotency-Key debe tener entre 1 y 100 caracteres."));
        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Result.Failure<SaleReprintRequestDto>(ApplicationError.NotFound("la instalación activa"));
        var authorization = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (authorization is null) return Result.Failure<SaleReprintRequestDto>(ApplicationError.Unauthorized());
        if (!authorization.PermissionCodes.Contains(PermissionCodes.SalesReprint, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleReprintRequestDto>(ApplicationError.Forbidden());
        var sale = await repository.GetSaleAsync(saleId, cancellationToken);
        if (sale is null || sale.InstalacionId != installation.Id)
            return Result.Failure<SaleReprintRequestDto>(ApplicationError.NotFound("la venta"));
        var canReadAll = authorization.PermissionCodes.Contains(PermissionCodes.SalesAllRead, StringComparer.OrdinalIgnoreCase);
        var canReadOwn = authorization.PermissionCodes.Contains(PermissionCodes.SalesOwnRead, StringComparer.OrdinalIgnoreCase);
        if (!canReadAll && !(canReadOwn && sale.UsuarioId == actor.UserId))
            return Result.Failure<SaleReprintRequestDto>(ApplicationError.Forbidden());
        if (sale.Estado != "CONFIRMADA")
            return Result.Failure<SaleReprintRequestDto>(ApplicationError.Conflict("La venta no está confirmada."));
        var key = idempotencyKey.Trim();
        var existing = await repository.GetIdempotencyAsync(installation.Id, key, cancellationToken);
        if (existing is not null)
        {
            if (existing.TipoOperacion != "SOLICITAR_REIMPRESION" || existing.UsuarioId != actor.UserId ||
                existing.EntidadId != saleId || existing.Resultado is null)
                return Result.Failure<SaleReprintRequestDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            var replay = JsonSerializer.Deserialize<SaleReprintRequestDto>(existing.Resultado);
            return replay is null
                ? Result.Failure<SaleReprintRequestDto>(ApplicationError.Conflict("La solicitud anterior no puede recuperarse."))
                : Result.Success(replay);
        }
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null) return Result.Failure<SaleReprintRequestDto>(ApplicationError.NotFound("la terminal activa"));
        var now = clock.UtcNow;
        var response = new SaleReprintRequestDto(Guid.CreateVersion7(), sale.Id, now, "SOLICITADA");
        await repository.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = response.RequestId, InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, ClaveIdempotencia = key, TipoOperacion = "SOLICITAR_REIMPRESION",
            HashSolicitud = Hash($"{actor.UserId:N}:{saleId:N}"), Entidad = nameof(Venta),
            EntidadId = saleId, Resultado = JsonSerializer.Serialize(response),
            CorrelacionId = Guid.CreateVersion7(), FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId,
            AuditAction.SaleReprintRequested, nameof(Venta), saleId,
            new { response.RequestId, Status = "SOLICITADA" }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(response);
    }

    private static SaleReceiptDto ToReceipt(Venta sale)
    {
        var detail = Map(sale);
        var vatPercentage = ReceiptVatPercentage(sale, sale.DetalleVenta);
        return ToReceipt(detail, sale.TipoVenta) with
        {
            VatPercentage = vatPercentage,
            TaxableBaseTotal = vatPercentage is > 0 ? sale.BaseGravableTotal : null,
            VatTotal = vatPercentage is > 0 ? sale.ImpuestoIncluidoTotal : null
        };
    }

    private static decimal? ReceiptVatPercentage(Venta sale, IEnumerable<DetalleVenta> details)
    {
        if (!sale.IvaDiscriminadoCompleto || sale.BaseGravableTotal is null ||
            sale.ImpuestoIncluidoTotal is null) return null;
        var percentages = details.Select(x => x.PorcentajeIva).Distinct().ToArray();
        return percentages.Length == 1 && percentages[0] is > 0 ? percentages[0] : null;
    }

    private static SaleReceiptDto ToReceipt(SaleDto detail, string saleType) =>
        new(detail.Id, detail.VisibleNumber, detail.OccurredAtUtc,
            detail.OperatingDate, saleType, detail.State, detail.GrossSubtotal,
            detail.DiscountTotal, detail.Total, detail.Change, detail.Lines,
            detail.Payments.Select(x => new SaleReceiptPaymentDto(x.MethodCode, x.AppliedAmount,
                x.ReceivedAmount, x.Change)).ToArray());

    private async Task<(UserAuthorizationSnapshot? Snapshot, ApplicationError? Error)> RequireAsync(
        ActorContext actor, string permission, CancellationToken cancellationToken)
    {
        var authorization = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (authorization is null) return (null, ApplicationError.Unauthorized());
        return authorization.PermissionCodes.Contains(permission, StringComparer.OrdinalIgnoreCase)
            ? (authorization, null)
            : (authorization, ApplicationError.Forbidden());
    }

    private static PreparedSale PrepareSale(
        IReadOnlyCollection<NormalizedLine> requestedLines,
        NormalizedDiscount? generalDiscount,
        IReadOnlyDictionary<Guid, Producto> products,
        decimal maximumDiscount, decimal? vatPercentage, int wholesaleMinimumQuantity)
    {
        var lines = new List<PreparedLine>(requestedLines.Count);
        foreach (var requested in requestedLines.OrderBy(x => x.ProductId))
        {
            var product = products[requested.ProductId];
            if (!product.Activo) throw new DomainRuleViolationException($"El producto {product.CodigoInterno} está inactivo.");
            if (product.CostoPromedio is null) throw new DomainRuleViolationException($"El producto {product.CodigoInterno} no tiene costo conocido.");
            if (requested.Quantity > product.StockDisponible)
                throw new DomainRuleViolationException($"El producto {product.CodigoInterno} no tiene stock disponible suficiente.");
            var unitPrice = requested.PriceType == "MAYORISTA"
                ? product.PrecioMayorista ?? throw new DomainRuleViolationException($"El producto {product.CodigoInterno} no tiene precio mayorista.")
                : product.PrecioMinorista;
            CashMoney.Require(unitPrice, allowNegative: false);
            if (requested.PriceType == "MAYORISTA" && requested.Quantity < wholesaleMinimumQuantity)
                throw new DomainRuleViolationException(
                    $"El precio mayorista exige al menos {wholesaleMinimumQuantity} unidades de {product.CodigoInterno}.");
            var gross = CashMoney.Require(checked(unitPrice * requested.Quantity), allowNegative: false);
            var line = new PreparedLine(product, requested.Quantity, requested.PriceType, unitPrice, gross);
            if (requested.Discount is not null)
            {
                if (requested.PriceType == "MAYORISTA")
                    throw new DomainRuleViolationException("El precio mayorista no permite descuento manual.");
                var discount = CalculateDiscount(gross, requested.Discount, maximumDiscount);
                line.ApplyDiscount("PRODUCTO", requested.Discount.Type, requested.Discount.Type == "PORCENTAJE" ? requested.Discount.Value : null, discount);
            }
            lines.Add(line);
        }

        var grossTotal = CashMoney.Sum(lines.Select(x => x.GrossAmount));
        if (generalDiscount is not null)
        {
            if (lines.Any(x => x.PriceType == "MAYORISTA"))
                throw new DomainRuleViolationException("El descuento general no puede aplicarse a líneas con precio mayorista.");
            var totalDiscount = CalculateDiscount(grossTotal, generalDiscount, maximumDiscount);
            IReadOnlyCollection<DiscountShare> exactShares = generalDiscount.Type == "PORCENTAJE"
                ? lines.Select(x => new DiscountShare(x.Product.Id, x.GrossAmount * generalDiscount.Value / 100m)).ToArray()
                : lines.Select(x => new DiscountShare(x.Product.Id, totalDiscount * x.GrossAmount / grossTotal)).ToArray();
            var allocations = SalePricingPolicy.AllocateByLargestRemainder(totalDiscount, exactShares);
            foreach (var line in lines)
                line.ApplyDiscount("VENTA", generalDiscount.Type, generalDiscount.Type == "PORCENTAJE" ? generalDiscount.Value : null, allocations[line.Product.Id]);
        }

        foreach (var line in lines)
        {
            try { SalePricingPolicy.EnsureNotBelowCost(line.NetAmount, line.Product.CostoPromedio!.Value, line.Quantity); }
            catch (DomainRuleViolationException)
            {
                // Nombra el producto para poder corregirlo; el costo solo lo ve quien tiene permiso de costos.
                throw new DomainRuleViolationException(
                    $"El producto {line.Product.CodigoInterno} ({line.Product.Nombre}) se vendería por debajo de su costo. Revisa su precio de venta o su costo promedio.");
            }
            line.CalculateTax(vatPercentage);
        }

        var discountTotal = CashMoney.Sum(lines.Select(x => x.DiscountAmount));
        var total = CashMoney.Subtract(grossTotal, discountTotal);
        var scope = generalDiscount is not null ? "VENTA" : lines.Any(x => x.DiscountAmount > 0) ? "PRODUCTO" : "NINGUNO";
        return new PreparedSale(lines, grossTotal, discountTotal, total, scope, true);
    }

    private static decimal CalculateDiscount(decimal gross, NormalizedDiscount discount, decimal maximumPercentage)
    {
        if (discount.Value <= 0) throw new DomainRuleViolationException("El descuento debe ser mayor que cero.");
        decimal amount;
        if (discount.Type == "PORCENTAJE")
        {
            if (discount.Value > maximumPercentage)
                throw new DomainRuleViolationException("El descuento supera el límite del rol actual.");
            amount = SalePricingPolicy.RoundCop(gross * discount.Value / 100m);
        }
        else
        {
            if (!CashMoney.IsValid(discount.Value, allowNegative: false))
                throw new DomainRuleViolationException("El descuento fijo debe ser un valor entero de COP.");
            amount = discount.Value;
        }
        SalePricingPolicy.EnsureDiscountWithinLimit(gross, amount, maximumPercentage);
        return amount;
    }

    private static ApplicationError? ValidatePayments(
        IReadOnlyCollection<NormalizedPayment> payments,
        IReadOnlyDictionary<string, MetodoPago> methods,
        decimal saleTotal)
    {
        var appliedTotal = 0m;
        foreach (var payment in payments)
        {
            if (!methods.ContainsKey(payment.MethodCode) || !ApprovedPaymentMethods.Contains(payment.MethodCode))
                return Validation("Solo se admiten EFECTIVO, TRANSFERENCIA y NEQUI en este corte.");
            if (!CashMoney.IsValid(payment.AppliedAmount, allowNegative: false) || payment.AppliedAmount <= 0)
                return Validation("Cada valor aplicado debe ser un peso COP entero mayor que cero.");
            appliedTotal = CashMoney.Add(appliedTotal, payment.AppliedAmount);
            if (payment.Reference?.Length > 200)
                return Validation("La referencia de pago admite hasta 200 caracteres.");

            if (payment.MethodCode == "EFECTIVO")
            {
                if (payment.Reference is not null) return Validation("El pago en efectivo no utiliza referencia.");
                if (payment.ReceivedAmount is not decimal received)
                    return Validation("El pago en efectivo requiere el valor recibido.");
                if (!CashMoney.IsValid(received, allowNegative: false) || received < payment.AppliedAmount)
                    return Validation("El efectivo recibido debe ser entero y no puede ser menor que el valor aplicado.");
            }
            else
            {
                if (payment.ReceivedAmount is not null)
                    return Validation("Transferencia y Nequi no usan valor recibido ni generan cambio.");
            }
        }
        return appliedTotal == saleTotal
            ? null
            : Validation("La suma de los valores aplicados debe coincidir exactamente con el total de la venta.");
    }

    private static NormalizedInput NormalizeInput(string key, ConfirmSaleRequest request)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 100)
            return NormalizedInput.Fail("Idempotency-Key es obligatorio y admite hasta 100 caracteres.");
        if (request?.Lines is null || request.Lines.Count == 0)
            return NormalizedInput.Fail("La venta debe contener al menos una línea.");
        var saleType = string.IsNullOrWhiteSpace(request.SaleType) ? "CONTADO" : NormalizeCode(request.SaleType);
        if (saleType is not "CONTADO" and not "CREDITO")
            return NormalizedInput.Fail("El tipo de venta debe ser CONTADO o CREDITO.");
        if (request.Payments is null || (saleType == "CONTADO" && request.Payments.Count == 0))
            return NormalizedInput.Fail("La venta de contado debe contener al menos un pago.");
        if (saleType == "CREDITO" && (request.Payments.Count != 0 || request.DueDate is null ||
            (request.Authorization is not null && !request.Authorization.IsComplete())))
            return NormalizedInput.Fail("El crédito requiere vencimiento, sin pago inicial; si envía authorization, incluya usuario y contraseña o la credencial escaneada.");
        if (saleType == "CONTADO" && request.DueDate is not null)
            return NormalizedInput.Fail("Una venta de contado no admite vencimiento.");
        if (saleType == "CONTADO" && request.Authorization is not null && !request.Authorization.IsComplete())
            return NormalizedInput.Fail("Si envía authorization, incluya usuario y contraseña o la credencial escaneada.");
        if (request.DiscountReason?.Length > 2000)
            return NormalizedInput.Fail("El motivo de descuento admite hasta 2000 caracteres.");

        var requestedLines = new List<NormalizedLine>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            if (line.ProductId == Guid.Empty || line.Quantity <= 0)
                return NormalizedInput.Fail("Cada línea requiere producto y cantidad entera positiva.");
            var priceType = NormalizeCode(line.PriceType);
            if (priceType is not "MINORISTA" and not "MAYORISTA")
                return NormalizedInput.Fail("El tipo de precio debe ser MINORISTA o MAYORISTA.");
            var discount = NormalizeDiscount(line.Discount);
            if (discount.Error is not null) return NormalizedInput.Fail(discount.Error.Message);
            requestedLines.Add(new NormalizedLine(line.ProductId, line.Quantity, priceType, discount.Value));
        }

        var lines = new List<NormalizedLine>();
        foreach (var group in requestedLines.GroupBy(x => x.ProductId))
        {
            var first = group.First();
            if (group.Any(x => x.PriceType != first.PriceType || !Equivalent(first.Discount, x.Discount)))
                return NormalizedInput.Fail("Los escaneos repetidos de un producto deben usar el mismo precio y descuento.");
            int quantity;
            try { quantity = group.Aggregate(0, (sum, x) => checked(sum + x.Quantity)); }
            catch (OverflowException) { return NormalizedInput.Fail("La cantidad acumulada excede el rango permitido."); }
            lines.Add(new NormalizedLine(group.Key, quantity, first.PriceType, first.Discount));
        }

        var general = NormalizeDiscount(request.GeneralDiscount);
        if (general.Error is not null) return NormalizedInput.Fail(general.Error.Message);
        if (general.Value is not null && lines.Any(x => x.Discount is not null))
            return NormalizedInput.Fail("No se pueden combinar descuentos por línea y descuento general.");

        var payments = request.Payments.Select(x => new NormalizedPayment(
            NormalizeCode(x.MethodCode),
            x.AppliedAmount,
            x.ReceivedAmount,
            NormalizeOptional(x.Reference))).ToArray();
        if (payments.Any(x => !ApprovedPaymentMethods.Contains(x.MethodCode)))
            return NormalizedInput.Fail("Solo se admiten EFECTIVO, TRANSFERENCIA y NEQUI en este corte.");
        if (payments.Select(x => x.MethodCode).Distinct(StringComparer.Ordinal).Count() != payments.Length)
            return NormalizedInput.Fail("No se puede repetir un método de pago en la misma venta.");

        // Preserve the original CONTADO hash so existing keys remain replayable.
        var canonical = saleType == "CONTADO"
            ? request.Authorization is null
                ? JsonSerializer.Serialize(new
                {
                    request.CustomerId,
                    Lines = lines.OrderBy(x => x.ProductId),
                    GeneralDiscount = general.Value,
                    DiscountReason = NormalizeOptional(request.DiscountReason),
                    Payments = payments.OrderBy(x => x.MethodCode, StringComparer.Ordinal)
                })
                // The authorizer's name (never the password) identifies a cash sale authorized for a discount without a reason.
                : JsonSerializer.Serialize(new
                {
                    request.CustomerId,
                    Lines = lines.OrderBy(x => x.ProductId),
                    GeneralDiscount = general.Value,
                    DiscountReason = NormalizeOptional(request.DiscountReason),
                    Payments = payments.OrderBy(x => x.MethodCode, StringComparer.Ordinal),
                    DiscountAuthorizer = request.Authorization.IdentityKey
                })
            : request.Authorization is null
                ? JsonSerializer.Serialize(new
                {
                    SaleType = saleType, request.CustomerId,
                    Lines = lines.OrderBy(x => x.ProductId), GeneralDiscount = general.Value,
                    DiscountReason = NormalizeOptional(request.DiscountReason),
                    Payments = payments.OrderBy(x => x.MethodCode, StringComparer.Ordinal),
                    request.DueDate, AuthorizationMode = "SELF"
                })
                : JsonSerializer.Serialize(new
                {
                    SaleType = saleType,
                    request.CustomerId,
                    Lines = lines.OrderBy(x => x.ProductId),
                    GeneralDiscount = general.Value,
                    DiscountReason = NormalizeOptional(request.DiscountReason),
                    Payments = payments.OrderBy(x => x.MethodCode, StringComparer.Ordinal),
                    request.DueDate,
                    Authorizer = request.Authorization.IdentityKey
                });
        return new NormalizedInput(key.Trim(), lines, general.Value, payments, saleType, canonical, null);
    }

    private static (NormalizedDiscount? Value, ApplicationError? Error) NormalizeDiscount(SaleDiscountRequest? discount)
    {
        if (discount is null) return (null, null);
        var type = NormalizeCode(discount.Type);
        return type is "PORCENTAJE" or "VALOR_FIJO"
            ? (new NormalizedDiscount(type, discount.Value), null)
            : (null, Validation("El descuento debe ser PORCENTAJE o VALOR_FIJO."));
    }

    private static bool Equivalent(NormalizedDiscount? left, NormalizedDiscount? right) =>
        left is null ? right is null : right is not null && left.Type == right.Type && left.Value == right.Value;

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

    private static ApplicationError? ValidatePage(SaleListQuery query) =>
        query.Page < 1 || query.PageSize is < 1 or > MaximumPageSize
            ? Validation("La página debe ser positiva y pageSize debe estar entre 1 y 200.")
            : null;

    private static PagedResult<SaleSummaryDto> Map(PagedResult<Venta> result) => new(
        result.Items.Select(MapSummary).ToArray(), result.Page, result.PageSize, result.TotalCount);

    private static SaleSummaryDto MapSummary(Venta sale) => new(
        sale.Id, VisibleNumber(sale), sale.UsuarioId, sale.ClienteId, Utc(sale.FechaHora),
        sale.FechaOperativa, sale.Total, sale.Estado);

    private static SaleDto Map(Venta sale) => new(
        sale.Id,
        VisibleNumber(sale),
        sale.TurnoCajaId,
        sale.UsuarioId,
        sale.SesionOperadorId,
        sale.ClienteId,
        Utc(sale.FechaHora),
        sale.FechaOperativa,
        sale.FechaLimiteCambio,
        sale.AmbitoDescuento,
        sale.TipoDescuento,
        sale.ValorDescuentoSolicitado,
        sale.MotivoDescuento,
        sale.SubtotalBruto,
        sale.DescuentoTotal,
        sale.Total,
        sale.PagoVenta.Sum(x => x.Cambio),
        sale.Estado,
        sale.Version,
        sale.DetalleVenta.OrderBy(x => x.ProductoId).Select(x => new SaleLineDto(
            x.Id, x.ProductoId, x.CodigoProducto, x.NombreProducto, x.UnidadMedida, x.Cantidad,
            x.TipoPrecio, x.PrecioUnitario, x.OrigenDescuento, x.TipoDescuento,
            x.PorcentajeDescuento, x.ValorDescuento, x.SubtotalBruto, x.SubtotalNeto)).ToArray(),
        sale.PagoVenta.OrderBy(x => x.MetodoPago.OrdenVisual).Select(x => new SalePaymentDto(
            x.Id, x.MetodoPago.Codigo, x.MetodoPago.Nombre, x.ValorAplicado, x.ValorRecibido, x.Cambio, x.Referencia)).ToArray(),
        sale.Credito?.Id);

    private static SaleDto Map(
        Venta sale,
        IReadOnlyCollection<DetalleVenta> details,
        IReadOnlyCollection<PagoVenta> payments,
        IReadOnlyDictionary<string, MetodoPago> methods) => new(
        sale.Id,
        VisibleNumber(sale),
        sale.TurnoCajaId,
        sale.UsuarioId,
        sale.SesionOperadorId,
        sale.ClienteId,
        Utc(sale.FechaHora),
        sale.FechaOperativa,
        sale.FechaLimiteCambio,
        sale.AmbitoDescuento,
        sale.TipoDescuento,
        sale.ValorDescuentoSolicitado,
        sale.MotivoDescuento,
        sale.SubtotalBruto,
        sale.DescuentoTotal,
        sale.Total,
        payments.Sum(x => x.Cambio),
        sale.Estado,
        sale.Version,
        details.OrderBy(x => x.ProductoId).Select(x => new SaleLineDto(
            x.Id, x.ProductoId, x.CodigoProducto, x.NombreProducto, x.UnidadMedida, x.Cantidad,
            x.TipoPrecio, x.PrecioUnitario, x.OrigenDescuento, x.TipoDescuento,
            x.PorcentajeDescuento, x.ValorDescuento, x.SubtotalBruto, x.SubtotalNeto)).ToArray(),
        payments.OrderBy(x => methods.Values.Single(m => m.Id == x.MetodoPagoId).OrdenVisual).Select(x =>
        {
            var method = methods.Values.Single(m => m.Id == x.MetodoPagoId);
            return new SalePaymentDto(x.Id, method.Codigo, method.Nombre, x.ValorAplicado, x.ValorRecibido, x.Cambio, x.Referencia);
        }).ToArray());

    private static string VisibleNumber(Venta sale) => $"{sale.Prefijo}-{sale.Serie}-{sale.Consecutivo}";
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static string NormalizeCode(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ApplicationError Validation(string message) => ApplicationError.Validation(message);

    private sealed record NormalizedDiscount(string Type, decimal Value);
    private sealed record NormalizedLine(Guid ProductId, int Quantity, string PriceType, NormalizedDiscount? Discount);
    private sealed record NormalizedPayment(string MethodCode, decimal AppliedAmount, decimal? ReceivedAmount, string? Reference);
    private sealed record NormalizedInput(
        string? Key,
        IReadOnlyCollection<NormalizedLine>? Lines,
        NormalizedDiscount? GeneralDiscount,
        IReadOnlyCollection<NormalizedPayment>? Payments,
        string? SaleType,
        string? Canonical,
        ApplicationError? Error)
    {
        public static NormalizedInput Fail(string message) => new(null, null, null, null, null, null, Validation(message));
    }

    private sealed class PreparedLine(Producto product, int quantity, string priceType, decimal unitPrice, decimal grossAmount)
    {
        public Producto Product { get; } = product;
        public int Quantity { get; } = quantity;
        public string PriceType { get; } = priceType;
        public decimal UnitPrice { get; } = unitPrice;
        public decimal GrossAmount { get; } = grossAmount;
        public string DiscountOrigin { get; private set; } = "NINGUNO";
        public string? DiscountType { get; private set; }
        public decimal? DiscountPercentage { get; private set; }
        public decimal DiscountAmount { get; private set; }
        public decimal NetAmount => GrossAmount - DiscountAmount;
        public decimal? TaxBase { get; private set; }
        public decimal? TaxAmount { get; private set; }

        public void ApplyDiscount(string origin, string type, decimal? percentage, decimal amount)
        {
            DiscountOrigin = origin;
            DiscountType = type;
            DiscountPercentage = percentage;
            DiscountAmount = amount;
        }

        public void CalculateTax(decimal? taxPercentage)
        {
            if (taxPercentage is null) throw new DomainRuleViolationException("La configuración de IVA no tiene porcentaje vigente.");
            TaxBase = decimal.Round(NetAmount * 100m / (100m + taxPercentage.Value), 0, MidpointRounding.AwayFromZero);
            TaxAmount = NetAmount - TaxBase.Value;
        }
    }

    private sealed record PreparedSale(
        IReadOnlyCollection<PreparedLine> Lines,
        decimal GrossSubtotal,
        decimal DiscountTotal,
        decimal Total,
        string DiscountScope,
        bool HasCompleteTax);
}
