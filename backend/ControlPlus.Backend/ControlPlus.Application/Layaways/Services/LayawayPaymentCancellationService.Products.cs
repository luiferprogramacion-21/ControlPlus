using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.Common;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Sales;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Layaways.Services;

public sealed partial class LayawayPaymentCancellationService
{
    public async Task<Result<IReadOnlyCollection<LayawayCorrectionDto>>> HistoryAsync(ActorContext actor,
        Guid layawayId, CancellationToken cancellationToken = default)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null || !snapshot.PermissionCodes.Contains(PermissionCodes.LayawayManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<IReadOnlyCollection<LayawayCorrectionDto>>(ApplicationError.Forbidden());
        var installation = await sales.GetActiveInstallationAsync(cancellationToken);
        // The header is read under the same lock used by the update so foreign IDs never leak history.
        var layaway = await layaways.GetForUpdateAsync(layawayId, cancellationToken);
        if (installation is null || layaway is null || layaway.InstalacionId != installation.Id)
            return Result.Failure<IReadOnlyCollection<LayawayCorrectionDto>>(ApplicationError.NotFound("el apartado"));
        return Result.Success<IReadOnlyCollection<LayawayCorrectionDto>>((await correction.HistoryAsync(layawayId, cancellationToken)).Select(MapCorrection).ToArray());
    }

