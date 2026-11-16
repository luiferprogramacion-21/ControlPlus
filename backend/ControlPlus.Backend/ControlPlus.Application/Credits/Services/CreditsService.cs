using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Credits.Ports;
using ControlPlus.Application.Numbering;
using ControlPlus.Application.Numbering.Ports;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.Common;
using ControlPlus.Domain.Credits;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Credits.Services;

public sealed partial class CreditsService(ICreditsRepository credits, ISalesRepository sales,
    IDocumentNumberingRepository numbering, ISalesTransactionManager transactions,
    IUserRepository users, IAuditRepository audits, IUnitOfWork unitOfWork, IClock clock,
    IPermissionRepository permissions, IPasswordHasher passwordHasher,
    IAuthorizerCredentialResolver credentialResolver, ILoginAttemptCoordinator loginAttempts,
    ControlPlus.Application.Cash.Ports.ICashRepository cashRepository)
    : ICreditsService
{
    public async Task<Result<PagedResult<CreditDto>>> ListAsync(ActorContext actor, CreditListQuery query,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CreditManage, cancellationToken);
        if (error is not null) return Result.Failure<PagedResult<CreditDto>>(error);
        var pagination = ValidatePagination(query.Page, query.PageSize);
        if (pagination.Error is not null)
            return Result.Failure<PagedResult<CreditDto>>(pagination.Error);
        if ((query.State is not null && query.State is not "PENDIENTE" and not "VENCIDO" and not "PAGADO" and not "ANULADO") ||
            (query.DueFrom is DateOnly from && query.DueTo is DateOnly to && from > to) ||
            query.SaleNumber?.Length > 40)
            return Result.Failure<PagedResult<CreditDto>>(ApplicationError.Validation("Filtros inválidos."));
        var today = await TodayAsync(cancellationToken);
        var result = await credits.ListAsync(query, today, pagination.Offset, cancellationToken);
        return Result.Success(new PagedResult<CreditDto>(result.Items.Select(x => Map(x, today)).ToArray(),
            result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<CreditDto>> GetAsync(ActorContext actor, Guid creditId,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CreditManage, cancellationToken);
        if (error is not null) return Result.Failure<CreditDto>(error);
        var credit = await credits.GetAsync(creditId, cancellationToken);
        return credit is null ? Result.Failure<CreditDto>(ApplicationError.NotFound("el crédito"))
            : Result.Success(Map(credit, await TodayAsync(cancellationToken)));
    }

    public async Task<Result<PagedResult<CreditPaymentDto>>> ListPaymentsAsync(ActorContext actor,
        Guid creditId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CreditManage, cancellationToken)
            ?? await RequireAsync(actor, PermissionCodes.CustomersHistoryRead, cancellationToken);
        if (error is not null) return Result.Failure<PagedResult<CreditPaymentDto>>(error);
        var pagination = ValidatePagination(page, pageSize);
        if (pagination.Error is not null)
            return Result.Failure<PagedResult<CreditPaymentDto>>(pagination.Error);
        if (await credits.GetAsync(creditId, cancellationToken) is null)
            return Result.Failure<PagedResult<CreditPaymentDto>>(ApplicationError.NotFound("el crédito"));
        var result = await credits.ListPaymentsAsync(
            creditId, page, pageSize, pagination.Offset, cancellationToken);
        return Result.Success(new PagedResult<CreditPaymentDto>(result.Items.Select(Map).ToArray(),
            result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<CreditPaymentOperationDto>> ApplyPaymentAsync(ActorContext actor, Guid creditId,
        string idempotencyKey, ApplyCreditPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 100 ||
            request is null || request.ExpectedVersion <= 0 || request.Payments is null ||
            request.Payments.Count is < 1 or > 3)
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("Idempotency-Key, versión y pagos son obligatorios."));
        var payments = request.Payments.Select(x => new CreditPaymentMethodRequest(
            x.MethodCode?.Trim().ToUpperInvariant() ?? string.Empty, x.AppliedAmount, x.ReceivedAmount,
            string.IsNullOrWhiteSpace(x.Reference) ? null : x.Reference.Trim())).ToArray();
        if (payments.Any(x => x.MethodCode is not "EFECTIVO" and not "TRANSFERENCIA" and not "NEQUI" ||
            x.Reference?.Length > 200) || payments.Select(x => x.MethodCode).Distinct().Count() != payments.Length)
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("Métodos de pago inválidos o repetidos."));
        decimal amount;
        try
        {
            amount = CashMoney.Sum(payments.Select(x => CashMoney.Require(x.AppliedAmount, allowNegative: false)));
            if (amount <= 0 || payments.Any(x => x.AppliedAmount <= 0))
                return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("Cada importe aplicado debe ser positivo."));
            foreach (var payment in payments)
            {
                if (payment.MethodCode == "EFECTIVO")
                {
                    if (payment.ReceivedAmount is null ||
                        CashMoney.Require(payment.ReceivedAmount.Value, allowNegative: false) < payment.AppliedAmount)
                        return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("El efectivo recibido debe cubrir el importe aplicado."));
                }
                else if (payment.ReceivedAmount is not null)
                    return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("Los métodos electrónicos no reciben efectivo."));
            }
        }
        catch (CashMoneyException)
        {
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("Los importes deben ser pesos COP enteros dentro del rango permitido."));
        }
        catch (OverflowException)
        {
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("El importe acumulado excede el rango permitido."));
        }

        var key = idempotencyKey.Trim();
        var canonical = JsonSerializer.Serialize(new { creditId, request.ExpectedVersion,
            Payments = payments.OrderBy(x => x.MethodCode) });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        await using var transaction = await transactions.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Result.Failure<CreditPaymentOperationDto>(ApplicationError.NotFound("la instalación activa"));
        var error = await RequireAsync(actor, PermissionCodes.CreditManage, cancellationToken);
        if (error is not null) return Result.Failure<CreditPaymentOperationDto>(error);
        var previous = await sales.GetIdempotencyAsync(installation.Id, key, cancellationToken);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "ABONAR_CREDITO" ||
                previous.HashSolicitud != hash || string.IsNullOrEmpty(previous.Resultado))
                return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            var replay = JsonSerializer.Deserialize<CreditPaymentOperationDto>(previous.Resultado);
            return replay is null ? Result.Failure<CreditPaymentOperationDto>(ApplicationError.Conflict("La operación no tiene un resultado recuperable."))
                : Result.Success(replay);
        }
        var terminal = await sales.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null) return Result.Failure<CreditPaymentOperationDto>(ApplicationError.NotFound("la terminal activa"));
        var shift = await sales.GetOpenShiftForUpdateAsync(cancellationToken);
        if (shift is null || shift.TerminalId != terminal.Id)
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Conflict("Debe existir un turno abierto en la terminal activa."));
        SesionOperador? session = null;
        if (shift.ModoOperacion == CashConstants.IndividualMode)
        {
            if (shift.UsuarioResponsableId != actor.UserId)
                return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Forbidden());
        }
        else if (shift.ModoOperacion == CashConstants.SharedMode)
        {
            session = await sales.GetActiveOperatorSessionAsync(shift.Id, terminal.Id, actor.UserId, cancellationToken);
            if (session is null) return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Conflict("Se requiere sesión activa del operador."));
        }
        else return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Conflict("Modalidad de turno inválida."));

        // The consecutive is created on first use when it does not exist yet (inside this transaction).
        await numbering.EnsureNumberingAsync(installation.Id, installation.Serie,
            DocumentNumberingDefaults.CreditPayments, cancellationToken);
        var sequence = await numbering.GetCreditPaymentNumberingAsync(installation.Id, installation.Serie, true, cancellationToken);
        if (sequence is null) return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Conflict("No se pudo obtener el consecutivo PAGO_CREDITO."));
        if (sequence.UltimoNumero > long.MaxValue - payments.Length)
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.NumberingExhausted("El consecutivo de abonos está agotado."));
        var credit = await credits.GetForUpdateAsync(creditId, cancellationToken);
        if (credit is null) return Result.Failure<CreditPaymentOperationDto>(ApplicationError.NotFound("el crédito"));
        if (credit.Version != request.ExpectedVersion)
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.ConcurrencyConflict("El crédito fue modificado. Recárguelo e intente nuevamente."));
        if (credit.Estado is "PAGADO" or "ANULADO")
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Conflict("El crédito no admite más abonos."));
        if (amount > credit.SaldoPendiente)
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("El abono no puede superar el saldo pendiente."));
        var methods = await sales.GetPaymentMethodsAsync(payments.Select(x => x.MethodCode).ToArray(), cancellationToken);
        if (methods.Count != payments.Length || methods.Any(x => !x.Activo) ||
            methods.Any(x => x.Codigo == "EFECTIVO" ? !x.AfectaEfectivo : x.AfectaEfectivo))
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("Los métodos de pago deben estar activos y conservar su clasificación."));
        var byCode = methods.ToDictionary(x => x.Codigo);
        if (payments.Any(x => byCode[x.MethodCode].RequiereReferencia && string.IsNullOrWhiteSpace(x.Reference)))
            return Result.Failure<CreditPaymentOperationDto>(ApplicationError.Validation("La referencia electrónica es obligatoria."));
        var now = clock.UtcNow;
        var today = await TodayAsync(cancellationToken);
        var correlationId = Guid.CreateVersion7();
        var movement = MovimientoCaja.Create(shift.Id, actor.UserId, session?.Id,
            CashConstants.IncomeMovement, "ABONO_CREDITO", amount,
            "Abono a crédito", correlationId, now);
        var rows = payments.Select(x =>
        {
            sequence.UltimoNumero++;
            return new PagoCredito
            {
                Id = Guid.CreateVersion7(), CreditoId = credit.Id, InstalacionId = installation.Id,
                TurnoCajaId = shift.Id, MetodoPagoId = byCode[x.MethodCode].Id, UsuarioId = actor.UserId,
                SesionOperadorId = session?.Id, MovimientoCajaId = movement.Id,
                Prefijo = sequence.Prefijo, Serie = sequence.Serie, Consecutivo = sequence.UltimoNumero,
                ValorAplicado = x.AppliedAmount, ValorRecibido = x.ReceivedAmount,
                Cambio = x.ReceivedAmount is decimal received ? received - x.AppliedAmount : 0,
                Referencia = x.Reference, FechaHora = now.UtcDateTime, Estado = "APLICADO"
            };
        }).ToArray();
        credit.SaldoPendiente -= amount;
        credit.Estado = credit.SaldoPendiente == 0 ? "PAGADO" :
            CreditStatusPolicy.ResolveOpenState(credit.FechaVencimiento, credit.SaldoPendiente, today);
        credit.FechaPago = credit.SaldoPendiente == 0 ? today : null;
        await sales.AddCashMovementAsync(movement, cancellationToken);
        await credits.AddPaymentsAsync(rows, cancellationToken);
        var response = new CreditPaymentOperationDto(credit.Id, credit.SaldoPendiente, credit.Estado,
            credit.Version + 1, correlationId,
            rows.Select(x => Map(x, byCode[payments.Single(p => p.AppliedAmount == x.ValorAplicado &&
                byCode[p.MethodCode].Id == x.MetodoPagoId).MethodCode], correlationId)).ToArray());
        await sales.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, SesionOperadorId = session?.Id, ClaveIdempotencia = key,
            TipoOperacion = "ABONAR_CREDITO", HashSolicitud = hash, Entidad = nameof(Credito),
            EntidadId = credit.Id, Resultado = JsonSerializer.Serialize(response),
            CorrelacionId = correlationId, FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteAsync(audits, clock, actor.UserId, AuditAction.CreditPaymentApplied,
            nameof(Credito), credit.Id, new { credit.Id, credit.Estado, CorrelationId = correlationId,
                PaymentIds = rows.Select(x => x.Id).ToArray() }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (session is not null)
        {
            session.Close(now, "CAMBIO_OPERADOR");
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(response);
    }

    private async Task<ApplicationError?> RequireAsync(ActorContext actor, string permission,
        CancellationToken cancellationToken)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        return snapshot is null ? ApplicationError.Unauthorized()
            : snapshot.PermissionCodes.Contains(permission, StringComparer.OrdinalIgnoreCase)
                ? null : ApplicationError.Forbidden();
    }

    private async Task<DateOnly> TodayAsync(CancellationToken cancellationToken)
    {
        var establishment = await sales.GetActiveEstablishmentAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(establishment?.ZonaHoraria ?? "America/Bogota");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    private static (int Offset, ApplicationError? Error) ValidatePagination(int page, int pageSize)
    {
        if (page <= 0 || pageSize is < 1 or > 100)
            return (0, ApplicationError.Validation(
                "La página debe ser positiva y pageSize debe estar entre 1 y 100."));

        var offset = ((long)page - 1L) * pageSize;
        return offset > int.MaxValue
            ? (0, ApplicationError.Validation("La página solicitada excede el rango admitido."))
            : ((int)offset, null);
    }
    private static CreditDto Map(Credito credit, DateOnly today)
    {
        var sale = credit.Venta;
        var customer = sale.Cliente!;
        return new CreditDto(credit.Id, sale.Id, $"{sale.Prefijo}-{sale.Serie}-{sale.Consecutivo}",
            customer.Id, customer.NombreCompleto, customer.TipoDocumento, customer.NumeroDocumento,
            customer.Telefono, credit.MontoOriginal, credit.MontoAjustado, credit.SaldoPendiente,
            credit.FechaOtorgamiento, credit.FechaVencimiento, credit.FechaPago,
            credit.Estado == "PENDIENTE" && CreditStatusPolicy.IsOverdue(
                credit.FechaVencimiento, credit.SaldoPendiente, today)
                ? "VENCIDO" : credit.Estado, credit.Version);
    }

    private static CreditPaymentDto Map(PagoCredito payment) => Map(payment, payment.MetodoPago,
        payment.MovimientoCaja?.CorrelacionId ?? Guid.Empty);

    private static CreditPaymentDto Map(PagoCredito payment, MetodoPago method, Guid correlationId) =>
        new(payment.Id, $"{payment.Prefijo}-{payment.Serie}-{payment.Consecutivo}",
            method.Codigo, method.Nombre, payment.ValorAplicado, payment.ValorRecibido,
            payment.Cambio, new DateTimeOffset(DateTime.SpecifyKind(payment.FechaHora, DateTimeKind.Utc)),
            payment.Estado, correlationId);
}
