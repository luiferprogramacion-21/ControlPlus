using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ControlPlus.Infrastructure.Printing;

/// <summary>
/// Reserves one durable decision per business operation before submitting a separate RAW pulse.
/// A replay, a receipt reprint, or a process restart never resends the same decision.
/// </summary>
public sealed class CashDrawerService(
    OfficialControlPlusDbContext db,
    IUserRepository users,
    IAuditRepository audit,
    IClock clock,
    ICashDrawerPulseSender sender,
    ILogger<CashDrawerService> logger) : ICashDrawerService
{
    private const string ManualType = "MANUAL";

    public async Task<Result<CashDrawerStatusDto>> GetStatusAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        var auth = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        if (auth is null) return Result.Failure<CashDrawerStatusDto>(ApplicationError.Unauthorized());
        if (!auth.PermissionCodes.Contains(PermissionCodes.CashOwnRead, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<CashDrawerStatusDto>(ApplicationError.Forbidden());
        var terminal = await GetActiveTerminalAsync(ct);
        if (terminal is null) return Result.Success(new CashDrawerStatusDto(false, null, false));
        var profile = await GetDefaultReceiptProfileAsync(terminal.Id, ct);
        var shiftOpen = await db.TurnoCaja.AsNoTracking().AnyAsync(x =>
            x.TerminalId == terminal.Id && x.Estado == "ABIERTA", ct);
        var enabled = profile?.CajonHabilitado == true && profile.CajonPin is 2 or 5;
        var canManage = auth.HighestRoleLevel is RoleLevel.Supervisor or RoleLevel.Administrador &&
            auth.PermissionCodes.Contains(PermissionCodes.CashShiftManage, StringComparer.OrdinalIgnoreCase);
        return Result.Success(new CashDrawerStatusDto(enabled, profile?.Nombre,
            enabled && shiftOpen && canManage));
    }

    public async Task<Result<CashDrawerOpenResultDto>> OpenManualAsync(ActorContext actor,
        Guid idempotencyId, OpenCashDrawerRequest request, CancellationToken ct = default)
    {
        var auth = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        if (auth is null) return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Unauthorized());
        if (auth.HighestRoleLevel is not (RoleLevel.Supervisor or RoleLevel.Administrador) ||
            !auth.PermissionCodes.Contains(PermissionCodes.CashShiftManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Forbidden());
        var reason = request?.Reason?.Trim();
        if (idempotencyId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 500 ||
            reason.Any(char.IsControl))
            return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Validation(
                "La apertura manual requiere una clave UUID y un motivo de hasta 500 caracteres."));

        var existing = await db.AperturaCajon.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TipoOperacion == ManualType && x.OperacionId == idempotencyId, ct);
        if (existing is not null) return ExistingManualResult(actor, reason, existing);

        var terminal = await GetActiveTerminalAsync(ct);
        if (terminal is null) return Result.Failure<CashDrawerOpenResultDto>(
            ApplicationError.Conflict("No hay una terminal activa para abrir el cajón."));
        var shift = await db.TurnoCaja.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TerminalId == terminal.Id && x.Estado == "ABIERTA", ct);
        if (shift is null) return Result.Failure<CashDrawerOpenResultDto>(
            ApplicationError.Conflict("Abre un turno de caja antes de usar el cajón."));
        var profile = await GetDefaultReceiptProfileAsync(terminal.Id, ct);
        if (profile?.CajonHabilitado != true || profile.CajonPin is not (2 or 5))
            return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Conflict(
                "Configura y habilita el cajón en el perfil predeterminado de recibos."));

        var result = await ReserveAndSendAsync(actor.UserId, ManualType, idempotencyId,
            shift, profile, reason, ct);
        // A concurrent request with this key may have won after the first lookup.
        var saved = await db.AperturaCajon.AsNoTracking().SingleAsync(x =>
            x.TipoOperacion == ManualType && x.OperacionId == idempotencyId, CancellationToken.None);
        if (saved.SolicitadoPorId != actor.UserId ||
            !string.Equals(saved.Motivo, reason, StringComparison.Ordinal))
            return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Conflict(
                "La clave de idempotencia ya se usó con otra apertura manual."));
        if (result.Status == "OMITIDO")
            return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Conflict(
                "El cajón dejó de estar habilitado. Actualiza la configuración antes de abrirlo."));
        return Result.Success(result);
    }

    private const string CheckoutType = "COBRO";

    public async Task<Result<CashDrawerOpenResultDto>> OpenForCheckoutAsync(ActorContext actor, Guid checkoutId,
        CancellationToken ct = default)
    {
        var auth = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        if (auth is null) return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Unauthorized());
        if (!auth.PermissionCodes.Contains(PermissionCodes.SalesCreate, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Forbidden());
        if (checkoutId == Guid.Empty)
            return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Validation("La clave del cobro debe ser un UUID."));
        var terminal = await GetActiveTerminalAsync(ct);
        var shift = terminal is null ? null : await db.TurnoCaja.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TerminalId == terminal.Id && x.Estado == "ABIERTA", ct);
        if (shift is null) return Result.Failure<CashDrawerOpenResultDto>(
            ApplicationError.Conflict("Abre un turno de caja antes de cobrar."));
        var profile = await GetDefaultReceiptProfileAsync(shift.TerminalId, ct);
        if (profile?.CajonHabilitado != true || profile.CajonPin is not (2 or 5))
            return Result.Success(new CashDrawerOpenResultDto(Guid.Empty, "DESACTIVADO", "El cajón no está habilitado."));
        return Result.Success(await ReserveAndSendAsync(actor.UserId, CheckoutType, checkoutId, shift, profile, null, ct));
    }

    public async Task<CashDrawerOpenResultDto?> TryOpenAutomaticAsync(ActorContext actor,
        CashDrawerOperationKind kind, Guid operationId, CancellationToken ct = default)
    {
        if (operationId == Guid.Empty) return null;
        var operation = await FindOperationAsync(kind, operationId, ct);
        if (operation is null || operation.UserId != actor.UserId || !operation.HasCash) return null;
        var shift = await db.TurnoCaja.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == operation.ShiftId, ct);
        if (shift is null || shift.Estado != "ABIERTA") return null;
        var profile = await GetDefaultReceiptProfileAsync(shift.TerminalId, ct);
        if (profile?.CajonHabilitado != true || profile.CajonPin is not (2 or 5)) return null;
        var now = clock.UtcNow.UtcDateTime;
        var enabledSince = profile.FechaModificacion ?? profile.FechaCreacion;
        var fresh = operation.OccurredAtUtc <= now.AddSeconds(5) &&
            operation.OccurredAtUtc >= now.AddMinutes(-2);
        // Nothing is written when the drawer cannot open: with the drawer disabled (the default) a cash
        // operation keeps exactly its previous writes and audit trail. A replay of an old operation, or of one
        // confirmed before the profile last changed, never opens the drawer.
        if (!fresh || enabledSince > operation.OccurredAtUtc) return null;
        return await ReserveAndSendAsync(actor.UserId, OperationType(kind), operationId,
            shift, profile, null, ct);
    }

    private async Task<CashDrawerOpenResultDto> ReserveAndSendAsync(Guid actorId, string operationType,
        Guid operationId, TurnoCaja shift, PerfilImpresora? profile, string? reason, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var attempt = new AperturaCajon
        {
            Id = Guid.CreateVersion7(), PerfilImpresoraId = profile?.Id,
            TerminalId = shift.TerminalId, TurnoCajaId = shift.Id,
            SolicitadoPorId = actorId, TipoOperacion = operationType, OperacionId = operationId,
            Estado = profile is null ? "OMITIDO" : "INTENTANDO",
            Motivo = reason, FechaSolicitud = now.UtcDateTime,
            FechaResultado = profile is null ? now.UtcDateTime : null
        };

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await LockInstallationAsync(ct);
            // The unique key serializes concurrent HTTP replays without a second pulse.
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO caja.apertura_cajon
                  (id, perfil_impresora_id, terminal_id, turno_caja_id, solicitado_por_id,
                   tipo_operacion, operacion_id, estado, motivo, fecha_solicitud, fecha_resultado)
                VALUES ({attempt.Id}, {attempt.PerfilImpresoraId}, {attempt.TerminalId},
                        {attempt.TurnoCajaId}, {attempt.SolicitadoPorId}, {attempt.TipoOperacion},
                        {attempt.OperacionId}, {attempt.Estado}, {attempt.Motivo},
                        {attempt.FechaSolicitud}, {attempt.FechaResultado})
                ON CONFLICT (tipo_operacion, operacion_id) DO NOTHING
                """, ct);
            if (inserted == 0)
            {
                var existing = await db.AperturaCajon.AsNoTracking().SingleAsync(x =>
                    x.TipoOperacion == operationType && x.OperacionId == operationId, ct);
                return ToResult(existing);
            }
            await WriteAuditAsync(actorId,
                profile is null ? AuditAction.CashDrawerOpeningSkipped : AuditAction.CashDrawerOpeningRequested,
                attempt, "EXITOSO", ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        if (profile is null) return ToResult(attempt);

        // Configuration can be disabled while the reservation commits. Never pulse a retired profile.
        var stillEnabled = await db.PerfilImpresora.AsNoTracking().AnyAsync(x =>
            x.Id == profile.Id && x.Activo && x.EsPredeterminada && x.CajonHabilitado &&
            x.CajonPin == profile.CajonPin && x.CajonPulsoOnMs == profile.CajonPulsoOnMs &&
            x.CajonPulsoOffMs == profile.CajonPulsoOffMs, CancellationToken.None);
        if (!stillEnabled)
            return await CompleteAsync(attempt.Id, "OMITIDO", null, CancellationToken.None);

        string state;
        string? error = null;
        try
        {
            var pulse = new CashDrawerPulse(profile.CajonPin!.Value,
                (byte)(profile.CajonPulsoOnMs / 2), (byte)(profile.CajonPulsoOffMs / 2));
            await sender.SendPulseAsync(profile.NombreSistema, pulse, CancellationToken.None);
            state = "ENVIADO";
        }
        catch (CashDrawerPulseSubmissionException exception)
        {
            state = exception.MayHaveBeenAccepted ? "INCIERTO" : "FALLIDO";
            error = "La cola de impresión no confirmó la orden de apertura.";
            logger.LogError(exception, "Resultado {State} al enviar apertura de cajón {AttemptId}.",
                state, attempt.Id);
        }
        catch (PlatformNotSupportedException exception)
        {
            state = "FALLIDO";
            error = "La apertura requiere el servicio instalado en Windows.";
            logger.LogError(exception, "Apertura de cajón {AttemptId} no disponible en esta plataforma.", attempt.Id);
        }
        catch (Exception exception)
        {
            state = "INCIERTO";
            error = "No se pudo confirmar si la impresora recibió la orden.";
            logger.LogError(exception, "Resultado incierto de apertura de cajón {AttemptId}.", attempt.Id);
        }
        return await CompleteAsync(attempt.Id, state, error, CancellationToken.None);
    }

    private async Task<CashDrawerOpenResultDto> CompleteAsync(Guid attemptId, string state,
        string? error, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await LockInstallationAsync(ct);
            var attempt = await db.AperturaCajon.AsTracking().SingleAsync(x => x.Id == attemptId, ct);
            attempt.Estado = state;
            attempt.ErrorSanitizado = error;
            attempt.FechaResultado = clock.UtcNow.UtcDateTime;
            var action = state switch
            {
                "ENVIADO" => AuditAction.CashDrawerOpeningAccepted,
                "FALLIDO" => AuditAction.CashDrawerOpeningFailed,
                "OMITIDO" => AuditAction.CashDrawerOpeningSkipped,
                _ => AuditAction.CashDrawerOpeningUncertain
            };
            await WriteAuditAsync(attempt.SolicitadoPorId, action, attempt,
                state is "ENVIADO" or "OMITIDO" ? "EXITOSO" : "FALLIDO", ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ToResult(attempt);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "No se pudo guardar el resultado del intento de cajón {AttemptId}.", attemptId);
            db.ChangeTracker.Clear();
            return new CashDrawerOpenResultDto(attemptId, "INCIERTO",
                "Revisa físicamente el cajón: no se pudo guardar el resultado de la orden.");
        }
    }

    /// <summary>
    /// Takes the active installation in key-share mode before writing. Sales, payments and cash movements
    /// lock it first (FOR UPDATE) and the shift afterwards, while the drawer row references the shift and its
    /// audit references the installation. Taking the installation first keeps the same lock order, so a
    /// concurrent operation waits instead of deadlocking; key-share stays compatible with the audit writes of
    /// operations that lock the shift first.
    /// </summary>
    private Task<int> LockInstallationAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        SELECT 1 FROM configuracion.instalacion
        WHERE activo ORDER BY fecha_instalacion, id LIMIT 1 FOR KEY SHARE
        """, ct);

    private static Result<CashDrawerOpenResultDto> ExistingManualResult(ActorContext actor,
        string reason, AperturaCajon existing)
    {
        if (existing.SolicitadoPorId != actor.UserId ||
            !string.Equals(existing.Motivo, reason, StringComparison.Ordinal))
            return Result.Failure<CashDrawerOpenResultDto>(ApplicationError.Conflict(
                "La clave de idempotencia ya se usó con otra apertura manual."));
        return Result.Success(ToResult(existing));
    }

    private static CashDrawerOpenResultDto ToResult(AperturaCajon attempt)
    {
        var state = attempt.Estado == "INTENTANDO" ? "INCIERTO" : attempt.Estado;
        var message = state switch
        {
            "ENVIADO" => "Windows aceptó la orden. Comprueba si el cajón se abrió.",
            "FALLIDO" => "No se pudo enviar la orden. Revisa la impresora y el cable.",
            "INCIERTO" => "No se pudo confirmar el envío. Revisa el cajón antes de otra apertura.",
            _ => "No se envió una orden de apertura para esta operación."
        };
        return new CashDrawerOpenResultDto(attempt.Id, state, message);
    }

    private async Task WriteAuditAsync(Guid actorId, AuditAction action, AperturaCajon attempt,
        string result, CancellationToken ct) => await audit.AddAsync(AuditRecord.Create(actorId,
            action, nameof(AperturaCajon), attempt.Id,
            JsonSerializer.Serialize(new
            {
                attempt.TipoOperacion, attempt.OperacionId, attempt.PerfilImpresoraId,
                attempt.TurnoCajaId, attempt.Estado
            }), clock.UtcNow, attempt.OperacionId.ToString(), terminalId: attempt.TerminalId,
            reason: attempt.Motivo, result: result), ct);

    private async Task<Terminal?> GetActiveTerminalAsync(CancellationToken ct)
    {
        var installation = await db.Instalacion.AsNoTracking().Where(x => x.Activo)
            .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        return installation is null ? null : await db.Terminal.AsNoTracking()
            .Where(x => x.InstalacionId == installation.Id && x.Activo)
            .OrderBy(x => x.FechaCreacion).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
    }

    private Task<PerfilImpresora?> GetDefaultReceiptProfileAsync(Guid terminalId, CancellationToken ct) =>
        db.PerfilImpresora.AsNoTracking().Where(x => x.TerminalId == terminalId && x.Activo &&
            x.EsPredeterminada && (x.TipoImpresora == "RECIBOS" || x.TipoImpresora == "HIBRIDA"))
            .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);

    private async Task<BusinessOperation?> FindOperationAsync(CashDrawerOperationKind kind, Guid id,
        CancellationToken ct)
    {
        switch (kind)
        {
            case CashDrawerOperationKind.Sale:
            {
                var row = await db.Venta.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                if (row is null) return null;
                var cash = await db.PagoVenta.AsNoTracking().AnyAsync(x => x.VentaId == id &&
                    x.ValorAplicado > 0 && x.MetodoPago.Codigo == "EFECTIVO", ct);
                return new(row.TurnoCajaId, row.UsuarioId, row.FechaHora, cash);
            }
            case CashDrawerOperationKind.CreditPayment:
            {
                var row = await db.PagoCredito.AsNoTracking().Include(x => x.MetodoPago)
                    .SingleOrDefaultAsync(x => x.Id == id, ct);
                return row is null ? null : new(row.TurnoCajaId, row.UsuarioId, row.FechaHora,
                    row.ValorAplicado > 0 && row.MetodoPago.Codigo == "EFECTIVO");
            }
            case CashDrawerOperationKind.LayawayInitial:
            {
                var row = await db.Apartado.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                if (row is null) return null;
                var correlation = await db.OperacionIdempotente.AsNoTracking().Where(x =>
                    x.EntidadId == id && x.TipoOperacion == "CREAR_APARTADO")
                    .Select(x => (Guid?)x.CorrelacionId).SingleOrDefaultAsync(ct);
                var cash = correlation is Guid correlationId && await db.PagoApartado.AsNoTracking()
                    .AnyAsync(x => x.ApartadoId == id && x.ValorAplicado > 0 &&
                        x.MovimientoCaja != null && x.MovimientoCaja.CorrelacionId == correlationId &&
                        x.MetodoPago.Codigo == "EFECTIVO", ct);
                return new(row.TurnoCajaId, row.UsuarioId, row.FechaHora, cash);
            }
            case CashDrawerOperationKind.LayawayPayment:
            {
                var row = await db.MovimientoCaja.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CorrelacionId == id && x.CategoriaMovimiento == "ABONO_APARTADO", ct);
                if (row is null) return null;
                var cash = await db.PagoApartado.AsNoTracking().AnyAsync(x =>
                    x.MovimientoCajaId == row.Id && x.ValorAplicado > 0 &&
                    x.MetodoPago.Codigo == "EFECTIVO", ct);
                return new(row.TurnoCajaId, row.UsuarioId, row.FechaHora, cash);
            }
            case CashDrawerOperationKind.LayawayRefund:
            {
                var row = await db.ReembolsoApartado.AsNoTracking().Include(x => x.MetodoPago)
                    .SingleOrDefaultAsync(x => x.Id == id, ct);
                return row is null ? null : new(row.TurnoCajaId, row.UsuarioId, row.FechaHora,
                    row.Valor > 0 && row.MetodoPago.Codigo == "EFECTIVO");
            }
            case CashDrawerOperationKind.CashMovement:
            {
                var row = await db.MovimientoCaja.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                return row is null ? null : new(row.TurnoCajaId, row.UsuarioId, row.FechaHora, row.Valor > 0);
            }
            case CashDrawerOperationKind.SaleChange:
            {
                var row = await db.CambioVenta.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                if (row is null) return null;
                var cash = await db.PagoCambioVenta.AsNoTracking().AnyAsync(x =>
                    x.CambioVentaId == id && x.Valor > 0 && x.MetodoPago.Codigo == "EFECTIVO", ct);
                return new(row.TurnoCajaId, row.UsuarioId, row.FechaHora, cash);
            }
            default:
                return null;
        }
    }

    private static string OperationType(CashDrawerOperationKind kind) => kind switch
    {
        CashDrawerOperationKind.Sale => "VENTA",
        CashDrawerOperationKind.CreditPayment => "ABONO_CREDITO",
        CashDrawerOperationKind.LayawayInitial => "APARTADO_INICIAL",
        CashDrawerOperationKind.LayawayPayment => "ABONO_APARTADO",
        CashDrawerOperationKind.LayawayRefund => "REEMBOLSO_APARTADO",
        CashDrawerOperationKind.CashMovement => "MOVIMIENTO_CAJA",
        CashDrawerOperationKind.SaleChange => "CAMBIO_VENTA",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed record BusinessOperation(Guid ShiftId, Guid UserId, DateTime OccurredAtUtc, bool HasCash);
}
