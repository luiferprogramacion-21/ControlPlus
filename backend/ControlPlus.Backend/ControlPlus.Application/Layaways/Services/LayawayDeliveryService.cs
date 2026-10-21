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
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Layaways.Services;

public sealed class LayawayDeliveryService(ILayawayDeliveryRepository layaways,
    ISalesRepository sales, ISalesTransactionManager transactions, IUserRepository users,
    IAuditRepository audits, IUnitOfWork unitOfWork, IClock clock) : ILayawayDeliveryService
{
    public async Task<Result<LayawayDeliveryDto>> DeliverAsync(ActorContext actor, Guid layawayId,
        string idempotencyKey, DeliverLayawayRequest request,
        CancellationToken cancellationToken = default)
    {
        if (layawayId == Guid.Empty || string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Trim().Length > 100 || request is null || request.ExpectedVersion <= 0)
            return Fail(ApplicationError.Validation("Idempotency-Key y una versión positiva son obligatorios."));
        var key = idempotencyKey.Trim();
        var canonical = JsonSerializer.Serialize(new { layawayId, request.ExpectedVersion });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        await using var transaction = await transactions.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // Payments take installation, shift/session, sequence and header locks in that order.
        // Delivery shares their installation -> header order and never takes a cash lock.
        var installation = await sales.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Fail(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Fail(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.LayawayManage, StringComparer.OrdinalIgnoreCase))
            return Fail(ApplicationError.Forbidden());

        var previous = await sales.GetIdempotencyAsync(installation.Id, key, cancellationToken);
        if (previous is not null)
        {
            if (previous.UsuarioId != actor.UserId || previous.TipoOperacion != "ENTREGAR_APARTADO" ||
                previous.HashSolicitud != hash || string.IsNullOrEmpty(previous.Resultado))
                return Fail(ApplicationError.Conflict("La clave de idempotencia ya fue utilizada con otra solicitud."));
            var replay = JsonSerializer.Deserialize<LayawayDeliveryDto>(previous.Resultado);
            return replay is null
                ? Fail(ApplicationError.Conflict("La operación idempotente no tiene resultado recuperable."))
                : Result.Success(replay);
        }

        // The official idempotency record requires a terminal FK, independently of cash.
        var terminal = await sales.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null) return Fail(ApplicationError.NotFound("la terminal activa"));
        var layaway = await layaways.GetForUpdateAsync(layawayId, cancellationToken);
        if (layaway is null || layaway.InstalacionId != installation.Id)
            return Fail(ApplicationError.NotFound("el apartado"));
        if (layaway.Version != request.ExpectedVersion)
            return Fail(ApplicationError.ConcurrencyConflict("El apartado fue modificado. Recárguelo."));
        if (layaway.Estado != "PAGADO" || layaway.SaldoPendiente != 0)
            return Fail(ApplicationError.Conflict("Solo se puede entregar un apartado PAGADO con saldo cero."));

        var lines = await layaways.GetLinesAsync(layaway.Id, cancellationToken);
        if (lines.Count == 0 || lines.Any(x => x.ProductoId == Guid.Empty || x.Cantidad <= 0))
            return Fail(ApplicationError.Conflict("Las cantidades del apartado no permiten la entrega."));
        var grouped = lines.GroupBy(x => x.ProductoId).OrderBy(x => x.Key).ToArray();
        var quantities = grouped.ToDictionary(x => x.Key, x => x.Sum(line => (long)line.Cantidad));
        if (quantities.Values.Any(x => x > int.MaxValue))
            return Fail(ApplicationError.Conflict("Las cantidades del apartado exceden el rango permitido."));
        var products = await sales.GetProductsForUpdateAsync(quantities.Keys.ToArray(), cancellationToken);
        if (products.Count != quantities.Count || products.Any(product =>
                !quantities.TryGetValue(product.Id, out var quantity) ||
                product.StockActual < quantity || product.StockReservado < quantity ||
                product.StockReservado > product.StockActual))
            return Fail(ApplicationError.Conflict("El stock o la reserva no permiten entregar el apartado."));

        // PostgreSQL timestamps have microsecond precision; persist and replay the same instant.
        var timestamp = clock.UtcNow;
        var now = new DateTimeOffset(timestamp.UtcTicks - timestamp.UtcTicks % 10, TimeSpan.Zero);
        var correlationId = Guid.CreateVersion7();
        var movements = new List<MovimientoInventario>();
        foreach (var product in products.OrderBy(x => x.Id))
        {
            var quantity = (int)quantities[product.Id];
            var stockBefore = product.StockActual;
            var reservedBefore = product.StockReservado;
            product.StockActual -= quantity;
            product.StockReservado -= quantity;
            movements.Add(new MovimientoInventario
            {
                Id = Guid.CreateVersion7(), ProductoId = product.Id, UsuarioId = actor.UserId,
                ApartadoId = layaway.Id, TipoMovimiento = "ENTREGA_APARTADO",
                CantidadStock = -quantity, CantidadReservada = -quantity,
                StockAnterior = stockBefore, StockPosterior = product.StockActual,
                ReservadoAnterior = reservedBefore, ReservadoPosterior = product.StockReservado,
                CostoPromedioAnterior = product.CostoPromedio, CostoPromedioPosterior = product.CostoPromedio,
                FechaHora = now.UtcDateTime, CorrelacionId = correlationId
            });
        }
        layaway.Estado = "ENTREGADO";
        layaway.FechaEntrega = now.UtcDateTime;
        layaway.UsuarioEntregaId = actor.UserId;
        // No sale, payment, operator session or cash movement is created by delivery.
        var deliveredProducts = grouped.Select(group =>
        {
            var line = group.OrderBy(x => x.Id).First();
            return new LayawayDeliveredProductDto(line.ProductoId, line.CodigoProducto,
                line.NombreProducto, line.UnidadMedida, (int)quantities[line.ProductoId]);
        }).ToArray();
        var response = new LayawayDeliveryDto(layaway.Id,
            $"{layaway.Prefijo}-{layaway.Serie}-{layaway.Consecutivo}", layaway.Estado,
            layaway.Version + 1, now, actor.UserId, correlationId, deliveredProducts);
        await sales.AddInventoryMovementsAsync(movements, cancellationToken);
        await sales.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, ClaveIdempotencia = key, TipoOperacion = "ENTREGAR_APARTADO",
            HashSolicitud = hash, Entidad = nameof(Apartado), EntidadId = layaway.Id,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlationId,
            FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId,
            AuditAction.LayawayDelivered, nameof(Apartado), layaway.Id,
            new { layaway.Id, layaway.Estado, Products = deliveredProducts,
                InventoryMovementIds = movements.Select(x => x.Id).ToArray() },
            correlationId, null, null, terminal.Id, null, cancellationToken);
        // OfficialControlPlusDbContext increments header/product versions centrally.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(response);
    }

    private static Result<LayawayDeliveryDto> Fail(ApplicationError error) =>
        Result.Failure<LayawayDeliveryDto>(error);
}
