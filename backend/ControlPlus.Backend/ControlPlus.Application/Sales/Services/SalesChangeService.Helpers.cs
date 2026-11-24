using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Sales.Services;

public sealed partial class SalesService
{
    private sealed record ChangeInput(string Key, string Hash, string Treatment,
        string? Reason, IReadOnlyCollection<SaleChangePaymentRequest> Payments, ApplicationError? Error);

    private sealed record ChangeTreatment(string Name, decimal CreditAmount, decimal CashAmount,
        ApplicationError? Error);

    private sealed record ChangePaymentCheck(IReadOnlyDictionary<Guid, MetodoPago> Methods,
        ApplicationError? Error);

    private static ChangeInput NormalizeChange(Guid saleId, string idempotencyKey,
        ConfirmSaleChangeRequest request)
    {
        static ChangeInput Fail(string message) => new("", "", "", null, [], Validation(message));
        var key = idempotencyKey?.Trim() ?? "";
        if (saleId == Guid.Empty || request is null || request.ExpectedSaleVersion is <= 0 or long.MaxValue ||
            key.Length is < 1 or > 100 ||
            key.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':')))
            return Fail("La venta, versión o Idempotency-Key es inválida.");
        if (request.Returned is null || request.Delivered is null || request.Payments is null ||
            request.Returned.Count == 0 ||
            request.Returned.Count > 100 || request.Delivered.Count > 100 || request.Payments.Count > 3)
            return Fail("Selecciona al menos un producto para quitar o cambiar, con límites válidos.");
        if (request.Returned.Any(x => x.SaleLineId == Guid.Empty || x.Quantity <= 0 || !x.Resellable) ||
            request.Returned.Select(x => x.SaleLineId).Distinct().Count() != request.Returned.Count)
            return Fail("Cada producto devuelto debe ser revendible, único y tener cantidad positiva.");
        if (request.Delivered.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0 ||
                NormalizeCode(x.PriceType) is not ("MINORISTA" or "MAYORISTA")) ||
            request.Delivered.Select(x => x.ProductId).Distinct().Count() != request.Delivered.Count)
            return Fail("Cada producto entregado debe ser único, tener cantidad positiva y precio minorista o mayorista.");
        if (request.Payments.Any(x => !ApprovedPaymentMethods.Contains(NormalizeCode(x.MethodCode)) ||
                x.Amount <= 0 || x.Amount > CashMoney.Maximum || decimal.Truncate(x.Amount) != x.Amount ||
                x.ReceivedAmount is decimal received &&
                    (received < 0 || received > CashMoney.Maximum || decimal.Truncate(received) != received) ||
                x.Reference?.Length > 200) ||
            request.Payments.Select(x => NormalizeCode(x.MethodCode)).Distinct().Count() != request.Payments.Count)
            return Fail("Los medios de pago deben ser únicos y contener importes enteros positivos.");
        if (request.ExpectedCreditVersion is <= 0 || request.Reason?.Length > 500)
            return Fail("La versión del crédito o el motivo es inválido.");
        var treatment = NormalizeCode(request.Treatment);
        if (treatment is not ("AUTO" or "PAGO_INMEDIATO" or "AGREGAR_CREDITO"))
            return Fail("El tratamiento debe ser AUTO, PAGO_INMEDIATO o AGREGAR_CREDITO.");
        var reason = NormalizeOptional(request.Reason);
        if (request.Delivered.Count == 0 && reason is null)
            return Fail("Selecciona un motivo para quitar productos de la venta.");
        var payments = request.Payments.Select(x => x with
        {
            MethodCode = NormalizeCode(x.MethodCode), Reference = NormalizeOptional(x.Reference)
        }).OrderBy(x => x.MethodCode, StringComparer.Ordinal).ToArray();
        var canonical = JsonSerializer.Serialize(new
        {
            SaleId = saleId, request.ExpectedSaleVersion, request.ExpectedCreditVersion,
            Returned = request.Returned.OrderBy(x => x.SaleLineId),
            Delivered = request.Delivered.Select(x => x with { PriceType = NormalizeCode(x.PriceType) })
                .OrderBy(x => x.ProductId),
            Treatment = treatment, Payments = payments, Reason = reason
        });
        return new ChangeInput(key, Hash(canonical), treatment, reason, payments, null);
    }

    private static ChangeTreatment ResolveChangeTreatment(string saleType, decimal difference,
        string requested, Credito? credit)
    {
        static ChangeTreatment Fail(string message) => new("", 0, 0, Validation(message));
        if (difference == 0)
            return requested == "AUTO" ? new("SIN_DIFERENCIA", 0, 0, null)
                : Fail("Un cambio sin diferencia exige tratamiento AUTO.");
        if (difference > 0)
        {
            if (requested == "AGREGAR_CREDITO" && credit is not null)
                return new("AGREGAR_CREDITO", difference, 0, null);
            if (requested == "PAGO_INMEDIATO" || requested == "AUTO" && saleType == "CONTADO")
                return new("PAGO_INMEDIATO", 0, difference, null);
            return Fail("Indique PAGO_INMEDIATO o AGREGAR_CREDITO para la diferencia positiva del crédito.");
        }
        if (requested != "AUTO") return Fail("Una diferencia negativa exige tratamiento AUTO.");
        var refund = -difference;
        if (credit is null) return new("DEVOLUCION", 0, refund, null);
        var reduction = Math.Min(refund, credit.SaldoPendiente);
        return reduction == refund
            ? new("REDUCIR_CREDITO", reduction, 0, null)
            : reduction > 0
                ? new("REDUCIR_CREDITO_Y_DEVOLVER", reduction, refund - reduction, null)
                : Fail("El crédito no tiene saldo reducible.");
    }

    private async Task<ChangePaymentCheck> ValidateChangePaymentsAsync(Venta sale,
        Guid shiftId, decimal difference, decimal cashAmount,
        IReadOnlyCollection<SaleChangePaymentRequest> payments, CancellationToken ct)
    {
        static ChangePaymentCheck Fail(string message) => new(
            new Dictionary<Guid, MetodoPago>(), Validation(message));
        if (payments.Sum(x => x.Amount) != cashAmount ||
            (cashAmount == 0 && payments.Count != 0) ||
            (cashAmount > 0 && payments.Count == 0))
            return Fail("La suma de cobros o devoluciones debe coincidir con la diferencia liquidada.");
        if (cashAmount == 0) return new(new Dictionary<Guid, MetodoPago>(), null);
        var methods = await repository.GetPaymentMethodsAsync(payments.Select(x => x.MethodCode).ToArray(), ct);
        if (methods.Count != payments.Count || methods.Any(x => x.Codigo == "EFECTIVO"
                ? !x.AfectaEfectivo : x.AfectaEfectivo))
            return Fail("Los medios de pago no conservan la clasificación aprobada.");
        var byCode = methods.ToDictionary(x => x.Codigo, StringComparer.Ordinal);
        if (difference > 0 && methods.Any(x => !x.Activo))
            return Fail("Los medios de cobro deben estar activos.");
        foreach (var payment in payments)
        {
            var method = byCode[payment.MethodCode];
            // Decisión D8 del 23/11/2026: la referencia de Nequi y transferencia es opcional también en los cambios.
            if (method.AfectaEfectivo && payment.Reference is not null)
                return Fail("El efectivo no admite referencia electrónica.");
            if (difference < 0 && payment.ReceivedAmount is not null ||
                difference > 0 && method.AfectaEfectivo &&
                (payment.ReceivedAmount is null || payment.ReceivedAmount < payment.Amount) ||
                difference > 0 && !method.AfectaEfectivo && payment.ReceivedAmount is not null)
                return Fail("El importe recibido solo corresponde a cobros en efectivo.");
        }
        if (difference < 0 && sale.TipoVenta == "CONTADO")
        {
            var original = sale.PagoVenta.Where(x => x.Estado == "APLICADO")
                .GroupBy(x => x.MetodoPagoId)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.ValorAplicado));
            var priorNet = await repository.GetConfirmedChangeNetPaymentsByMethodAsync(sale.Id, ct);
            if (payments.Any(x => !original.TryGetValue(byCode[x.MethodCode].Id, out var paid) ||
                    x.Amount > paid + priorNet.GetValueOrDefault(byCode[x.MethodCode].Id)))
                return Fail("La devolución debe usar medios originales sin superar el neto cobrado y no devuelto por cada medio.");
        }
        if (difference < 0)
        {
            var cashRefund = payments.Where(x => byCode[x.MethodCode].AfectaEfectivo).Sum(x => x.Amount);
            var expected = await cashRepository.GetExpectedAmountsAsync(shiftId, ct);
            var cashAvailable = expected.Where(x => x.AffectsCash).Sum(x => x.ExpectedAmount);
            if (cashRefund > cashAvailable)
                return new(new Dictionary<Guid, MetodoPago>(),
                    ApplicationError.Conflict("El efectivo esperado no cubre la devolución."));
        }
        return new(methods.ToDictionary(x => x.Id), null);
    }

    private async Task ApplyChangeInventoryAsync(Guid actorId, Guid authorizationId,
        Guid changeId, Guid? sessionId, Guid correlation, DateTime now,
        ConfirmSaleChangeRequest request, IReadOnlyDictionary<Guid, DetalleVenta> sold,
        IReadOnlyDictionary<Guid, Producto> products, CancellationToken ct)
    {
        var movements = new List<MovimientoInventario>();
        foreach (var returned in request.Returned.OrderBy(x => sold[x.SaleLineId].ProductoId))
        {
            var product = products[sold[returned.SaleLineId].ProductoId];
            var before = product.StockActual;
            product.StockActual = checked(before + returned.Quantity);
            movements.Add(NewChangeMovement(product, actorId, authorizationId, changeId,
                sessionId, correlation, now, "CAMBIO_DEVOLUCION", returned.Quantity, before));
        }
        foreach (var delivered in request.Delivered.OrderBy(x => x.ProductId))
        {
            var product = products[delivered.ProductId];
            var before = product.StockActual;
            product.StockActual = checked(before - delivered.Quantity);
            movements.Add(NewChangeMovement(product, actorId, authorizationId, changeId,
                sessionId, correlation, now, "CAMBIO_ENTREGA", -delivered.Quantity, before));
        }
        await repository.AddInventoryMovementsAsync(movements, ct);
    }

    private static MovimientoInventario NewChangeMovement(Producto product, Guid actorId,
        Guid authorizationId, Guid changeId, Guid? sessionId, Guid correlation, DateTime now,
        string type, int quantity, int before) => new()
    {
        Id = Guid.CreateVersion7(), ProductoId = product.Id, UsuarioId = actorId,
        SesionOperadorId = sessionId, AutorizacionOperacionId = authorizationId,
        CambioVentaId = changeId, TipoMovimiento = type, CantidadStock = quantity,
        CantidadReservada = 0, StockAnterior = before, StockPosterior = product.StockActual,
        ReservadoAnterior = product.StockReservado, ReservadoPosterior = product.StockReservado,
        CostoPromedioAnterior = product.CostoPromedio, CostoPromedioPosterior = product.CostoPromedio,
        FechaHora = now, CorrelacionId = correlation
    };

    private async Task ApplyChangeCreditAsync(Guid actorId, Guid changeId, Guid correlation,
        DateTime now, DateOnly today, Credito credit, decimal difference, decimal amount,
        CancellationToken ct)
    {
        var beforeAdjusted = credit.MontoAjustado;
        var beforeBalance = credit.SaldoPendiente;
        var increasing = difference > 0;
        var installments = (await repository.GetCreditInstallmentsForUpdateAsync(credit.Id, ct)).ToArray();
        if (installments.Length == 0 || installments.Sum(x => x.SaldoPendiente) != beforeBalance)
            throw new InvalidOperationException("El plan de cuotas no coincide con el saldo del crédito.");
        if (increasing)
        {
            var last = installments.OrderByDescending(x => x.FechaVencimiento).ThenByDescending(x => x.Numero).First();
            last.Monto = CashMoney.Add(last.Monto, amount);
            last.SaldoPendiente = CashMoney.Add(last.SaldoPendiente, amount);
            last.FechaPago = null;
        }
        else
        {
            var remaining = amount;
            foreach (var installment in installments.Where(x => x.SaldoPendiente > 0)
                         .OrderByDescending(x => x.FechaVencimiento).ThenByDescending(x => x.Numero))
            {
                var reduction = Math.Min(remaining, installment.SaldoPendiente);
                installment.Monto = CashMoney.Subtract(installment.Monto, reduction);
                installment.SaldoPendiente = CashMoney.Subtract(installment.SaldoPendiente, reduction);
                if (installment.SaldoPendiente == 0) installment.FechaPago = today;
                remaining -= reduction;
                if (remaining == 0) break;
            }
            if (remaining != 0) throw new InvalidOperationException("Las cuotas no admiten la reducción solicitada.");
        }
        credit.MontoAjustado = increasing ? CashMoney.Add(beforeAdjusted, amount)
            : CashMoney.Subtract(beforeAdjusted, amount);
        credit.SaldoPendiente = increasing ? CashMoney.Add(beforeBalance, amount)
            : CashMoney.Subtract(beforeBalance, amount);
        credit.Estado = credit.SaldoPendiente == 0 ? "PAGADO"
            : ControlPlus.Domain.Credits.CreditStatusPolicy.ResolveOpenState(
                installments.Select(x => (x.FechaVencimiento, x.SaldoPendiente)), today);
        credit.FechaPago = credit.SaldoPendiente == 0 ? today : null;
        await repository.AddCreditChangeAdjustmentAsync(new AjusteCreditoCambio
        {
            Id = Guid.CreateVersion7(), CreditoId = credit.Id, CambioVentaId = changeId,
            UsuarioId = actorId, TipoAjuste = increasing ? "AUMENTO" : "REDUCCION",
            Valor = amount, MontoAjustadoAnterior = beforeAdjusted,
            MontoAjustadoPosterior = credit.MontoAjustado, SaldoAnterior = beforeBalance,
            SaldoPosterior = credit.SaldoPendiente, FechaHora = now, CorrelacionId = correlation
        }, ct);
    }

    private async Task<IReadOnlyCollection<PagoCambioVenta>> ApplyChangePaymentsAsync(Guid actorId,
        Guid shiftId, Guid? sessionId, Guid changeId, Guid correlation, DateTimeOffset now,
        decimal difference, IReadOnlyCollection<SaleChangePaymentRequest> payments,
        IReadOnlyDictionary<Guid, MetodoPago> methods, CancellationToken ct)
    {
        var byCode = methods.Values.ToDictionary(x => x.Codigo);
        var rows = new List<PagoCambioVenta>();
        foreach (var payment in payments)
        {
            var method = byCode[payment.MethodCode];
            var movement = MovimientoCaja.Create(shiftId, actorId, sessionId,
                difference > 0 ? CashConstants.IncomeMovement : CashConstants.ExpenseMovement,
                "CAMBIO_VENTA", payment.Amount, "Diferencia de cambio de productos", correlation, now);
            await repository.AddCashMovementAsync(movement, ct);
            rows.Add(new PagoCambioVenta
            {
                Id = Guid.CreateVersion7(), CambioVentaId = changeId, TurnoCajaId = shiftId,
                MetodoPagoId = method.Id, UsuarioId = actorId, SesionOperadorId = sessionId,
                MovimientoCajaId = movement.Id, TipoMovimiento = difference > 0 ? "COBRO" : "DEVOLUCION",
                Valor = payment.Amount, ValorRecibido = payment.ReceivedAmount,
                Cambio = payment.ReceivedAmount is decimal received ? received - payment.Amount : 0,
                Referencia = payment.Reference, FechaHora = now.UtcDateTime, Estado = "APLICADO"
            });
        }
        if (rows.Count > 0) await repository.AddChangePaymentsAsync(rows, ct);
        return rows;
    }

    private static bool CanReadSale(IReadOnlyCollection<string> permissions, Guid actorId, Venta sale) =>
        permissions.Contains(PermissionCodes.SalesAllRead, StringComparer.OrdinalIgnoreCase) ||
        sale.UsuarioId == actorId &&
        permissions.Contains(PermissionCodes.SalesOwnRead, StringComparer.OrdinalIgnoreCase);

    private static SaleChangeDto MapChange(CambioVenta change) => new(change.Id, change.VentaId,
        $"{change.Prefijo}-{change.Serie}-{change.Consecutivo}", change.Estado,
        new DateTimeOffset(DateTime.SpecifyKind(change.FechaHora, DateTimeKind.Utc)),
        change.TotalDevuelto, change.TotalEntregado, change.Diferencia,
        change.TratamientoDiferencia, change.ValorAjusteCredito,
        change.ValorPagoODevolucion, change.CorrelacionId,
        change.DetalleCambioVenta.OrderBy(x => x.TipoDetalle).ThenBy(x => x.ProductoId)
            .Select(MapChangeDetail).ToArray(),
        change.PagoCambioVenta.OrderBy(x => x.MetodoPago.Codigo)
            .Select(x => MapChangePayment(x, x.MetodoPago)).ToArray(), change.Motivo);

    private static SaleChangeDetailDto MapChangeDetail(DetalleCambioVenta row) =>
        new(row.Id, row.TipoDetalle, row.ProductoId, row.DetalleVentaOrigenId,
            row.Cantidad, row.PrecioUnitario, row.ValorDescuento, row.SubtotalNeto,
            row.CodigoProducto, row.NombreProducto, row.UnidadMedida);

    private static SaleChangePaymentDto MapChangePayment(PagoCambioVenta row, MetodoPago method) =>
        new(row.Id, row.TipoMovimiento, method.Codigo, row.Valor, row.ValorRecibido, row.Cambio,
            row.Referencia);
}
