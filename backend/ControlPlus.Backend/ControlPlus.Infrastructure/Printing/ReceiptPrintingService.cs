using System.Security.Cryptography;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Sales.Services;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Printing;

public sealed class ReceiptPrintingService(
    OfficialControlPlusDbContext db,
    IUserRepository users,
    IAuditRepository audit,
    IClock clock,
    IPrintSpooler spooler) : IReceiptPrintingService
{
    private const string ReceiptTemplatePrefix = "RECEIPT_";

    public async Task<Result<PrintProfileDto>> CreateProfileAsync(ActorContext actor,
        CreatePrintProfileRequest request, CancellationToken ct = default)
    {
        var auth = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        if (auth is null) return Result.Failure<PrintProfileDto>(ApplicationError.Unauthorized());
        if (auth.HighestRoleLevel != RoleLevel.Administrador ||
            !auth.PermissionCodes.Contains(PermissionCodes.ConfigurationManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<PrintProfileDto>(ApplicationError.Forbidden());
        if (request is null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150 ||
            string.IsNullOrWhiteSpace(request.SystemQueueName) || request.SystemQueueName.Trim().Length > 300 ||
            request.Name.Any(char.IsControl) || request.SystemQueueName.Any(char.IsControl) ||
            request.Columns is < 24 or > 80)
            return Result.Failure<PrintProfileDto>(ApplicationError.Validation(
                "Nombre, cola de Windows y columnas (24 a 80) son obligatorios."));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var terminal = await GetLockedActiveTerminalAsync(ct);
        if (terminal is null) return Result.Failure<PrintProfileDto>(ApplicationError.NotFound("la terminal activa"));
        if (await db.PerfilImpresora.AnyAsync(x => x.TerminalId == terminal.Id && x.Nombre == request.Name.Trim(), ct))
            return Result.Failure<PrintProfileDto>(ApplicationError.Conflict("Ya existe un perfil con ese nombre en la terminal."));

        if (request.MakeDefault)
        {
            var oldDefaults = await db.PerfilImpresora.Where(x => x.TerminalId == terminal.Id && x.EsPredeterminada).ToListAsync(ct);
            foreach (var existing in oldDefaults)
            {
                existing.EsPredeterminada = false;
                existing.FechaModificacion = clock.UtcNow.UtcDateTime;
            }
        }

        var profile = new PerfilImpresora
        {
            Id = Guid.CreateVersion7(), TerminalId = terminal.Id, Nombre = request.Name.Trim(),
            TipoImpresora = "RECIBOS", NombreSistema = request.SystemQueueName.Trim(),
            EsPredeterminada = request.MakeDefault, Activo = true, Version = 1,
            FechaCreacion = clock.UtcNow.UtcDateTime
        };
        var installation = await db.Instalacion.AsNoTracking().SingleAsync(x => x.Id == terminal.InstalacionId, ct);
        var template = new PlantillaImpresion
        {
            Id = Guid.CreateVersion7(), EstablecimientoId = installation.EstablecimientoId,
            Codigo = TemplateCode(profile.Id),
            Nombre = ("Comprobante " + profile.Nombre)[..Math.Min(150, 12 + profile.Nombre.Length)],
            TipoSalida = "COMPROBANTE", Configuracion = JsonSerializer.Serialize(new { columns = request.Columns }),
            Activa = true, Version = 1, FechaCreacion = clock.UtcNow.UtcDateTime
        };
        db.PerfilImpresora.Add(profile);
        db.PlantillaImpresion.Add(template);
        await WriteAuditAsync(actor.UserId, AuditAction.PrintProfileCreated, nameof(PerfilImpresora),
            profile.Id, new { profile.Id, profile.TerminalId, profile.Nombre, profile.NombreSistema,
                request.Columns, profile.EsPredeterminada }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Success(ToProfile(profile, request.Columns));
    }

    public async Task<Result<IReadOnlyCollection<PrintProfileDto>>> ListProfilesAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, PermissionCodes.ConfigurationManage, ct);
        if (error is not null) return Result.Failure<IReadOnlyCollection<PrintProfileDto>>(error);
        var terminal = await GetActiveTerminalAsync(ct);
        if (terminal is null) return Result.Failure<IReadOnlyCollection<PrintProfileDto>>(
            ApplicationError.NotFound("la terminal activa"));
        var profiles = await db.PerfilImpresora.AsNoTracking().Where(x => x.TerminalId == terminal.Id)
            .OrderBy(x => x.Nombre).ToArrayAsync(ct);
        var codes = profiles.Select(x => TemplateCode(x.Id)).ToArray();
        var templates = await db.PlantillaImpresion.AsNoTracking().Where(x => codes.Contains(x.Codigo))
            .ToDictionaryAsync(x => x.Codigo, ct);
        return Result.Success<IReadOnlyCollection<PrintProfileDto>>(profiles.Select(p => ToProfile(p,
            templates.TryGetValue(TemplateCode(p.Id), out var t) ? ReadColumns(t.Configuracion) : 0)).ToArray());
    }

    public async Task<Result<PrintStatusDto>> GetStatusAsync(ActorContext actor, CancellationToken ct = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, PermissionCodes.SalesCreate, ct);
        if (error is not null) return Result.Failure<PrintStatusDto>(error);
        var terminal = await GetActiveTerminalAsync(ct);
        if (terminal is null) return Result.Success(new PrintStatusDto(false, null, null));
        var profile = await db.PerfilImpresora.AsNoTracking()
            .Where(x => x.TerminalId == terminal.Id && x.Activo && x.EsPredeterminada &&
                (x.TipoImpresora == "RECIBOS" || x.TipoImpresora == "HIBRIDA"))
            .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (profile is null) return Result.Success(new PrintStatusDto(false, null, null));
        var template = await db.PlantillaImpresion.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Codigo == TemplateCode(profile.Id) && x.Activa, ct);
        var columns = template is null ? 0 : ReadColumns(template.Configuracion);
        return Result.Success(columns is >= 24 and <= 80
            ? new PrintStatusDto(true, profile.Nombre, columns)
            : new PrintStatusDto(false, null, null));
    }

    public async Task<Result<PagedResult<PrintJobDto>>> ListJobsAsync(ActorContext actor,
        PrintJobsQuery query, CancellationToken ct = default)
    {
        var pageError = ValidatePage(query);
        if (pageError is not null) return Result.Failure<PagedResult<PrintJobDto>>(pageError);
        var error = await RequirePermissionAsync(actor.UserId, PermissionCodes.ConfigurationManage, ct);
        if (error is not null) return Result.Failure<PagedResult<PrintJobDto>>(error);
        var terminal = await GetActiveTerminalAsync(ct);
        if (terminal is null) return Result.Failure<PagedResult<PrintJobDto>>(
            ApplicationError.NotFound("la terminal activa"));
        var source = db.TrabajoImpresion.AsNoTracking().Where(x => x.TerminalId == terminal.Id);
        var count = await source.CountAsync(ct);
        var jobs = await source.Include(x => x.DocumentoEmitido)
            .OrderByDescending(x => x.FechaSolicitud).ThenByDescending(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return Result.Success(new PagedResult<PrintJobDto>(jobs.Select(ToJob).ToArray(),
            query.Page, query.PageSize, count));
    }

    public async Task<Result<PagedResult<PrintJobDto>>> ListSaleJobsAsync(ActorContext actor,
        Guid saleId, PrintJobsQuery query, CancellationToken ct = default)
    {
        var pageError = ValidatePage(query);
        if (pageError is not null) return Result.Failure<PagedResult<PrintJobDto>>(pageError);
        var auth = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        if (auth is null) return Result.Failure<PagedResult<PrintJobDto>>(ApplicationError.Unauthorized());
        var sale = await db.Venta.AsNoTracking().SingleOrDefaultAsync(x => x.Id == saleId, ct);
        if (sale is null) return Result.Failure<PagedResult<PrintJobDto>>(ApplicationError.NotFound("la venta"));
        var canReadAll = auth.PermissionCodes.Contains(PermissionCodes.SalesAllRead, StringComparer.OrdinalIgnoreCase);
        var canReadOwn = auth.PermissionCodes.Contains(PermissionCodes.SalesOwnRead, StringComparer.OrdinalIgnoreCase);
        if (!canReadAll && !(canReadOwn && sale.UsuarioId == actor.UserId))
            return Result.Failure<PagedResult<PrintJobDto>>(ApplicationError.Forbidden());
        var source = db.TrabajoImpresion.AsNoTracking()
            .Where(x => x.DocumentoEmitido.EntidadId == saleId &&
                x.DocumentoEmitido.InstalacionId == sale.InstalacionId &&
                x.DocumentoEmitido.TipoDocumento == "COMPROBANTE_VENTA");
        var count = await source.CountAsync(ct);
        var jobs = await source.Include(x => x.DocumentoEmitido)
            .OrderBy(x => x.FechaSolicitud).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
        return Result.Success(new PagedResult<PrintJobDto>(jobs.Select(ToJob).ToArray(),
            query.Page, query.PageSize, count));
    }

    public async Task<Result<PrintJobDto>> RetryAsync(ActorContext actor, Guid jobId,
        CancellationToken ct = default)
    {
        var error = await RequirePermissionAsync(actor.UserId, PermissionCodes.ConfigurationManage, ct);
        if (error is not null) return Result.Failure<PrintJobDto>(error);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.TrabajoImpresion.FromSqlInterpolated(
            $"SELECT * FROM configuracion.trabajo_impresion WHERE id = {jobId} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct);
        var job = rows.SingleOrDefault();
        if (job is null) return Result.Failure<PrintJobDto>(ApplicationError.NotFound("el trabajo de impresión"));
        var terminal = await GetActiveTerminalAsync(ct);
        if (terminal is null || terminal.Id != job.TerminalId)
            return Result.Failure<PrintJobDto>(ApplicationError.NotFound("el trabajo de impresión"));
        await db.Entry(job).Reference(x => x.DocumentoEmitido).LoadAsync(ct);
        if (job.Estado != "FALLIDO")
            return Result.Failure<PrintJobDto>(ApplicationError.Conflict("Solo se reintentan trabajos fallidos; un resultado incierto requiere revisión."));
        job.Estado = "PENDIENTE";
        job.ErrorSanitizado = null;
        await WriteAuditAsync(actor.UserId, AuditAction.PrintJobRetried, nameof(TrabajoImpresion), job.Id,
            new { job.Id, job.DocumentoEmitidoId }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Success(ToJob(job));
    }

    public async Task<Result<PrintJobDto>> RetrySaleJobAsync(ActorContext actor, Guid saleId,
        Guid jobId, CancellationToken ct = default)
    {
        var auth = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        if (auth is null) return Result.Failure<PrintJobDto>(ApplicationError.Unauthorized());
        if (!auth.PermissionCodes.Contains(PermissionCodes.SalesReprint, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<PrintJobDto>(ApplicationError.Forbidden());
        var sale = await db.Venta.AsNoTracking().SingleOrDefaultAsync(x => x.Id == saleId, ct);
        if (sale is null) return Result.Failure<PrintJobDto>(ApplicationError.NotFound("la venta"));
        var canReadAll = auth.PermissionCodes.Contains(PermissionCodes.SalesAllRead, StringComparer.OrdinalIgnoreCase);
        var canReadOwn = auth.PermissionCodes.Contains(PermissionCodes.SalesOwnRead, StringComparer.OrdinalIgnoreCase);
        if (!canReadAll && !(canReadOwn && sale.UsuarioId == actor.UserId))
            return Result.Failure<PrintJobDto>(ApplicationError.Forbidden());
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = (await db.TrabajoImpresion.FromSqlInterpolated(
            $"SELECT * FROM configuracion.trabajo_impresion WHERE id = {jobId} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).SingleOrDefault();
        if (job is null) return Result.Failure<PrintJobDto>(ApplicationError.NotFound("el trabajo de impresión"));
        await db.Entry(job).Reference(x => x.DocumentoEmitido).LoadAsync(ct);
        if (job.DocumentoEmitido.EntidadId != saleId || job.DocumentoEmitido.InstalacionId != sale.InstalacionId ||
            job.DocumentoEmitido.TipoDocumento != "COMPROBANTE_VENTA")
            return Result.Failure<PrintJobDto>(ApplicationError.NotFound("el trabajo de impresión"));
        if (job.Estado is "PENDIENTE" or "IMPRESO") return Result.Success(ToJob(job));
        if (job.Estado != "FALLIDO")
            return Result.Failure<PrintJobDto>(ApplicationError.Conflict("El resultado de impresión es incierto; revise la cola antes de reintentar."));
        job.Estado = "PENDIENTE";
        job.ErrorSanitizado = null;
        await WriteAuditAsync(actor.UserId, AuditAction.PrintJobRetried, nameof(TrabajoImpresion), job.Id,
            new { job.Id, job.DocumentoEmitidoId }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Success(ToJob(job));
    }

    public Task<PrintJobDto?> QueueOriginalAsync(Guid saleId, CancellationToken ct = default) =>
        QueueAsync(saleId, null, null, ct);

    public Task<PrintJobDto?> QueueReprintAsync(Guid requestId, Guid saleId, Guid actorId,
        CancellationToken ct = default) => QueueAsync(saleId, requestId, actorId, ct);

    public async Task<bool> RecoverOneOriginalAsync(CancellationToken ct = default)
    {
        var saleId = await db.DocumentoEmitido.AsNoTracking()
            .Where(d => d.TipoDocumento == "COMPROBANTE_VENTA" && d.VersionDocumento == 1 &&
                db.Venta.Any(v => v.Id == d.EntidadId && v.Estado == "CONFIRMADA") &&
                !d.TrabajoImpresion.Any(j => j.TipoCopia == "ORIGINAL") &&
                db.PerfilImpresora.Any(p => p.TerminalId == d.TerminalId && p.Activo &&
                    p.EsPredeterminada && p.FechaCreacion <= d.FechaEmision))
            .OrderBy(d => d.FechaEmision).Select(d => (Guid?)d.EntidadId).FirstOrDefaultAsync(ct);
        return saleId is Guid id && await QueueOriginalAsync(id, ct) is not null;
    }

    private async Task<PrintJobDto?> QueueAsync(Guid saleId, Guid? requestId, Guid? actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var documentType = "COMPROBANTE_VENTA";
        var documentRows = await db.DocumentoEmitido.FromSqlInterpolated(
            $"SELECT * FROM configuracion.documento_emitido WHERE entidad_id = {saleId} AND tipo_documento = {documentType} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct);
        var document = documentRows.SingleOrDefault();
        if (document is null) return null;
        if (!await db.Venta.AsNoTracking().AnyAsync(x => x.Id == saleId && x.Estado == "CONFIRMADA", ct))
            return null;
        var key = requestId is Guid id ? $"REPRINT_{id:N}" : $"SALE_ORIGINAL_{saleId:N}";
        var existing = await db.TrabajoImpresion.Include(x => x.DocumentoEmitido)
            .SingleOrDefaultAsync(x => x.TerminalId == document.TerminalId && x.ClaveIdempotencia == key, ct);
        if (existing is not null)
        {
            if (existing.DocumentoEmitido.EntidadId != saleId) return null;
            await tx.CommitAsync(ct);
            return ToJob(existing);
        }
        var profile = await db.PerfilImpresora.AsNoTracking().Where(x => x.TerminalId == document.TerminalId &&
            x.Activo && x.EsPredeterminada && (x.TipoImpresora == "RECIBOS" || x.TipoImpresora == "HIBRIDA"))
            .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (profile is null || requestId is null && profile.FechaCreacion > document.FechaEmision)
            return null;
        var template = await db.PlantillaImpresion.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Codigo == TemplateCode(profile.Id) && x.Activa, ct);
        if (template is null || !SaleReceiptSnapshotCodec.TryDecode(document, saleId, out var receipt))
            return null;
        var columns = ReadColumns(template.Configuracion);
        if (columns is < 24 or > 80) return null;
        var establishment = await db.Establecimiento.AsNoTracking()
            .Where(x => x.Id == template.EstablecimientoId)
            .Select(x => new ReceiptEstablishmentInfo(x.NombreComercial, x.Identificacion, x.Telefono,
                x.Direccion, x.EncabezadoComprobante, x.PieComprobante))
            .SingleAsync(ct);
        var content = ReceiptTextRenderer.Render(receipt!, establishment, columns, requestId is not null);
        var nextCopy = requestId is null ? 0 :
            1 + (await db.TrabajoImpresion.Where(x => x.DocumentoEmitidoId == document.Id &&
                x.TipoCopia == "REIMPRESION").MaxAsync(x => (int?)x.NumeroReimpresion, ct) ?? 0);
        var job = new TrabajoImpresion
        {
            Id = Guid.CreateVersion7(), DocumentoEmitidoId = document.Id,
            PerfilImpresoraId = profile.Id, PlantillaImpresionId = template.Id,
            TerminalId = document.TerminalId, SolicitadoPorId = actorId ?? document.EmitidoPorId,
            ClaveIdempotencia = key, TipoCopia = requestId is null ? "ORIGINAL" : "REIMPRESION",
            NumeroReimpresion = nextCopy, Estado = "PENDIENTE", Intentos = 0,
            ContenidoRenderizado = content, HashContenido = Convert.ToHexString(SHA256.HashData(content)),
            FechaSolicitud = clock.UtcNow.UtcDateTime
        };
        db.TrabajoImpresion.Add(job);
        await WriteAuditAsync(job.SolicitadoPorId, AuditAction.PrintJobQueued, nameof(TrabajoImpresion), job.Id,
            new { job.Id, job.DocumentoEmitidoId, job.TipoCopia, job.NumeroReimpresion }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        job.DocumentoEmitido = document;
        return ToJob(job);
    }

    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.TrabajoImpresion.FromSqlRaw(
                "SELECT * FROM configuracion.trabajo_impresion WHERE estado = 'PENDIENTE' ORDER BY fecha_solicitud, id LIMIT 1 FOR UPDATE SKIP LOCKED")
            .AsTracking().ToArrayAsync(ct);
        var job = rows.SingleOrDefault();
        if (job is null) return false;
        job.Estado = "IMPRIMIENDO";
        job.Intentos++;
        job.FechaUltimoIntento = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var profile = await db.PerfilImpresora.AsNoTracking().SingleAsync(x => x.Id == job.PerfilImpresoraId, CancellationToken.None);
        var status = "FALLIDO";
        if (string.Equals(job.HashContenido,
            Convert.ToHexString(SHA256.HashData(job.ContenidoRenderizado)), StringComparison.Ordinal))
        {
            try
            {
                await spooler.SubmitAsync(profile.NombreSistema, job.ContenidoRenderizado,
                    $"ControlPlus {job.TipoCopia} {job.Id:N}", CancellationToken.None);
                status = "IMPRESO";
            }
            catch (PrintSubmissionException exception)
            {
                status = exception.MayHaveBeenAccepted ? "IMPRIMIENDO" : "FALLIDO";
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Unknown transport failures may have occurred after the queue accepted a job.
                status = exception is PlatformNotSupportedException ? "FALLIDO" : "IMPRIMIENDO";
            }
        }

        job.Estado = status;
        job.FechaImpresion = status == "IMPRESO" ? clock.UtcNow.UtcDateTime : null;
        job.ErrorSanitizado = status switch
        {
            "FALLIDO" => "La cola rechazó el trabajo o el contenido no pudo verificarse.",
            "IMPRIMIENDO" => "Resultado incierto: revisar la cola antes de solicitar otra copia.",
            _ => null
        };
        await WriteAuditAsync(job.SolicitadoPorId,
            status switch
            {
                "IMPRESO" => AuditAction.PrintJobAccepted,
                "IMPRIMIENDO" => AuditAction.PrintJobUncertain,
                _ => AuditAction.PrintJobFailed
            },
            nameof(TrabajoImpresion), job.Id, new { job.Id, job.Intentos, job.Estado,
                QueueAccepted = status == "IMPRESO" }, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    private async Task<ApplicationError?> RequirePermissionAsync(Guid userId, string permission, CancellationToken ct)
    {
        var auth = await users.GetAuthorizationSnapshotAsync(userId, ct);
        if (auth is null) return ApplicationError.Unauthorized();
        return auth.PermissionCodes.Contains(permission, StringComparer.OrdinalIgnoreCase)
            ? null : ApplicationError.Forbidden();
    }

    private static ApplicationError? ValidatePage(PrintJobsQuery query) =>
        query.Page < 1 || query.PageSize is < 1 or > 100 || query.Page > int.MaxValue / query.PageSize
            ? ApplicationError.Validation("page debe ser positivo y pageSize debe estar entre 1 y 100.")
            : null;

    private async Task<Terminal?> GetActiveTerminalAsync(CancellationToken ct)
    {
        var installation = await db.Instalacion.AsNoTracking().Where(x => x.Activo)
            .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        return installation is null ? null : await db.Terminal.AsNoTracking()
            .Where(x => x.InstalacionId == installation.Id && x.Activo)
            .OrderBy(x => x.FechaCreacion).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<Terminal?> GetLockedActiveTerminalAsync(CancellationToken ct)
    {
        var terminal = await GetActiveTerminalAsync(ct);
        return terminal is null ? null : (await db.Terminal.FromSqlInterpolated(
            $"SELECT * FROM configuracion.terminal WHERE id = {terminal.Id} FOR UPDATE")
            .AsTracking().ToArrayAsync(ct)).Single();
    }

    private async Task WriteAuditAsync(Guid userId, AuditAction action, string entity,
        Guid id, object details, CancellationToken ct) =>
        await audit.AddAsync(AuditRecord.Create(userId, action, entity, id,
            JsonSerializer.Serialize(details), clock.UtcNow), ct);

    private static string TemplateCode(Guid profileId) => ReceiptTemplatePrefix + profileId.ToString("N").ToUpperInvariant();

    private static int ReadColumns(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            return parsed.RootElement.TryGetProperty("columns", out var value) && value.TryGetInt32(out var columns)
                ? columns : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static PrintProfileDto ToProfile(PerfilImpresora profile, int columns) =>
        new(profile.Id, profile.Nombre, profile.NombreSistema, columns,
            profile.EsPredeterminada, profile.Activo, profile.Version);

    private static PrintJobDto ToJob(TrabajoImpresion job) =>
        new(job.Id, job.DocumentoEmitido.EntidadId, job.TipoCopia, job.NumeroReimpresion,
            job.Estado == "FALLIDO" ? "ERROR" : job.Estado, job.Intentos, new DateTimeOffset(job.FechaSolicitud, TimeSpan.Zero),
            job.FechaImpresion is DateTime date ? new DateTimeOffset(date, TimeSpan.Zero) : null,
            job.ErrorSanitizado);
}