    public async Task<Result<LayawayCorrectionDto>> CorrectAsync(ActorContext actor, Guid layawayId,
        string key, CorrectLayawayProductsRequest request, CancellationToken cancellationToken = default)
    {
        if (await ActorAsync(actor.UserId, cancellationToken) is null) return ProductFail(ApplicationError.Forbidden());
        key = key?.Trim() ?? "";
        if (layawayId == Guid.Empty || request is null || request.ExpectedVersion <= 0 || request.ExpectedVersion == long.MaxValue ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500 ||
            request.Lines is null || request.Lines.Count is < 1 or > 200 || request.Refunds is null || request.Refunds.Count > 3 ||
            request.Lines.Any(l => l is null || l.ProductId == Guid.Empty || l.Quantity <= 0) ||
            request.Lines.Select(l => l.ProductId).Distinct().Count() != request.Lines.Count ||
            key.Length is < 1 or > 100 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':') ||
            (request.Authorization is not null && !request.Authorization.IsComplete()))
            return ProductFail(ApplicationError.Validation("Selecciona productos y cantidades enteras positivas, versión y un motivo de hasta 500 caracteres."));
        var refunds = new List<LayawayRefundRequest>();
        foreach (var row in request.Refunds)
        {
            if (row is null || row.MethodCode?.Trim().ToUpperInvariant() is not ("EFECTIVO" or "TRANSFERENCIA" or "NEQUI") ||
                !CashMoney.IsValid(row.Amount, allowNegative: false) || row.Amount <= 0 || row.Reference?.Trim().Length > 200)
                return ProductFail(ApplicationError.Validation("Revisa los medios e importes de devolución."));
            var code = row.MethodCode.Trim().ToUpperInvariant();
            var reference = string.IsNullOrWhiteSpace(row.Reference) ? null : row.Reference.Trim();
            // Decisión D8 del 23/11/2026: la referencia de una devolución electrónica es opcional.
            if (code == "EFECTIVO" && reference is not null)
                return ProductFail(ApplicationError.Validation("La devolución en efectivo no lleva referencia."));
            refunds.Add(new(code, row.Amount, reference));
        }
        if (refunds.Select(r => r.MethodCode).Distinct().Count() != refunds.Count)
            return ProductFail(ApplicationError.Validation("No repitas un medio de devolución."));
        request = request with { Reason = request.Reason.Trim(), Lines = request.Lines.OrderBy(l => l.ProductId).ToArray(), Refunds = refunds.OrderBy(r => r.MethodCode).ToArray() };
        if (request.Authorization is { } auth && auth.UsesCredential())
        {
            if (auth.MixesCredentialWithPassword()) return ProductFail(ApplicationError.Validation("Envíe carné o usuario y contraseña, no ambos."));
            var match = await credentialResolver.ResolveAsync(auth.CredentialToken!, cancellationToken);
            request = request with { Authorization = auth with { UserName = match?.UserName ?? AuthorizerCredentialResolver.UnknownUserName, ResolvedCredentialId = match?.CredentialId ?? Guid.Empty } };
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { LayawayId = layawayId, request.ExpectedVersion, request.Reason, request.Lines, request.Refunds, Actor = actor.UserId }))));
        return await attempts.ExecuteAsync(request.Authorization?.UserName?.Trim().ToUpperInvariant() ?? $"LAYAWAY-CORRECTION:{actor.UserId:N}",
            ct => CorrectWithinAsync(actor, layawayId, key, request, fingerprint, ct), cancellationToken);
    }

    private async Task<Result<LayawayCorrectionDto>> CorrectWithinAsync(ActorContext actor, Guid layawayId,
        string key, CorrectLayawayProductsRequest request, string fingerprint, CancellationToken ct)
    {
        var installation = await sales.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return ProductFail(ApplicationError.NotFound("la instalación activa"));
        var actorSnapshot = await ActorAsync(actor.UserId, ct);
        if (actorSnapshot is null) return ProductFail(ApplicationError.Forbidden());
        var previous = await sales.GetIdempotencyAsync(installation.Id, key, ct);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "CORREGIR_APARTADO" || previous.EntidadId != layawayId || previous.HashSolicitud != fingerprint || string.IsNullOrWhiteSpace(previous.Resultado))
                return ProductFail(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            try { return JsonSerializer.Deserialize<LayawayCorrectionDto>(previous.Resultado) is { } saved ? Result.Success(saved) : ProductFail(ApplicationError.Conflict("La corrección no es recuperable.")); }
            catch (JsonException) { return ProductFail(ApplicationError.Conflict("La corrección no es recuperable.")); }
        }
        var administrator = actorSnapshot.HighestRoleLevel == RoleLevel.Administrador;
        if (administrator && request.Authorization is not null) return ProductFail(ApplicationError.Validation("El Administrador corrige sin un segundo autorizador."));
        if (!administrator && request.Authorization is null) return ProductFail(ApplicationError.Validation("El Supervisor requiere autorización de otro Administrador."));
        var terminal = await sales.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null) return ProductFail(ApplicationError.NotFound("la terminal activa"));
        var shift = await sales.GetOpenShiftForUpdateAsync(ct);
        if (shift is null || shift.TerminalId != terminal.Id) return ProductFail(ApplicationError.Conflict("Abre un turno de Caja para registrar la corrección."));
        SesionOperador? session = null;
        if (shift.ModoOperacion == CashConstants.IndividualMode)
        { if (shift.UsuarioResponsableId != actor.UserId) return ProductFail(ApplicationError.Forbidden("El turno individual pertenece a otro responsable.")); }
        else if (shift.ModoOperacion == CashConstants.SharedMode)
        { session = await sales.GetActiveOperatorSessionForUpdateAsync(shift.Id, terminal.Id, actor.UserId, ct); if (session is null) return ProductFail(ApplicationError.Conflict("Identifícate con tu carné en el turno compartido.")); }
        else return ProductFail(ApplicationError.Conflict("La modalidad del turno no es válida."));
        var layaway = await layaways.GetForUpdateAsync(layawayId, ct);
        if (layaway is null || layaway.InstalacionId != installation.Id) return ProductFail(ApplicationError.NotFound("el apartado"));
        if (layaway.Version != request.ExpectedVersion) return ProductFail(ApplicationError.ConcurrencyConflict("El apartado cambió. Actualízalo antes de corregir."));
        if (layaway.Estado is not ("ACTIVO" or "PAGADO")) return ProductFail(ApplicationError.Conflict("Un apartado entregado o cancelado no admite esta corrección; su devolución requiere una operación propia."));
        var old = await correction.LinesForUpdateAsync(layawayId, ct);
        if (old.Count == request.Lines.Count && old.All(l => request.Lines.Any(n => n.ProductId == l.ProductoId && n.Quantity == l.Cantidad)))
            return ProductFail(ApplicationError.Validation("No cambiaste ningún producto ni cantidad."));
        if (layaway.AmbitoDescuento != "NINGUNO") return ProductFail(ApplicationError.Conflict("Este apartado histórico tiene descuentos distribuidos. Requiere una revisión específica para conservarlos."));
        var products = await sales.GetProductsForUpdateAsync(old.Select(l => l.ProductoId).Concat(request.Lines.Select(l => l.ProductId)).Distinct().ToArray(), ct);
        var units = await sales.GetMeasurementUnitNamesAsync(products.Select(p => p.UnidadMedidaId).Distinct().ToArray(), ct);
        var newLines = new List<DetalleApartado>(); var movements = new List<MovimientoInventario>();
        var now = clock.UtcNow; var correlation = Guid.CreateVersion7();
        try
        {
            foreach (var product in products.OrderBy(p => p.Id))
            {
                var historic = old.SingleOrDefault(l => l.ProductoId == product.Id);
                var input = request.Lines.SingleOrDefault(l => l.ProductId == product.Id);
                var newQuantity = input?.Quantity ?? 0; var delta = checked(newQuantity - (historic?.Cantidad ?? 0));
                if (delta > 0 && (!product.Activo || delta > product.StockActual - product.StockReservado))
                    return ProductFail(ApplicationError.Conflict($"No hay existencias disponibles de {product.Nombre} para reservar la cantidad nueva."));
                if (product.StockReservado + delta < 0 || product.StockReservado + delta > product.StockActual)
                    return ProductFail(ApplicationError.Conflict("Las reservas cambiaron; actualiza el apartado."));
                if (newQuantity > 0)
                {
                    if (historic is not null)
                    {
                        var gross = CashMoney.Require(checked(historic.PrecioReserva * newQuantity), allowNegative: false);
                        var basis = historic.PorcentajeIva is decimal rate ? decimal.Round(gross / (1m + rate / 100m), 0, MidpointRounding.AwayFromZero) : (decimal?)null;
                        newLines.Add(new DetalleApartado { Id = Guid.CreateVersion7(), ApartadoId = layaway.Id, ProductoId = product.Id,
                            CodigoProducto = historic.CodigoProducto, NombreProducto = historic.NombreProducto, UnidadMedida = historic.UnidadMedida,
                            Cantidad = newQuantity, PrecioReserva = historic.PrecioReserva, CostoUnitario = historic.CostoUnitario,
                            PorcentajeIva = historic.PorcentajeIva, SubtotalBruto = gross, SubtotalNeto = gross, OrigenDescuento = "NINGUNO",
                            BaseGravable = basis, ValorIva = basis is decimal b ? gross - b : null, Vigente = true });
                    }
                    else
                    {
                        if (product.CostoPromedio is not decimal cost) return ProductFail(ApplicationError.Validation($"Registra el costo de {product.Nombre} antes de reservarlo."));
                        var price = product.PrecioMinorista; var total = CashMoney.Require(checked(price * newQuantity), allowNegative: false);
                        SalePricingPolicy.EnsureNotBelowCost(total, cost, newQuantity);
                        if (price <= 0) return ProductFail(ApplicationError.Validation("El precio de los productos nuevos debe ser positivo."));
                        newLines.Add(new DetalleApartado { Id = Guid.CreateVersion7(), ApartadoId = layaway.Id, ProductoId = product.Id,
                            CodigoProducto = product.CodigoInterno, NombreProducto = product.Nombre, UnidadMedida = units[product.UnidadMedidaId],
                            Cantidad = newQuantity, PrecioReserva = price, CostoUnitario = cost,
                            SubtotalBruto = total, SubtotalNeto = total, OrigenDescuento = "NINGUNO", Vigente = true });
                    }
                }
                if (delta != 0)
                {
                    var beforeReserved = product.StockReservado; product.StockReservado = checked(product.StockReservado + delta);
                    movements.Add(new MovimientoInventario { Id = Guid.CreateVersion7(), ProductoId = product.Id,
                        ApartadoId = layaway.Id, UsuarioId = actor.UserId, TipoMovimiento = delta > 0 ? "RESERVA_APARTADO" : "LIBERACION_APARTADO",
                        CantidadStock = 0, CantidadReservada = delta, StockAnterior = product.StockActual, StockPosterior = product.StockActual,
                        ReservadoAnterior = beforeReserved, ReservadoPosterior = product.StockReservado,
                        CostoPromedioAnterior = product.CostoPromedio, CostoPromedioPosterior = product.CostoPromedio,
                        FechaHora = now.UtcDateTime, CorrelacionId = correlation });
                }
            }
            if (newLines.Count != request.Lines.Count) return ProductFail(ApplicationError.NotFound("algún producto de la corrección"));
            var totalAfter = CashMoney.Sum(newLines.Select(l => l.SubtotalNeto));
            if (totalAfter <= 0) return ProductFail(ApplicationError.Validation("El apartado debe conservar al menos un producto con valor; para quitar todo usa Cancelar apartado."));
            var paid = layaway.Total - layaway.SaldoPendiente;
            var refund = Math.Max(0, paid - totalAfter); var balance = Math.Max(0, totalAfter - paid);
            if (refund > 0 && (!actorSnapshot.PermissionCodes.Contains(PermissionCodes.LayawayCancel, StringComparer.OrdinalIgnoreCase) ||
                !actorSnapshot.PermissionCodes.Contains(PermissionCodes.LayawayRefundDecide, StringComparer.OrdinalIgnoreCase))) return ProductFail(ApplicationError.Forbidden("La devolución requiere LAYAWAY.CANCEL y LAYAWAY.REFUND_DECIDE."));
            if (CashMoney.Sum(request.Refunds.Select(r => r.Amount)) != refund) return ProductFail(ApplicationError.Validation($"La devolución debe ser exactamente {refund:N0} pesos; revisa el resumen."));
            var methods = await sales.GetPaymentMethodsAsync(request.Refunds.Select(r => r.MethodCode).ToArray(), ct);
            if (methods.Count != request.Refunds.Count || methods.Any(m => !m.Activo || (m.Codigo == "EFECTIVO") != m.AfectaEfectivo)) return ProductFail(ApplicationError.Validation("Los medios de devolución deben estar activos y conservar su clasificación."));
            var expected = await cash.GetExpectedAmountsAsync(shift.Id, ct);
            if (request.Refunds.Where(r => r.MethodCode == "EFECTIVO").Sum(r => r.Amount) > CashMoney.Sum(expected.Where(e => e.AffectsCash).Select(e => e.ExpectedAmount)))
                return ProductFail(ApplicationError.Conflict("La caja actual no tiene efectivo suficiente para la devolución."));
            var permission = await permissions.GetByCodeAsync(PermissionCodes.LayawayManage, ct);
            if (permission is null || !permission.Activo) return ProductFail(ApplicationError.Conflict("El permiso de apartados no está activo."));
            var authorizer = await AuthorizerAsync(actor, request.Authorization, administrator, terminal.Id, layaway.Id, ct);
            if (authorizer.Error is not null) return ProductFail(authorizer.Error);
            if (refund > 0 && !administrator)
            { var authorizerSnapshot = await users.GetAuthorizationSnapshotAsync(authorizer.User!.Id, ct);
              if (authorizerSnapshot is null || !authorizerSnapshot.PermissionCodes.Contains(PermissionCodes.LayawayCancel, StringComparer.OrdinalIgnoreCase))
                  return ProductFail(ApplicationError.Forbidden("El Administrador autorizador requiere permiso para cancelar apartados.")); }
            var authorization = new AutorizacionOperacion { Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId,
                UsuarioAutorizadorId = authorizer.User!.Id, PermisoId = permission.Id, TipoOperacion = "CORRECCION_APARTADO", CorrelacionId = correlation,
                DescripcionOperacion = "Corrección de productos de apartado sin entregar.", Motivo = request.Reason,
                MetodoAutenticacion = request.Authorization.AuthenticationMethod(), CredencialAutorizadorId = request.Authorization?.ResolvedCredentialId,
                Estado = "UTILIZADA", Resultado = "EXITOSA", FechaSolicitud = now.UtcDateTime, FechaExpiracion = now.AddMinutes(5).UtcDateTime, FechaUtilizacion = now.UtcDateTime };
            await sales.AddAuthorizationAsync(authorization, ct);
            var revision = new CorreccionApartado { Id = Guid.CreateVersion7(), ApartadoId = layaway.Id, UsuarioId = actor.UserId, AutorizacionId = authorization.Id,
                VersionAnterior = request.ExpectedVersion, VersionPosterior = request.ExpectedVersion + 1, TotalAnterior = layaway.Total,
                TotalPosterior = totalAfter, SaldoAnterior = layaway.SaldoPendiente, SaldoPosterior = balance, Reembolso = refund,
                Motivo = request.Reason, Antes = Snapshot(old), Despues = Snapshot(newLines), FechaHora = now.UtcDateTime, CorrelacionId = correlation };
            var refundRows = new List<ReembolsoCorreccionApartado>();
            if (refund > 0)
            {
                var movement = MovimientoCaja.Create(shift.Id, actor.UserId, session?.Id, "EGRESO", "DEVOLUCION_APARTADO", refund,
                    $"Corrección de apartado {layaway.Prefijo}-{layaway.Serie}-{layaway.Consecutivo}", correlation, now);
                await sales.AddCashMovementAsync(movement, ct);
                refundRows.AddRange(request.Refunds.Select(r => new ReembolsoCorreccionApartado { Id = Guid.CreateVersion7(), Correccion = revision,
                    CorreccionId = revision.Id, TurnoCajaId = shift.Id, MetodoPagoId = methods.Single(m => m.Codigo == r.MethodCode).Id,
                    MovimientoCaja = movement, MovimientoCajaId = movement.Id, Valor = r.Amount, Referencia = r.Reference }));
            }
            await correction.AddCorrectionAsync(revision, refundRows, ct);
            foreach (var line in old) line.Vigente = false;
            await correction.AppendLinesAsync(newLines, ct);
            await sales.AddInventoryMovementsAsync(movements, ct);
            layaway.Total = totalAfter; layaway.SubtotalBruto = totalAfter; layaway.SaldoPendiente = balance;
            layaway.IvaDiscriminadoCompleto = newLines.All(l => l.PorcentajeIva is not null);
            layaway.BaseGravableTotal = layaway.IvaDiscriminadoCompleto ? newLines.Sum(l => l.BaseGravable) : null;
            layaway.ImpuestoIncluidoTotal = layaway.IvaDiscriminadoCompleto ? newLines.Sum(l => l.ValorIva) : null;
            layaway.Estado = balance == 0 ? "PAGADO" : "ACTIVO"; layaway.FechaPago = balance == 0 ? now.UtcDateTime : null;
            var response = MapCorrection(revision);
            await sales.AddIdempotencyAsync(new OperacionIdempotente { Id = Guid.CreateVersion7(), InstalacionId = installation.Id,
                TerminalId = terminal.Id, UsuarioId = actor.UserId, SesionOperadorId = session?.Id, ClaveIdempotencia = key,
                TipoOperacion = "CORREGIR_APARTADO", HashSolicitud = fingerprint, Entidad = nameof(Apartado), EntidadId = layaway.Id,
                Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlation, FechaRegistro = now.UtcDateTime }, ct);
            await AuditWriter.WriteCashAsync(audit, clock, actor.UserId, AuditAction.LayawayProductsCorrected, nameof(Apartado), layaway.Id,
                new { CorrectionId = revision.Id, revision.VersionAnterior, revision.VersionPosterior, revision.Reembolso, AuthorizationId = authorization.Id },
                correlation, authorizer.User.Id, session?.Id, terminal.Id, request.Reason, ct);
            await work.SaveChangesAsync(ct);
            if (session is not null) { session.Close(now.UtcDateTime, "CAMBIO_OPERADOR"); await work.SaveChangesAsync(ct); }
            return Result.Success(response);
        }
        catch (Exception e) when (e is CashMoneyException or OverflowException or DomainRuleViolationException)
        { return ProductFail(ApplicationError.Validation("La corrección excede los límites de cantidades, precios o existencias.")); }
    }

    private static string Snapshot(IEnumerable<DetalleApartado> lines) => JsonSerializer.Serialize(lines.Select(l => new
    { l.Id, l.ProductoId, l.CodigoProducto, l.NombreProducto, l.UnidadMedida, l.Cantidad, l.PrecioReserva, l.CostoUnitario,
        l.PorcentajeIva, l.SubtotalBruto, l.SubtotalNeto, l.BaseGravable, l.ValorIva, l.ValorDescuento, l.OrigenDescuento }));
    private static LayawayCorrectionDto MapCorrection(CorreccionApartado c) => new(c.Id, c.ApartadoId, c.VersionPosterior,
        c.TotalAnterior, c.TotalPosterior, c.SaldoAnterior, c.SaldoPosterior, c.Reembolso, c.Motivo,
        new DateTimeOffset(DateTime.SpecifyKind(c.FechaHora, DateTimeKind.Utc)), c.CorrelacionId);
    private static Result<LayawayCorrectionDto> ProductFail(ApplicationError e) => Result.Failure<LayawayCorrectionDto>(e);
}
