using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Printing;

public sealed partial class ProductLabelPrintingService(
    OfficialControlPlusDbContext db,
    IUserRepository users,
    IAuditRepository audit,
    IClock clock) : IProductLabelPrintingService
{
    private const string LabelTemplatePrefix = "LABEL_";
    private const string JobKeyPrefix = "LABELS_";
    private const int MaximumDistinctProducts = 100;
    private const int MaximumLabels = 500;

    public async Task<Result<IReadOnlyCollection<ProductLabelProfileDto>>> ListProfilesAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, cancellationToken);
        if (error is not null) return Result.Failure<IReadOnlyCollection<ProductLabelProfileDto>>(error);
        var terminal = await GetActiveTerminalAsync(cancellationToken);
        if (terminal is null)
            return Result.Failure<IReadOnlyCollection<ProductLabelProfileDto>>(
                ApplicationError.NotFound("la terminal activa"));

        var profiles = await db.PerfilImpresora.AsNoTracking()
            .Where(profile => profile.TerminalId == terminal.Id && profile.Activo &&
                (profile.TipoImpresora == "ETIQUETAS" || profile.TipoImpresora == "HIBRIDA"))
            .OrderByDescending(profile => profile.EsPredeterminada).ThenBy(profile => profile.Nombre)
            .ToArrayAsync(cancellationToken);
        var codes = profiles.Select(profile => TemplateCode(profile.Id)).ToArray();
        var templates = await db.PlantillaImpresion.AsNoTracking()
            .Where(template => codes.Contains(template.Codigo) && template.Activa &&
                template.TipoSalida == "ETIQUETA_PRODUCTO" && template.FormatoCodigo == "CODE128")
            .ToDictionaryAsync(template => template.Codigo, cancellationToken);

        return Result.Success<IReadOnlyCollection<ProductLabelProfileDto>>(profiles
            .Where(profile => templates.ContainsKey(TemplateCode(profile.Id)))
            .Select(profile => ToProfile(profile, templates[TemplateCode(profile.Id)].Nombre)).ToArray());
    }

    public async Task<Result<PagedResult<ProductLabelProductDto>>> ListProductsAsync(
        ActorContext actor, ProductLabelProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, cancellationToken);
        if (error is not null) return Result.Failure<PagedResult<ProductLabelProductDto>>(error);
        if (query.Page < 1 || query.PageSize is < 1 or > 100 ||
            query.Page > int.MaxValue / query.PageSize)
            return Result.Failure<PagedResult<ProductLabelProductDto>>(
                ApplicationError.Validation("page debe ser positivo y pageSize debe estar entre 1 y 100."));

        var products = db.Producto.AsNoTracking().Where(product => product.Activo);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            if (term.Length > 100)
                return Result.Failure<PagedResult<ProductLabelProductDto>>(
                    ApplicationError.Validation("La búsqueda no puede superar 100 caracteres."));
            var exactId = await db.ProductoCodigo.AsNoTracking().Where(code => code.Codigo == term.ToUpper())
                .Select(code => (Guid?)code.ProductoId).SingleOrDefaultAsync(cancellationToken);
            if (exactId is not null) products = products.Where(product => product.Id == exactId);
            else
            {
                var pattern = $"%{term}%";
                products = products.Where(product => EF.Functions.ILike(product.Nombre, pattern) ||
                    db.ProductoCodigo.Any(code => code.ProductoId == product.Id &&
                        EF.Functions.ILike(code.Codigo, pattern)));
            }
        }

        var count = await products.CountAsync(cancellationToken);
        var page = await products.OrderBy(product => product.Nombre).ThenBy(product => product.CodigoInterno)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(product => new ProductLabelProductDto(product.Id, product.CodigoInterno,
                product.CodigoBarras, product.Nombre, product.PrecioMinorista))
            .ToArrayAsync(cancellationToken);
        return Result.Success(new PagedResult<ProductLabelProductDto>(page,
            query.Page, query.PageSize, count));
    }

    public async Task<Result<ProductLabelJobDto>> QueueAsync(ActorContext actor,
        string? idempotencyKey, CreateProductLabelJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, cancellationToken);
        if (error is not null) return Result.Failure<ProductLabelJobDto>(error);
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 90)
            return Result.Failure<ProductLabelJobDto>(ApplicationError.Validation(
                "Idempotency-Key es obligatorio y no puede superar 90 caracteres."));
        if (request is null || request.ProfileId == Guid.Empty || request.Lines is null ||
            request.Lines.Count is < 1 or > MaximumDistinctProducts ||
            request.Lines.Any(line => line is null || line.ProductId == Guid.Empty || line.Quantity is < 1 or > MaximumLabels) ||
            request.Lines.Select(line => line.ProductId).Distinct().Count() != request.Lines.Count ||
            request.Lines.Sum(line => (long)line.Quantity) > MaximumLabels)
            return Result.Failure<ProductLabelJobDto>(ApplicationError.Validation(
                $"Selecciona de 1 a {MaximumDistinctProducts} productos, sin repetir, y hasta {MaximumLabels} etiquetas."));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var terminal = await GetActiveTerminalAsync(cancellationToken);
        if (terminal is null)
            return Result.Failure<ProductLabelJobDto>(ApplicationError.NotFound("la terminal activa"));
        var storedKey = JobKeyPrefix + key;
        var existing = await db.TrabajoImpresion.AsNoTracking()
            .Include(job => job.DocumentoEmitido).Include(job => job.PerfilImpresora)
            .SingleOrDefaultAsync(job => job.TerminalId == terminal.Id &&
                job.ClaveIdempotencia == storedKey, cancellationToken);
        if (existing is not null)
        {
            if (!MatchesRequest(existing.DocumentoEmitido, request, out var existingCount))
                return Result.Failure<ProductLabelJobDto>(ApplicationError.Conflict(
                    "La clave de idempotencia ya fue utilizada con otro lote de etiquetas."));
            return Result.Success(ToJob(existing, existingCount));
        }
        Compra? sourcePurchase = null;
        if (request.SourcePurchaseId is Guid sourceId)
        {
            var locked = await db.Compra.FromSqlInterpolated(
                $"SELECT * FROM compras.compra WHERE id = {sourceId} FOR UPDATE")
                .ToArrayAsync(cancellationToken);
            sourcePurchase = locked.SingleOrDefault();
            if (sourcePurchase is null || sourcePurchase.InstalacionId != terminal.InstalacionId ||
                sourcePurchase.Estado != "CONFIRMADA")
                return Result.Failure<ProductLabelJobDto>(ApplicationError.Conflict(
                    "La recepción debe estar confirmada y pertenecer a esta instalación."));
            // A second request may have waited for the same receipt lock; replay it before
            // checking remaining quantities, which the first request has already reserved.
            var replay = await db.TrabajoImpresion.AsNoTracking()
                .Include(job => job.DocumentoEmitido).Include(job => job.PerfilImpresora)
                .SingleOrDefaultAsync(job => job.TerminalId == terminal.Id &&
                    job.ClaveIdempotencia == storedKey, cancellationToken);
            if (replay is not null)
                return MatchesRequest(replay.DocumentoEmitido, request, out var replayCount)
                    ? Result.Success(ToJob(replay, replayCount))
                    : Result.Failure<ProductLabelJobDto>(ApplicationError.Conflict(
                        "La clave de idempotencia ya fue utilizada con otro lote de etiquetas."));
            await db.Entry(sourcePurchase).Collection(purchase => purchase.DetalleCompra).LoadAsync(cancellationToken);
            var reserved = await ReservedQuantitiesAsync(sourceId, cancellationToken);
            if (request.Lines.Any(line => line.Quantity > sourcePurchase.DetalleCompra
                .Where(detail => detail.ProductoId == line.ProductId).Sum(detail => detail.Cantidad) -
                reserved.GetValueOrDefault(line.ProductId)))
                return Result.Failure<ProductLabelJobDto>(ApplicationError.Conflict(
                    "La cantidad supera las etiquetas pendientes de esta recepción. Actualiza la lista."));
        }
        var profile = await db.PerfilImpresora.SingleOrDefaultAsync(candidate =>
            candidate.Id == request.ProfileId && candidate.TerminalId == terminal.Id && candidate.Activo &&
            (candidate.TipoImpresora == "ETIQUETAS" || candidate.TipoImpresora == "HIBRIDA"),
            cancellationToken);
        if (profile is null)
            return Result.Failure<ProductLabelJobDto>(ApplicationError.NotFound("el perfil de etiquetas activo"));
        var template = await db.PlantillaImpresion.SingleOrDefaultAsync(candidate =>
            candidate.Codigo == TemplateCode(profile.Id) && candidate.Activa &&
            candidate.TipoSalida == "ETIQUETA_PRODUCTO" && candidate.FormatoCodigo == "CODE128",
            cancellationToken);
        if (template is null)
            return Result.Failure<ProductLabelJobDto>(ApplicationError.Conflict(
                "El perfil no tiene una plantilla activa de etiquetas Code 128."));

        var productIds = request.Lines.Select(line => line.ProductId).ToArray();
        var products = await db.Producto.AsNoTracking().Where(product =>
                productIds.Contains(product.Id) && product.Activo)
            .ToDictionaryAsync(product => product.Id, cancellationToken);
        if (products.Count != productIds.Length)
            return Result.Failure<ProductLabelJobDto>(ApplicationError.Validation(
                "Todos los productos deben existir y estar activos."));

        var items = new List<ProductLabelPrintItem>(request.Lines.Count);
        foreach (var line in request.Lines)
        {
            var product = products[line.ProductId];
            if (!Code128Encoder.CanEncode(product.CodigoBarras))
                return Result.Failure<ProductLabelJobDto>(ApplicationError.Validation(
                    $"{product.Nombre} no tiene un código de barras imprimible en Code 128."));
            items.Add(new ProductLabelPrintItem(product.Id, product.Nombre, product.PrecioMinorista,
                product.CodigoBarras!, line.Quantity));
        }

        var establishmentName = await db.Instalacion.AsNoTracking()
            .Where(candidate => candidate.Id == terminal.InstalacionId)
            .Select(candidate => candidate.Establecimiento.NombreComercial).SingleAsync(cancellationToken);
        var content = ProductLabelPayloadCodec.Encode(items, establishmentName);
        var snapshot = JsonSerializer.Serialize(new
        {
            schemaVersion = ProductLabelPayloadCodec.CurrentSchemaVersion,
            profileId = profile.Id,
            sourcePurchaseId = request.SourcePurchaseId,
            establishmentName,
            items = items.Select(item => new
            {
                productId = item.ProductId, name = item.Name, retailPrice = item.RetailPrice,
                barcode = item.Barcode, quantity = item.Quantity
            })
        });
        var snapshotHash = SnapshotHash(snapshot);
        var installation = await db.Instalacion.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == terminal.InstalacionId, cancellationToken);
        var now = clock.UtcNow;
        var batchId = Guid.CreateVersion7();
        var document = new DocumentoEmitido
        {
            Id = Guid.CreateVersion7(), InstalacionId = installation.Id, TerminalId = terminal.Id,
            EmitidoPorId = actor.UserId, TipoDocumento = "ETIQUETA_PRODUCTO",
            EntidadTipo = sourcePurchase is null ? "LOTE_ETIQUETAS_PRODUCTO" : ReceiptLabelEntity,
            EntidadId = request.SourcePurchaseId ?? batchId,
            VersionDocumento = sourcePurchase is null ? 1 : 1 +
                (await db.DocumentoEmitido.Where(candidate => candidate.InstalacionId == installation.Id &&
                    candidate.TipoDocumento == "ETIQUETA_PRODUCTO" && candidate.EntidadId == sourcePurchase.Id)
                    .Select(candidate => (int?)candidate.VersionDocumento).MaxAsync(cancellationToken) ?? 0),
            DatosSnapshot = snapshot,
            PlantillaSnapshot = JsonSerializer.Serialize(new
            {
                template.Id, template.Codigo, template.FormatoCodigo,
                template.AnchoMm, template.AltoMm, profile.ResolucionDpi
            }),
            HashSnapshot = snapshotHash, FechaEmision = now.UtcDateTime
        };
        var job = new TrabajoImpresion
        {
            Id = Guid.CreateVersion7(), DocumentoEmitidoId = document.Id,
            PerfilImpresoraId = profile.Id, PlantillaImpresionId = template.Id,
            TerminalId = terminal.Id, SolicitadoPorId = actor.UserId,
            ClaveIdempotencia = storedKey, TipoCopia = "ORIGINAL", NumeroReimpresion = 0,
            Estado = "PENDIENTE", Intentos = 0, ContenidoRenderizado = content,
            HashContenido = Convert.ToHexString(SHA256.HashData(content)),
            FechaSolicitud = now.UtcDateTime, DocumentoEmitido = document,
            PerfilImpresora = profile, PlantillaImpresion = template
        };
        db.DocumentoEmitido.Add(document);
        db.TrabajoImpresion.Add(job);
        await audit.AddAsync(AuditRecord.Create(actor.UserId, AuditAction.PrintJobQueued,
            nameof(TrabajoImpresion), job.Id, JsonSerializer.Serialize(new
            {
                job.Id, BatchId = batchId, ProfileId = profile.Id,
                DistinctProducts = items.Count, LabelCount = items.Sum(item => item.Quantity),
                DocumentType = "ETIQUETA_PRODUCTO"
            }), now), cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(ToJob(job, items.Sum(item => item.Quantity)));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var raced = await db.TrabajoImpresion.AsNoTracking()
                .Include(candidate => candidate.DocumentoEmitido)
                .Include(candidate => candidate.PerfilImpresora)
                .SingleOrDefaultAsync(candidate => candidate.TerminalId == terminal.Id &&
                    candidate.ClaveIdempotencia == storedKey, cancellationToken);
            if (raced is not null)
            {
                if (MatchesRequest(raced.DocumentoEmitido, request, out var racedCount))
                    return Result.Success(ToJob(raced, racedCount));
                return Result.Failure<ProductLabelJobDto>(ApplicationError.Conflict(
                    "La clave de idempotencia ya fue utilizada con otro lote de etiquetas."));
            }
            throw;
        }
    }

    private async Task<ApplicationError?> RequirePermissionAsync(Guid userId,
        CancellationToken cancellationToken)
    {
        var authorization = await users.GetAuthorizationSnapshotAsync(userId, cancellationToken);
        if (authorization is null) return ApplicationError.Unauthorized();
        return authorization.PermissionCodes.Contains(PermissionCodes.ProductLabelsManage,
            StringComparer.OrdinalIgnoreCase) ? null : ApplicationError.Forbidden();
    }

    private async Task<Terminal?> GetActiveTerminalAsync(CancellationToken cancellationToken)
    {
        var installation = await db.Instalacion.AsNoTracking().Where(candidate => candidate.Activo)
            .OrderBy(candidate => candidate.FechaInstalacion).ThenBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return installation is null ? null : await db.Terminal.AsNoTracking()
            .Where(candidate => candidate.InstalacionId == installation.Id && candidate.Activo)
            .OrderBy(candidate => candidate.FechaCreacion).ThenBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string TemplateCode(Guid profileId) =>
        LabelTemplatePrefix + profileId.ToString("N").ToUpperInvariant();

    private static bool MatchesRequest(DocumentoEmitido document,
        CreateProductLabelJobRequest request, out int labelCount)
    {
        labelCount = 0;
        try
        {
            using var snapshot = JsonDocument.Parse(document.DatosSnapshot);
            var root = snapshot.RootElement;
            var sourcePurchaseId = root.TryGetProperty("sourcePurchaseId", out var source) &&
                source.ValueKind != JsonValueKind.Null ? source.GetGuid() : (Guid?)null;
            if (sourcePurchaseId != request.SourcePurchaseId) return false;
            if (!string.Equals(SnapshotHash(document.DatosSnapshot), document.HashSnapshot,
                    StringComparison.Ordinal) ||
                !root.TryGetProperty("profileId", out var profileValue) ||
                profileValue.GetGuid() != request.ProfileId ||
                !root.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != request.Lines.Count)
                return false;
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                var line = request.Lines.ElementAt(index++);
                if (item.GetProperty("productId").GetGuid() != line.ProductId ||
                    item.GetProperty("quantity").GetInt32() != line.Quantity)
                    return false;
                labelCount += line.Quantity;
            }
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            FormatException or OverflowException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static string SnapshotHash(string json)
    {
        using var document = JsonDocument.Parse(json);
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output)) WriteCanonical(document.RootElement, writer);
        return Convert.ToHexString(SHA256.HashData(output.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteCanonical(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetDecimal().ToString("G29", CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("Tipo JSON no admitido en el snapshot de etiquetas.");
        }
    }

    private static ProductLabelProfileDto ToProfile(PerfilImpresora profile, string templateName) =>
        new(profile.Id, profile.Nombre, profile.TipoImpresora, profile.AnchoMm,
            profile.AltoMm, profile.ResolucionDpi, profile.EsPredeterminada, templateName);

    private static ProductLabelJobDto ToJob(TrabajoImpresion job, int labelCount) =>
        new(job.Id, job.DocumentoEmitido.EntidadTipo == ReceiptLabelEntity
                ? job.DocumentoEmitido.Id : job.DocumentoEmitido.EntidadId, job.PerfilImpresoraId,
            job.PerfilImpresora.Nombre, labelCount,
            job.Estado == "FALLIDO" ? "ERROR" : job.Estado,
            new DateTimeOffset(job.FechaSolicitud, TimeSpan.Zero));
}
