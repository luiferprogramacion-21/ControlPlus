using ControlPlus.Domain.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.Common;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Sales;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Layaways.Services;

public sealed class LayawayCreateService(
    ILayawayCreateRepository layaways,
    ISalesRepository sales,
    IUserRepository users,
    IPermissionChecker permissions,
    IPermissionRepository permissionRepository,
    IPasswordHasher passwordHasher,
    IAuthorizerCredentialResolver credentialResolver,
    ILoginAttemptCoordinator loginAttempts,
    IAuditRepository audits,
    IUnitOfWork unitOfWork,
    IClock clock) : ILayawayCreateService
{
    public async Task<Result<CreatedLayawayDto>> CreateAsync(
        ActorContext actor, string idempotencyKey, CreateLayawayRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request?.Authorization is { } scanned && scanned.UsesCredential())
        {
            if (scanned.MixesCredentialWithPassword())
                return Fail(ApplicationError.Validation("Envíe la credencial escaneada o usuario y contraseña, no ambos."));
            var (userName, credentialId) = await credentialResolver.ResolveOrUnknownAsync(scanned.CredentialToken!, cancellationToken);
            request = request with { Authorization = scanned with { UserName = userName, ResolvedCredentialId = credentialId } };
        }
        var normalized = Normalize(idempotencyKey, request!);
        if (normalized.Error is not null)
            return Result.Failure<CreatedLayawayDto>(normalized.Error);

        return await loginAttempts.ExecuteAsync(
            normalized.AuthorizerUserName ?? actor.UserId.ToString("N"),
            token => CreateWithinTransactionAsync(actor, normalized, token),
            cancellationToken);
    }

    private async Task<Result<CreatedLayawayDto>> CreateWithinTransactionAsync(
        ActorContext actor, NormalizedCreate input, CancellationToken cancellationToken)
    {
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Fail(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Fail(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.LayawayManage, StringComparer.OrdinalIgnoreCase))
            return Fail(ApplicationError.Forbidden());
        var administratorSelfAuthorized = snapshot.HighestRoleLevel == RoleLevel.Administrador &&
            input.AuthorizerUserName is null;
        var supervisorSelfAuthorized = snapshot.HighestRoleLevel == RoleLevel.Supervisor &&
            input.AuthorizerUserName is null;
        var selfAuthorized = administratorSelfAuthorized || supervisorSelfAuthorized;
        if (snapshot.HighestRoleLevel == RoleLevel.Administrador && input.AuthorizerUserName is not null)
            return Fail(ApplicationError.Validation("El Administrador debe omitir authorization al crear un apartado."));
        if (!selfAuthorized && input.AuthorizerUserName is null)
            return Fail(ApplicationError.Validation("La autorización de otro Supervisor o Administrador es obligatoria."));
        var requestHash = supervisorSelfAuthorized
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Hash + "|SUPERVISOR_SELF")))
            : input.Hash;

        var previous = await sales.GetIdempotencyAsync(installation.Id, input.Key!, cancellationToken);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "CREAR_APARTADO" ||
                previous.HashSolicitud != requestHash || string.IsNullOrEmpty(previous.Resultado))
                return Fail(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            var replay = JsonSerializer.Deserialize<CreatedLayawayDto>(previous.Resultado);
            return replay is null
                ? Fail(ApplicationError.Conflict("La operación idempotente no tiene un resultado recuperable."))
                : Result.Success(replay);
        }

        var establishment = await layaways.GetEstablishmentAsync(installation.EstablecimientoId, cancellationToken);
        if (establishment is null) return Fail(ApplicationError.NotFound("el establecimiento activo"));
        var terminal = await sales.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null) return Fail(ApplicationError.NotFound("la terminal activa"));
        var shift = await sales.GetOpenShiftForUpdateAsync(cancellationToken);
        if (shift is null || shift.TerminalId != terminal.Id)
            return Fail(ApplicationError.Conflict("Debe existir un turno abierto en la terminal activa."));

        SesionOperador? session = null;
        if (shift.ModoOperacion == CashConstants.IndividualMode)
        {
            if (shift.UsuarioResponsableId != actor.UserId) return Fail(ApplicationError.Forbidden());
        }
        else if (shift.ModoOperacion == CashConstants.SharedMode)
        {
            session = await layaways.GetActiveOperatorSessionForUpdateAsync(
                shift.Id, terminal.Id, actor.UserId, cancellationToken);
            if (session is null)
                return Fail(ApplicationError.Conflict("Se requiere una sesión activa del operador."));
        }
        else return Fail(ApplicationError.Conflict("La modalidad del turno no es válida."));

        DateOnly dueDate;
        try
        {
            dueDate = LayawayPolicy.ResolveDueDate(
                shift.FechaOperativa, establishment.DiasLimiteApartado, input.DueDate);
        }
        catch (DomainRuleViolationException exception)
        {
            return Fail(ApplicationError.Validation(exception.Message));
        }

        var customer = await layaways.GetActiveCustomerForUpdateAsync(input.CustomerId, cancellationToken);
        if (customer is null)
            return Fail(ApplicationError.NotFound("el cliente activo"));
        if ((string.IsNullOrWhiteSpace(customer.TipoDocumento) ||
             string.IsNullOrWhiteSpace(customer.NumeroDocumento)) &&
            string.IsNullOrWhiteSpace(customer.Telefono))
            return Fail(ApplicationError.Validation("El cliente requiere documento completo o teléfono para apartar."));

        Usuario? authorizer = null;
        if (!selfAuthorized)
        {
            var candidate = await users.GetByUserNameAsync(input.AuthorizerUserName!, cancellationToken);
            if (candidate is null) return Fail(ApplicationError.Forbidden());
            authorizer = await sales.GetUserForUpdateAsync(candidate.Id, cancellationToken);
            if (authorizer is null || !authorizer.IsActive || authorizer.Id == actor.UserId ||
                !RoleHierarchy.IsHigherThan(authorizer.HighestRoleLevel, snapshot.HighestRoleLevel))
                return Fail(ApplicationError.Forbidden());
            if (input.CredentialId is null && !passwordHasher.Verify(input.AuthorizerPassword!, authorizer.PasswordHash))
            {
                var locked = authorizer.RegisterFailedLogin(clock.UtcNow);
                await users.UpdateAsync(authorizer, cancellationToken);
                if (locked)
                    await AuditWriter.WriteAsync(audits, clock, authorizer.Id, AuditAction.UserLocked,
                        nameof(Usuario), authorizer.Id,
                        new { authorizer.Id, authorizer.FailedLoginAttempts }, cancellationToken);
                await AuditWriter.WriteCashFailureAsync(audits, clock, actor.UserId,
                    AuditAction.LayawayAuthorizationRejected, nameof(Apartado), null,
                    new { Reason = "INVALID_AUTHORIZER_CREDENTIALS" }, Guid.CreateVersion7(),
                    terminal.Id, null, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Fail(ApplicationError.Forbidden());
            }
            if (!await permissions.HasPermissionAsync(authorizer.Id, PermissionCodes.LayawayManage, cancellationToken))
                return Fail(ApplicationError.Forbidden());
        }

        // Decisión del 15 de noviembre de 2026: un Cajero solo aparta a precio mayorista si su autorizador, de rol
        // superior, también puede autorizar descuentos; la misma persona deja registrada la autorización PRECIO_MAYORISTA.
        var wholesaleNeedsAuthorization = snapshot.HighestRoleLevel == RoleLevel.Cajero &&
            input.Lines!.Any(x => x.PriceType == "MAYORISTA");
        if (wholesaleNeedsAuthorization && (authorizer is null ||
            !await permissions.HasPermissionAsync(authorizer.Id, PermissionCodes.SalesDiscountAuthorize, cancellationToken)))
            return Fail(ApplicationError.Forbidden(
                "El precio mayorista de un Cajero requiere un autorizador con permiso para autorizar descuentos."));

        // Installation and shift are already locked. Sequence locks are acquired in a fixed order.
        var layawaySequence = await layaways.GetSequenceForUpdateAsync(
            installation.Id, installation.Serie, "APARTADO", cancellationToken);
        var paymentSequence = await layaways.GetSequenceForUpdateAsync(
            installation.Id, installation.Serie, "PAGO_APARTADO", cancellationToken);
        if (layawaySequence is null || paymentSequence is null)
            return Fail(ApplicationError.Conflict("Configure explícitamente los consecutivos APARTADO y PAGO_APARTADO."));
        if (layawaySequence.Prefijo != "APT" || paymentSequence.Prefijo != "PAP")
            return Fail(ApplicationError.Conflict("Los consecutivos de apartados no conservan los prefijos aprobados."));
        if (layawaySequence.UltimoNumero == long.MaxValue ||
            paymentSequence.UltimoNumero > long.MaxValue - input.Payments!.Length)
            return Fail(ApplicationError.NumberingExhausted("El consecutivo de apartados o pagos está agotado."));

        var products = await sales.GetProductsForUpdateAsync(input.Lines!.Select(x => x.ProductId).ToArray(), cancellationToken);
        if (products.Count != input.Lines!.Length)
            return Fail(ApplicationError.NotFound("uno o más productos"));
        var productById = products.ToDictionary(x => x.Id);
        var unitNames = await sales.GetMeasurementUnitNamesAsync(
            products.Select(x => x.UnidadMedidaId).Distinct().ToArray(), cancellationToken);
        if (unitNames.Count != products.Select(x => x.UnidadMedidaId).Distinct().Count())
            return Fail(ApplicationError.Conflict("La unidad de medida de un producto no está disponible."));

        List<PreparedLine> prepared;
        decimal total;
        decimal? taxBaseTotal = null;
        decimal? taxTotal = null;
        try
        {
            prepared = [];
            foreach (var line in input.Lines.OrderBy(x => x.ProductId))
            {
                var product = productById[line.ProductId];
                if (!product.Activo || product.CostoPromedio is null || line.Quantity > product.StockDisponible)
                    return Fail(ApplicationError.Conflict("Uno o más productos no están disponibles para reservar."));
                var unitPrice = line.PriceType == "MAYORISTA" ? product.PrecioMayorista : product.PrecioMinorista;
                if (unitPrice is null)
                    return Fail(ApplicationError.Validation("El producto no tiene precio mayorista configurado."));
                if (line.PriceType == "MAYORISTA" && line.Quantity < establishment.MinimoUnidadesMayorista)
                    return Fail(ApplicationError.Validation(
                        $"El precio mayorista exige al menos {establishment.MinimoUnidadesMayorista} unidades de {product.CodigoInterno}."));
                CashMoney.Require(unitPrice.Value, allowNegative: false);
                var lineTotal = CashMoney.Require(checked(unitPrice.Value * line.Quantity), allowNegative: false);
                SalePricingPolicy.EnsureNotBelowCost(lineTotal, product.CostoPromedio.Value, line.Quantity);
                var tax = LayawayPolicy.CalculateIncludedTax(lineTotal,
                    0m /* El IVA se quitó del sistema el 18 de noviembre de 2026: las ventas y apartados nuevos no lo desglosan. */);
                prepared.Add(new PreparedLine(product, line.Quantity, line.PriceType, unitPrice.Value,
                    lineTotal, tax.Base, tax.Tax));
            }
            total = CashMoney.Sum(prepared.Select(x => x.Total));
            if (total <= 0) return Fail(ApplicationError.Validation("El apartado debe tener un total positivo."));
            if (prepared.All(x => x.TaxBase is not null))
            {
                taxBaseTotal = LayawayPolicy.SumTaxAmounts(prepared.Select(x => x.TaxBase!.Value));
                taxTotal = LayawayPolicy.SumTaxAmounts(prepared.Select(x => x.TaxAmount!.Value));
            }
        }
        catch (CashMoneyException)
        {
            return Fail(ApplicationError.Validation("Los importes deben ser pesos COP enteros dentro del rango permitido."));
        }
        catch (OverflowException)
        {
            return Fail(ApplicationError.Validation("La cantidad o el importe acumulado excede el rango permitido."));
        }
        catch (DomainRuleViolationException exception)
        {
            return Fail(ApplicationError.Validation(exception.Message));
        }

        decimal initialPayment;
        decimal minimumPayment;
        try
        {
            initialPayment = CashMoney.Sum(input.Payments.Select(x => x.AppliedAmount));
            minimumPayment = establishment.RequireMinimumInitial
                ? LayawayPolicy.MinimumInitialPayment(total, establishment.PorcentajeMinimoApartado)
                : 1m;
            if (initialPayment < minimumPayment)
                return Fail(ApplicationError.Validation(
                    $"El abono inicial mínimo es de {minimumPayment:0} pesos."));
            if (initialPayment > total)
                return Fail(ApplicationError.Validation("El abono inicial no puede superar el total del apartado."));
        }
        catch (CashMoneyException)
        {
            return Fail(ApplicationError.Validation("Los importes deben ser pesos COP enteros dentro del rango permitido."));
        }
        catch (DomainRuleViolationException exception)
        {
            return Fail(ApplicationError.Validation(exception.Message));
        }

        var methodCodes = input.Payments.Select(x => x.MethodCode).ToArray();
        var methods = await sales.GetPaymentMethodsAsync(methodCodes, cancellationToken);
        if (methods.Count != methodCodes.Length || methods.Any(x => !x.Activo) ||
            methods.Any(x => x.Codigo == "EFECTIVO" ? !x.AfectaEfectivo : x.AfectaEfectivo))
            return Fail(ApplicationError.Validation("Los métodos de pago deben estar activos y conservar su clasificación."));
        var methodsByCode = methods.ToDictionary(x => x.Codigo, StringComparer.Ordinal);
        if (input.Payments.Any(x => methodsByCode[x.MethodCode].RequiereReferencia &&
            string.IsNullOrWhiteSpace(x.Reference)))
            return Fail(ApplicationError.Validation("La referencia electrónica es obligatoria."));

        var permission = await permissionRepository.GetByCodeAsync(PermissionCodes.LayawayManage, cancellationToken);
        if (permission is null || !permission.Activo)
            return Fail(ApplicationError.Conflict("El permiso de apartados no está activo."));

        var now = clock.UtcNow;
        var correlationId = Guid.CreateVersion7();
        layawaySequence.UltimoNumero++;
        var layaway = new Apartado
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TurnoCajaId = shift.Id,
            ClienteId = customer.Id, UsuarioId = actor.UserId, SesionOperadorId = session?.Id,
            AutorizacionOperacionId = Guid.CreateVersion7(), Prefijo = layawaySequence.Prefijo,
            Serie = layawaySequence.Serie, Consecutivo = layawaySequence.UltimoNumero,
            FechaHora = now.UtcDateTime, FechaLimite = dueDate,
            AmbitoDescuento = "NINGUNO",
            PorcentajeMinimoInicial = establishment.RequireMinimumInitial
                ? establishment.PorcentajeMinimoApartado
                : 0m,
            AbonoInicial = initialPayment, SubtotalBruto = total, DescuentoTotal = 0,
            IvaDiscriminadoCompleto = true,
            BaseGravableTotal = taxBaseTotal, ImpuestoIncluidoTotal = taxTotal,
            Total = total, SaldoPendiente = total - initialPayment,
            Estado = initialPayment == total ? "PAGADO" : "ACTIVO",
            FechaPago = initialPayment == total ? now.UtcDateTime : null, Version = 1
        };

        var details = new List<DetalleApartado>(prepared.Count);
        var inventoryMovements = new List<MovimientoInventario>(prepared.Count);
        foreach (var line in prepared)
        {
            var product = line.Product;
            var reservedBefore = product.StockReservado;
            product.StockReservado = checked(reservedBefore + line.Quantity);
            details.Add(new DetalleApartado
            {
                Id = Guid.CreateVersion7(), ApartadoId = layaway.Id, ProductoId = product.Id,
                CodigoProducto = product.CodigoInterno, NombreProducto = product.Nombre,
                UnidadMedida = unitNames[product.UnidadMedidaId], Cantidad = line.Quantity,
                PrecioReserva = line.UnitPrice, CostoUnitario = product.CostoPromedio,
                PorcentajeIva = 0m /* El IVA se quitó del sistema el 18 de noviembre de 2026: las ventas y apartados nuevos no lo desglosan. */,
                SubtotalBruto = line.Total,
                OrigenDescuento = "NINGUNO", ValorDescuento = 0,
                BaseGravable = line.TaxBase, ValorIva = line.TaxAmount,
                SubtotalNeto = line.Total
            });
            inventoryMovements.Add(new MovimientoInventario
            {
                Id = Guid.CreateVersion7(), ProductoId = product.Id, UsuarioId = actor.UserId,
                SesionOperadorId = session?.Id, ApartadoId = layaway.Id,
                TipoMovimiento = "RESERVA_APARTADO", CantidadStock = 0,
                CantidadReservada = line.Quantity, StockAnterior = product.StockActual,
                StockPosterior = product.StockActual, ReservadoAnterior = reservedBefore,
                ReservadoPosterior = product.StockReservado,
                CostoPromedioAnterior = product.CostoPromedio,
                CostoPromedioPosterior = product.CostoPromedio,
                FechaHora = now.UtcDateTime, CorrelacionId = correlationId
            });
        }
        var movement = MovimientoCaja.Create(shift.Id, actor.UserId, session?.Id,
            CashConstants.IncomeMovement, "APARTADO", initialPayment,
            $"Apartado {layaway.Prefijo}-{layaway.Serie}-{layaway.Consecutivo}", correlationId, now);
        var paymentRows = input.Payments.Select(x =>
        {
            paymentSequence.UltimoNumero++;
            return new PagoApartado
            {
                Id = Guid.CreateVersion7(), ApartadoId = layaway.Id, InstalacionId = installation.Id,
                TurnoCajaId = shift.Id, MetodoPagoId = methodsByCode[x.MethodCode].Id,
                UsuarioId = actor.UserId, SesionOperadorId = session?.Id, MovimientoCajaId = movement.Id,
                Prefijo = paymentSequence.Prefijo, Serie = paymentSequence.Serie,
                Consecutivo = paymentSequence.UltimoNumero,
                ValorAplicado = x.AppliedAmount, ValorRecibido = x.ReceivedAmount,
                Cambio = x.ReceivedAmount is decimal received ? received - x.AppliedAmount : 0m,
                Referencia = x.Reference, FechaHora = now.UtcDateTime, Estado = "APLICADO"
            };
        }).ToArray();
        var authorization = new AutorizacionOperacion
        {
            Id = layaway.AutorizacionOperacionId, UsuarioSolicitanteId = actor.UserId,
            UsuarioAutorizadorId = authorizer?.Id ?? actor.UserId, PermisoId = permission.Id,
            TipoOperacion = "CREAR_APARTADO", CorrelacionId = correlationId,
            DescripcionOperacion = administratorSelfAuthorized
                ? "Autorización de creación de apartado por Administrador sin segundo autorizador."
                : supervisorSelfAuthorized
                    ? "Autorización de creación de apartado por Supervisor sin segundo autorizador."
                    : "Autorización de creación de apartado por otro usuario.",
            MetodoAutenticacion = input.CredentialId is null ? "PASSWORD" : "CODIGO_BARRAS_PIN",
            CredencialAutorizadorId = input.CredentialId,
            Resultado = "EXITOSA", Estado = "UTILIZADA",
            FechaSolicitud = now.UtcDateTime, FechaExpiracion = now.AddMinutes(5).UtcDateTime,
            FechaUtilizacion = now.UtcDateTime
        };

        var response = new CreatedLayawayDto(
            layaway.Id, $"{layaway.Prefijo}-{layaway.Serie}-{layaway.Consecutivo}",
            layaway.ClienteId, shift.Id, actor.UserId, session?.Id, dueDate,
            layaway.PorcentajeMinimoInicial, minimumPayment, initialPayment, total,
            layaway.SaldoPendiente, paymentRows.Sum(x => x.Cambio), layaway.Estado, layaway.Version,
            prepared.Select(x => new CreatedLayawayLineDto(
                x.Product.Id, x.Product.CodigoInterno, x.Product.Nombre,
                unitNames[x.Product.UnidadMedidaId], x.Quantity, x.PriceType, x.UnitPrice, x.Total)).ToArray(),
            paymentRows.Select(x => new CreatedLayawayPaymentDto(
                $"{x.Prefijo}-{x.Serie}-{x.Consecutivo}",
                methods.Single(m => m.Id == x.MetodoPagoId).Codigo,
                x.ValorAplicado, x.ValorRecibido, x.Cambio)).ToArray());

        if (authorizer is not null)
        {
            authorizer.RegisterSuccessfulLogin(now);
            await users.UpdateAsync(authorizer, cancellationToken);
        }
        await sales.AddAuthorizationAsync(authorization, cancellationToken);
        if (wholesaleNeedsAuthorization)
        {
            var wholesalePermission = await permissionRepository.GetByCodeAsync(PermissionCodes.SalesDiscountAuthorize, cancellationToken);
            if (wholesalePermission is null || !wholesalePermission.Activo)
                return Fail(ApplicationError.Conflict("El permiso de autorización de descuentos no está activo."));
            await sales.AddAuthorizationAsync(new AutorizacionOperacion
            {
                Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
                UsuarioAutorizadorId = authorizer!.Id, PermisoId = wholesalePermission.Id,
                TipoOperacion = "PRECIO_MAYORISTA", CorrelacionId = correlationId,
                DescripcionOperacion = "Autorización de precio mayorista de un Cajero en un apartado.",
                MetodoAutenticacion = input.CredentialId is null ? "PASSWORD" : "CODIGO_BARRAS_PIN",
                CredencialAutorizadorId = input.CredentialId, Resultado = "EXITOSA", Estado = "UTILIZADA",
                FechaSolicitud = now.UtcDateTime, FechaExpiracion = now.AddMinutes(5).UtcDateTime,
                FechaUtilizacion = now.UtcDateTime
            }, cancellationToken);
        }
        await layaways.AddAsync(layaway, details, paymentRows, inventoryMovements, cancellationToken);
        await sales.AddCashMovementAsync(movement, cancellationToken);
        await sales.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, SesionOperadorId = session?.Id,
            ClaveIdempotencia = input.Key!, TipoOperacion = "CREAR_APARTADO",
            HashSolicitud = requestHash!, Entidad = nameof(Apartado), EntidadId = layaway.Id,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlationId,
            FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId, AuditAction.LayawayCreated,
            nameof(Apartado), layaway.Id,
            new { layaway.Id, layaway.Estado,
                AuthorizationMode = administratorSelfAuthorized ? "ADMINISTRATOR_SELF" :
                    supervisorSelfAuthorized ? "SUPERVISOR_SELF" : "OTHER_USER",
                AuthorizerId = authorizer?.Id ?? actor.UserId,
                ProductIds = prepared.Select(x => x.Product.Id).ToArray(),
                PaymentMethods = input.Payments.Select(x => x.MethodCode).ToArray() },
            correlationId, authorizer?.Id ?? actor.UserId, session?.Id, terminal.Id, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (session is not null)
        {
            session.Close(now, "CAMBIO_OPERADOR");
            await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
                AuditAction.OperatorSessionClosed, nameof(SesionOperador), session.Id,
                new { session.Estado, LayawayId = layaway.Id, SessionConsumedByLayaway = true },
                correlationId, null, session.Id, terminal.Id, null, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result.Success(response);
    }

    private static NormalizedCreate Normalize(string key, CreateLayawayRequest request)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 100 || request is null ||
            request.CustomerId == Guid.Empty || request.Lines is null || request.Lines.Count == 0 ||
            request.Payments is null || request.Payments.Count is < 1 or > 3 ||
            (request.Authorization is not null && !request.Authorization.IsComplete()))
            return NormalizedCreate.Fail("Cliente, líneas, pagos e Idempotency-Key son obligatorios; si envía authorization, incluya usuario y contraseña o la credencial escaneada.");

        var lines = new List<CreateLayawayLineRequest>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.ProductId == Guid.Empty || line.Quantity <= 0)
                return NormalizedCreate.Fail("Cada línea requiere producto y cantidad entera positiva.");
            var priceType = line.PriceType?.Trim().ToUpperInvariant();
            if (priceType is not "MINORISTA" and not "MAYORISTA")
                return NormalizedCreate.Fail("El precio debe ser MINORISTA o MAYORISTA.");
            lines.Add(new CreateLayawayLineRequest(line.ProductId, line.Quantity, priceType));
        }
        try
        {
            lines = lines.GroupBy(x => x.ProductId).Select(group =>
            {
                var first = group.First();
                if (group.Any(x => x.PriceType != first.PriceType))
                    throw new ArgumentException("Los escaneos repetidos deben usar el mismo tipo de precio.");
                return first with { Quantity = group.Aggregate(0, (sum, x) => checked(sum + x.Quantity)) };
            }).OrderBy(x => x.ProductId).ToList();
        }
        catch (ArgumentException exception) { return NormalizedCreate.Fail(exception.Message); }
        catch (OverflowException) { return NormalizedCreate.Fail("La cantidad acumulada excede el rango permitido."); }

        var payments = new List<CreateLayawayPaymentRequest>();
        foreach (var payment in request.Payments)
        {
            if (payment is null) return NormalizedCreate.Fail("El pago es obligatorio.");
            var code = payment.MethodCode?.Trim().ToUpperInvariant();
            var reference = string.IsNullOrWhiteSpace(payment.Reference) ? null : payment.Reference.Trim();
            if (code is not "EFECTIVO" and not "TRANSFERENCIA" and not "NEQUI" ||
                !CashMoney.IsValid(payment.AppliedAmount, allowNegative: false) ||
                payment.AppliedAmount <= 0 || reference?.Length > 200)
                return NormalizedCreate.Fail("Método, importe o referencia de pago inválidos.");
            if (code == "EFECTIVO")
            {
                if (reference is not null || payment.ReceivedAmount is not decimal received ||
                    !CashMoney.IsValid(received, allowNegative: false) || received < payment.AppliedAmount)
                    return NormalizedCreate.Fail("El efectivo requiere recibido suficiente y no admite referencia.");
            }
            else if (payment.ReceivedAmount is not null || reference is null)
                return NormalizedCreate.Fail("Transferencia y Nequi requieren referencia y no admiten recibido.");
            payments.Add(new CreateLayawayPaymentRequest(code, payment.AppliedAmount, payment.ReceivedAmount, reference));
        }
        if (payments.Select(x => x.MethodCode).Distinct().Count() != payments.Count)
            return NormalizedCreate.Fail("No se puede repetir un método de pago.");
        var authorizerUserName = request.Authorization?.UserName!.Trim().ToUpperInvariant();
        var sortedPayments = payments.OrderBy(x => x.MethodCode).ToArray();
        var canonical = authorizerUserName is null
            ? JsonSerializer.Serialize(new
            {
                request.CustomerId, request.DueDate, Lines = lines,
                Payments = sortedPayments, AuthorizationMode = "ADMINISTRATOR_SELF"
            })
            : JsonSerializer.Serialize(new
            {
                request.CustomerId, request.DueDate, Lines = lines,
                Payments = sortedPayments, AuthorizerUserName = authorizerUserName
            });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new NormalizedCreate(key.Trim(), request.CustomerId, lines.ToArray(), sortedPayments,
            request.DueDate, authorizerUserName, request.Authorization?.Password, hash, request.Authorization?.ResolvedCredentialId, null);
    }

    private static Result<CreatedLayawayDto> Fail(ApplicationError error) => Result.Failure<CreatedLayawayDto>(error);

    private sealed record PreparedLine(Producto Product, int Quantity, string PriceType,
        decimal UnitPrice, decimal Total, decimal? TaxBase, decimal? TaxAmount);

    private sealed record NormalizedCreate(
        string? Key, Guid CustomerId, CreateLayawayLineRequest[]? Lines,
        CreateLayawayPaymentRequest[]? Payments, DateOnly? DueDate,
        string? AuthorizerUserName, string? AuthorizerPassword, string? Hash, Guid? CredentialId,
        ApplicationError? Error)
    {
        public static NormalizedCreate Fail(string message) =>
            new(null, Guid.Empty, null, null, null, null, null, null, null, ApplicationError.Validation(message));
    }
}
