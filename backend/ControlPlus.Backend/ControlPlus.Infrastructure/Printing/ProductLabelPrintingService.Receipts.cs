using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Printing;

public sealed partial class ProductLabelPrintingService
{
    private const string ReceiptLabelEntity = "RECEPCION_ETIQUETAS";

    public async Task<Result<PagedResult<ProductLabelReceiptDto>>> ListReceiptsAsync(
        ActorContext actor, PrintJobsQuery query, CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, cancellationToken);
        if (error is not null) return Result.Failure<PagedResult<ProductLabelReceiptDto>>(error);
        if (!ValidPage(query)) return Result.Failure<PagedResult<ProductLabelReceiptDto>>(
            ApplicationError.Validation("page debe ser positivo y pageSize debe estar entre 1 y 100."));
        var terminal = await GetActiveTerminalAsync(cancellationToken);
        if (terminal is null) return Result.Failure<PagedResult<ProductLabelReceiptDto>>(
            ApplicationError.NotFound("la terminal activa"));

        // Quantities already assigned to a batch stay reserved even on a known printer failure:
        // the same job must be retried rather than preparing a duplicate batch for the receipt.
        var pending = db.Database.SqlQuery<PendingReceiptRow>($"""
            SELECT c.id AS "Id", c.fecha_hora_confirmacion AS "ReceivedAt"
            FROM compras.compra c
            WHERE c.instalacion_id = {terminal.InstalacionId} AND c.estado = 'CONFIRMADA'
              AND EXISTS (
                SELECT 1 FROM compras.detalle_compra dc WHERE dc.compra_id = c.id
                AND dc.cantidad > COALESCE((
                  SELECT SUM((item->>'quantity')::integer)
                  FROM configuracion.documento_emitido d
                  JOIN configuracion.trabajo_impresion j ON j.documento_emitido_id = d.id
                  CROSS JOIN LATERAL jsonb_array_elements(d.datos_snapshot->'items') item
                  WHERE d.instalacion_id = c.instalacion_id
                    AND d.entidad_tipo = 'RECEPCION_ETIQUETAS' AND d.entidad_id = c.id
                    AND d.tipo_documento = 'ETIQUETA_PRODUCTO' AND j.estado <> 'CANCELADO'
                    AND item->>'productId' = dc.producto_id::text
                ), 0))
            """);
        var count = await pending.CountAsync(cancellationToken);
        var ids = await pending.OrderByDescending(row => row.ReceivedAt).ThenBy(row => row.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(row => row.Id).ToArrayAsync(cancellationToken);
        var receipts = await ReceiptSource().Where(purchase => ids.Contains(purchase.Id))
            .ToArrayAsync(cancellationToken);
        var items = new List<ProductLabelReceiptDto>(receipts.Length);
        foreach (var id in ids)
        {
            var purchase = receipts.Single(candidate => candidate.Id == id);
            items.Add(ToReceipt(purchase, await ReservedQuantitiesAsync(id, cancellationToken)));
        }
        return Result.Success(new PagedResult<ProductLabelReceiptDto>(items, query.Page, query.PageSize, count));
    }

