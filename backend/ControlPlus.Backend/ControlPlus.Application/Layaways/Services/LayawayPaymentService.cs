using System.Data;
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
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Layaways.Services;

public sealed class LayawayPaymentService(ILayawayPaymentRepository layaways,
    ILayawayCreateRepository creation, ISalesRepository sales,
    ISalesTransactionManager transactions, IUserRepository users,
    IAuditRepository audits, IUnitOfWork unitOfWork, IClock clock) : ILayawayPaymentService
{
    public async Task<Result<LayawayPaymentOperationDto>> ApplyAsync(ActorContext actor,
        Guid layawayId, string idempotencyKey, ApplyLayawayPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var input = Normalize(layawayId, idempotencyKey, request);
        if (input.Error is not null) return Fail(input.Error);

        await using var transaction = await transactions.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Fail(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Fail(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.LayawayManage, StringComparer.OrdinalIgnoreCase))
            return Fail(ApplicationError.Forbidden());

        var previous = await sales.GetIdempotencyAsync(installation.Id, input.Key!, cancellationToken);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "ABONAR_APARTADO" ||
                previous.HashSolicitud != input.Hash || string.IsNullOrEmpty(previous.Resultado))
                return Fail(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            var replay = JsonSerializer.Deserialize<LayawayPaymentOperationDto>(previous.Resultado);
            return replay is null
                ? Fail(ApplicationError.Conflict("La operación idempotente no tiene resultado recuperable."))
                : Result.Success(replay);
        }

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
            session = await creation.GetActiveOperatorSessionForUpdateAsync(
                shift.Id, terminal.Id, actor.UserId, cancellationToken);
            if (session is null)
                return Fail(SharedShift.CardRequired());
        }
        else return Fail(ApplicationError.Conflict("La modalidad del turno no es válida."));

        var sequence = await creation.GetSequenceForUpdateAsync(
            installation.Id, installation.Serie, "PAGO_APARTADO", cancellationToken);
        if (sequence is null || sequence.Prefijo != "PAP")
            return Fail(ApplicationError.Conflict("Configure explícitamente el consecutivo PAP."));
        if (sequence.UltimoNumero > long.MaxValue - input.Payments!.Length)
            return Fail(ApplicationError.NumberingExhausted("El consecutivo PAP está agotado."));
        var layaway = await layaways.GetForUpdateAsync(layawayId, cancellationToken);
        if (layaway is null || layaway.InstalacionId != installation.Id)
            return Fail(ApplicationError.NotFound("el apartado"));
        if (layaway.Version != request.ExpectedVersion)
            return Fail(ApplicationError.ConcurrencyConflict("El apartado fue modificado. Recárguelo."));
        if (layaway.Estado != "ACTIVO")
            return Fail(ApplicationError.Conflict("El apartado no admite más abonos."));
        if (input.Amount > layaway.SaldoPendiente)
            return Fail(ApplicationError.Validation("El abono no puede superar el saldo pendiente."));

        var methods = await sales.GetPaymentMethodsAsync(
            input.Payments.Select(x => x.MethodCode).ToArray(), cancellationToken);
        if (methods.Count != input.Payments.Length || methods.Any(x => !x.Activo) ||
            methods.Any(x => x.Codigo == "EFECTIVO" ? !x.AfectaEfectivo : x.AfectaEfectivo))
            return Fail(ApplicationError.Validation("Los métodos de pago deben estar activos y conservar su clasificación."));
        var byCode = methods.ToDictionary(x => x.Codigo);
        // Desde el 21 de noviembre de 2026 la referencia de Transferencia y Nequi es opcional (propuesta 7).

        var now = clock.UtcNow;
        var correlationId = Guid.CreateVersion7();
        var movement = MovimientoCaja.Create(shift.Id, actor.UserId, session?.Id,
            CashConstants.IncomeMovement, "ABONO_APARTADO", input.Amount,
            $"Abono a apartado {layaway.Prefijo}-{layaway.Serie}-{layaway.Consecutivo}",
            correlationId, now);
        var rows = input.Payments.Select(payment =>
        {
            sequence.UltimoNumero++;
            return new PagoApartado
            {
                Id = Guid.CreateVersion7(), ApartadoId = layaway.Id, InstalacionId = installation.Id,
                TurnoCajaId = shift.Id, MetodoPagoId = byCode[payment.MethodCode].Id,
                UsuarioId = actor.UserId, SesionOperadorId = session?.Id, MovimientoCajaId = movement.Id,
                Prefijo = sequence.Prefijo, Serie = sequence.Serie, Consecutivo = sequence.UltimoNumero,
                ValorAplicado = payment.AppliedAmount, ValorRecibido = payment.ReceivedAmount,
                Cambio = payment.ReceivedAmount is decimal received ? received - payment.AppliedAmount : 0m,
                Referencia = payment.Reference, FechaHora = now.UtcDateTime, Estado = "APLICADO"
            };
        }).ToArray();
        layaway.SaldoPendiente -= input.Amount;
        if (layaway.SaldoPendiente == 0)
        {
            layaway.Estado = "PAGADO";
            layaway.FechaPago = now.UtcDateTime;
        }
        var response = new LayawayPaymentOperationDto(layaway.Id, layaway.SaldoPendiente,
            layaway.Estado, layaway.Version + 1, correlationId, rows.Sum(x => x.Cambio),
            rows.Select(x => new CreatedLayawayPaymentDto(
                $"{x.Prefijo}-{x.Serie}-{x.Consecutivo}",
                methods.Single(m => m.Id == x.MetodoPagoId).Codigo,
                x.ValorAplicado, x.ValorRecibido, x.Cambio)).ToArray());

        await sales.AddCashMovementAsync(movement, cancellationToken);
        await layaways.AddPaymentsAsync(rows, cancellationToken);
        await sales.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, SesionOperadorId = session?.Id,
            ClaveIdempotencia = input.Key!, TipoOperacion = "ABONAR_APARTADO",
            HashSolicitud = input.Hash!, Entidad = nameof(Apartado), EntidadId = layaway.Id,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlationId,
            FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
            AuditAction.LayawayPaymentApplied, nameof(Apartado), layaway.Id,
            new { layaway.Id, layaway.Estado, PaymentIds = rows.Select(x => x.Id).ToArray(),
                PaymentMethods = input.Payments.Select(x => x.MethodCode).ToArray() },
            correlationId, null, session?.Id, terminal.Id, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (session is not null)
        {
            session.Close(now, "CAMBIO_OPERADOR");
            await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
                AuditAction.OperatorSessionClosed, nameof(SesionOperador), session.Id,
                new { session.Estado, LayawayId = layaway.Id, SessionConsumedByLayawayPayment = true },
                correlationId, null, session.Id, terminal.Id, null, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(response);
    }

    private static NormalizedPayment Normalize(Guid layawayId, string key,
        ApplyLayawayPaymentRequest request)
    {
        if (layawayId == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Trim().Length > 100 ||
            request is null || request.ExpectedVersion <= 0 || request.Payments is null ||
            request.Payments.Count is < 1 or > 3)
            return NormalizedPayment.Fail("Idempotency-Key, versión y uno a tres pagos son obligatorios.");
        var payments = new List<CreateLayawayPaymentRequest>();
        foreach (var payment in request.Payments)
        {
            if (payment is null) return NormalizedPayment.Fail("El pago es obligatorio.");
            var code = payment.MethodCode?.Trim().ToUpperInvariant();
            var reference = string.IsNullOrWhiteSpace(payment.Reference) ? null : payment.Reference.Trim();
            if (code is not "EFECTIVO" and not "TRANSFERENCIA" and not "NEQUI" ||
                !CashMoney.IsValid(payment.AppliedAmount, allowNegative: false) ||
                payment.AppliedAmount <= 0 || reference?.Length > 200)
                return NormalizedPayment.Fail("Método, importe o referencia de pago inválidos.");
            if (code == "EFECTIVO")
            {
                if (reference is not null || payment.ReceivedAmount is not decimal received ||
                    !CashMoney.IsValid(received, allowNegative: false) || received < payment.AppliedAmount)
                    return NormalizedPayment.Fail("El efectivo requiere recibido suficiente y no admite referencia.");
            }
            else if (payment.ReceivedAmount is not null)
                return NormalizedPayment.Fail("Transferencia y Nequi no admiten recibido.");
            payments.Add(new CreateLayawayPaymentRequest(code, payment.AppliedAmount,
                payment.ReceivedAmount, reference));
        }
        if (payments.Select(x => x.MethodCode).Distinct().Count() != payments.Count)
            return NormalizedPayment.Fail("No se puede repetir un método de pago.");
        decimal amount;
        try { amount = CashMoney.Sum(payments.Select(x => x.AppliedAmount)); }
        catch (CashMoneyException) { return NormalizedPayment.Fail("El importe acumulado excede el rango permitido."); }
        catch (OverflowException) { return NormalizedPayment.Fail("El importe acumulado excede el rango permitido."); }
        var sorted = payments.OrderBy(x => x.MethodCode).ToArray();
        var canonical = JsonSerializer.Serialize(new { layawayId, request.ExpectedVersion, Payments = sorted });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new NormalizedPayment(key.Trim(), sorted, amount, hash, null);
    }

    private static Result<LayawayPaymentOperationDto> Fail(ApplicationError error) =>
        Result.Failure<LayawayPaymentOperationDto>(error);

    private sealed record NormalizedPayment(string? Key, CreateLayawayPaymentRequest[]? Payments,
        decimal Amount, string? Hash, ApplicationError? Error)
    {
        public static NormalizedPayment Fail(string message) =>
            new(null, null, 0, null, ApplicationError.Validation(message));
    }
}