    public async Task<Result<ProductLabelReceiptDto>> GetReceiptAsync(ActorContext actor,
        Guid purchaseId, CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, cancellationToken);
        if (error is not null) return Result.Failure<ProductLabelReceiptDto>(error);
        var terminal = await GetActiveTerminalAsync(cancellationToken);
        var purchase = terminal is null ? null : await ReceiptSource().SingleOrDefaultAsync(
            candidate => candidate.Id == purchaseId && candidate.InstalacionId == terminal.InstalacionId &&
                candidate.Estado == "CONFIRMADA", cancellationToken);
        if (purchase is null) return Result.Failure<ProductLabelReceiptDto>(
            ApplicationError.NotFound("la recepción confirmada"));
        return Result.Success(ToReceipt(purchase, await ReservedQuantitiesAsync(purchase.Id, cancellationToken)));
    }

    public async Task<Result<PagedResult<ProductLabelQueueItemDto>>> ListJobsAsync(
        ActorContext actor, PrintJobsQuery query, CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, cancellationToken);
        if (error is not null) return Result.Failure<PagedResult<ProductLabelQueueItemDto>>(error);
        if (!ValidPage(query)) return Result.Failure<PagedResult<ProductLabelQueueItemDto>>(
            ApplicationError.Validation("page debe ser positivo y pageSize debe estar entre 1 y 100."));
        var terminal = await GetActiveTerminalAsync(cancellationToken);
        if (terminal is null) return Result.Failure<PagedResult<ProductLabelQueueItemDto>>(
            ApplicationError.NotFound("la terminal activa"));
        var source = db.TrabajoImpresion.AsNoTracking().Where(job => job.TerminalId == terminal.Id &&
            job.DocumentoEmitido.TipoDocumento == "ETIQUETA_PRODUCTO");
        var count = await source.CountAsync(cancellationToken);
        var jobs = await source.Include(job => job.DocumentoEmitido).Include(job => job.PerfilImpresora)
            .OrderByDescending(job => job.FechaSolicitud).ThenBy(job => job.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return Result.Success(new PagedResult<ProductLabelQueueItemDto>(jobs.Select(ToQueueItem).ToArray(),
            query.Page, query.PageSize, count));
    }

    public async Task<Result<ProductLabelQueueItemDto>> RetryAsync(ActorContext actor, Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, cancellationToken);
        if (error is not null) return Result.Failure<ProductLabelQueueItemDto>(error);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.TrabajoImpresion.FromSqlInterpolated(
            $"SELECT * FROM configuracion.trabajo_impresion WHERE id = {jobId} FOR UPDATE")
            .AsTracking().ToArrayAsync(cancellationToken);
        var job = rows.SingleOrDefault();
        var terminal = await GetActiveTerminalAsync(cancellationToken);
        if (job is null || terminal is null || job.TerminalId != terminal.Id)
            return Result.Failure<ProductLabelQueueItemDto>(ApplicationError.NotFound("el trabajo de etiquetas"));
        await db.Entry(job).Reference(candidate => candidate.DocumentoEmitido).LoadAsync(cancellationToken);
        await db.Entry(job).Reference(candidate => candidate.PerfilImpresora).LoadAsync(cancellationToken);
        if (job.DocumentoEmitido.TipoDocumento != "ETIQUETA_PRODUCTO")
            return Result.Failure<ProductLabelQueueItemDto>(ApplicationError.NotFound("el trabajo de etiquetas"));
        if (job.Estado == "PENDIENTE") return Result.Success(ToQueueItem(job));
        if (job.Estado != "FALLIDO") return Result.Failure<ProductLabelQueueItemDto>(ApplicationError.Conflict(
            "Solo se reintentan trabajos fallidos; comprueba los resultados inciertos antes de preparar otra copia."));
        job.Estado = "PENDIENTE";
        job.ErrorSanitizado = null;
        await audit.AddAsync(AuditRecord.Create(actor.UserId, AuditAction.PrintJobRetried,
            nameof(TrabajoImpresion), job.Id, JsonSerializer.Serialize(new { job.Id,
                job.DocumentoEmitidoId, DocumentType = "ETIQUETA_PRODUCTO" }), clock.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(ToQueueItem(job));
    }

    private IQueryable<Compra> ReceiptSource() => db.Compra.AsNoTracking()
        .Include(purchase => purchase.Proveedor)
        .Include(purchase => purchase.DetalleCompra).ThenInclude(detail => detail.Producto);

    private async Task<Dictionary<Guid, int>> ReservedQuantitiesAsync(Guid purchaseId,
        CancellationToken cancellationToken)
    {
        var documents = await db.DocumentoEmitido.AsNoTracking()
            .Where(document => document.EntidadTipo == ReceiptLabelEntity && document.EntidadId == purchaseId &&
                document.TipoDocumento == "ETIQUETA_PRODUCTO" &&
                document.TrabajoImpresion.Any(job => job.Estado != "CANCELADO"))
            .ToArrayAsync(cancellationToken);
        var quantities = new Dictionary<Guid, int>();
        foreach (var document in documents)
        {
            if (!string.Equals(SnapshotHash(document.DatosSnapshot), document.HashSnapshot, StringComparison.Ordinal))
                throw new InvalidOperationException("El historial de etiquetas no pasó la verificación de integridad.");
            using var snapshot = JsonDocument.Parse(document.DatosSnapshot);
            foreach (var item in snapshot.RootElement.GetProperty("items").EnumerateArray())
            {
                var id = item.GetProperty("productId").GetGuid();
                quantities[id] = checked(quantities.GetValueOrDefault(id) + item.GetProperty("quantity").GetInt32());
            }
        }
        return quantities;
    }

    private static ProductLabelReceiptDto ToReceipt(Compra purchase, Dictionary<Guid, int> reserved)
    {
        var lines = purchase.DetalleCompra.OrderBy(detail => detail.Producto.Nombre)
            .Select(detail => new ProductLabelReceiptLineDto(new ProductLabelProductDto(detail.ProductoId,
                detail.Producto.CodigoInterno, detail.Producto.CodigoBarras,
                detail.Producto.Nombre, detail.Producto.PrecioMinorista), detail.Cantidad,
                Math.Max(0, detail.Cantidad - reserved.GetValueOrDefault(detail.ProductoId)))).ToArray();
        return new ProductLabelReceiptDto(purchase.Id,
            $"{purchase.Prefijo}-{purchase.Serie}-{purchase.Consecutivo}", purchase.Proveedor.NombreRazonSocial,
            new DateTimeOffset(purchase.FechaHoraConfirmacion ?? purchase.FechaHoraRegistro, TimeSpan.Zero),
            lines.Sum(line => line.PendingQuantity), lines);
    }

    private static ProductLabelQueueItemDto ToQueueItem(TrabajoImpresion job)
    {
        using var snapshot = JsonDocument.Parse(job.DocumentoEmitido.DatosSnapshot);
        var root = snapshot.RootElement;
        var count = root.GetProperty("items").EnumerateArray().Sum(item => item.GetProperty("quantity").GetInt32());
        var dto = ToJob(job, count);
        return new ProductLabelQueueItemDto(dto.JobId, dto.BatchId, dto.ProfileId, dto.ProfileName,
            dto.LabelCount, dto.Status, dto.RequestedAtUtc,
            job.DocumentoEmitido.EntidadTipo == ReceiptLabelEntity ? job.DocumentoEmitido.EntidadId : null,
            job.ErrorSanitizado);
    }

    private static bool ValidPage(PrintJobsQuery query) => query.Page > 0 && query.PageSize is >= 1 and <= 100 &&
        query.Page <= int.MaxValue / query.PageSize;

    private sealed class PendingReceiptRow
    {
        public Guid Id { get; init; }
        public DateTime ReceivedAt { get; init; }
    }
}
