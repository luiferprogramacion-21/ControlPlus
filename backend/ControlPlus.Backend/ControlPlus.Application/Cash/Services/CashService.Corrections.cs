using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Cash.Services;

public sealed partial class CashService
{
    public async Task<Result<CashMovementCorrectionDto>> CorrectMovementAsync(ActorContext actor, Guid movementId,
        string key, CorrectCashMovementRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || movementId == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Length > 100 ||
            request.ExpectedShiftVersion <= 0 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Result.Failure<CashMovementCorrectionDto>(Validation("Indica el movimiento, la versión y un motivo de hasta 500 caracteres."));
        if (request.ReplacementKind is not null && request.ReplacementKind is not ("INGRESO" or "GASTO" or "SANGRIA") ||
            request.ReplacementKind is null && (request.ReplacementAmount is not null || request.ReplacementConcept is not null) ||
            request.ReplacementKind is not null && (request.ReplacementAmount is not decimal amount || !IsWholePositiveMoney(amount) ||
                string.IsNullOrWhiteSpace(request.ReplacementConcept) || request.ReplacementConcept.Trim().Length > 500))
            return Result.Failure<CashMovementCorrectionDto>(Validation("Elige anular, o indica tipo, monto y concepto del movimiento correcto."));
        if (request.Authorization is { } credentials)
        {
            if (!credentials.IsComplete() || credentials.MixesCredentialWithPassword())
                return Result.Failure<CashMovementCorrectionDto>(Validation("Usa carné o usuario y contraseña del autorizador."));
            if (credentials.UsesCredential())
            {
                var (name, id) = await credentialResolver.ResolveOrUnknownAsync(credentials.CredentialToken!, cancellationToken);
                request = request with { Authorization = credentials with { UserName = name, ResolvedCredentialId = id } };
            }
        }
        var normalized = request with { Reason = request.Reason.Trim(), ReplacementConcept = request.ReplacementConcept?.Trim() };
        return await loginAttemptCoordinator.ExecuteAsync(normalized.Authorization?.UserName?.ToUpperInvariant() ?? actor.UserId.ToString("N"),
            ct => CorrectMovementWithinTransactionAsync(actor, movementId, key.Trim(), normalized, ct), cancellationToken);
    }

    private async Task<Result<CashMovementCorrectionDto>> CorrectMovementWithinTransactionAsync(ActorContext actor,
        Guid id, string key, CorrectCashMovementRequest request, CancellationToken ct)
    {
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<CashMovementCorrectionDto>(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        if (snapshot is null) return Result.Failure<CashMovementCorrectionDto>(ApplicationError.Unauthorized());
        if (snapshot.HighestRoleLevel is not (RoleLevel.Administrador or RoleLevel.Supervisor) ||
            !snapshot.PermissionCodes.Contains(PermissionCodes.CashMovementsManage, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<CashMovementCorrectionDto>(ApplicationError.Forbidden());
        var self = snapshot.HighestRoleLevel == RoleLevel.Administrador && request.Authorization is null;
        var hash = CorrectionHash(new { Id = id, request.ExpectedShiftVersion, request.Reason, request.ReplacementKind,
            request.ReplacementAmount, request.ReplacementConcept, request.OperatorSessionId,
            Authorizer = self ? "ADMINISTRATOR_SELF" : request.Authorization?.UserName?.ToUpperInvariant() });
        var previous = await repository.GetCorrectionIdempotencyAsync(installation.Id, key, ct);
        if (previous is not null) return ReplayCorrection<CashMovementCorrectionDto>(previous, actor.UserId, id, "CORREGIR_MOVIMIENTO_CAJA", hash);
        if (!self && request.Authorization is null)
            return Result.Failure<CashMovementCorrectionDto>(Validation("Un Supervisor necesita la autorización de un Administrador."));
        var shift = await repository.GetOpenShiftAsync(true, ct);
        if (shift is null) return Result.Failure<CashMovementCorrectionDto>(ApplicationError.Conflict("Abre caja para registrar la corrección."));
        if (shift.Version != request.ExpectedShiftVersion)
            return Result.Failure<CashMovementCorrectionDto>(ApplicationError.ConcurrencyConflict("La caja cambió. Actualiza antes de corregir."));
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, ct);
        if (terminal is null || shift.TerminalId != terminal.Id)
            return Result.Failure<CashMovementCorrectionDto>(ApplicationError.Conflict("El turno no corresponde a la terminal activa."));
        var sessionCheck = await CorrectionSessionAsync(shift, actor, request.OperatorSessionId, ct);
        if (sessionCheck.Error is not null) return Result.Failure<CashMovementCorrectionDto>(sessionCheck.Error);
        var original = await repository.GetMovementForCorrectionAsync(id, ct);
        if (original is null || original.TurnoCaja.Caja.InstalacionId != installation.Id)
            return Result.Failure<CashMovementCorrectionDto>(ApplicationError.NotFound("el movimiento"));
        if (!IsCorrectableManualMovement(original) || await repository.HasMovementPaymentsAsync(id, ct))
            return Result.Failure<CashMovementCorrectionDto>(Validation("Solo se corrigen ingresos, gastos y retiros manuales. Corrige ventas o abonos desde su documento."));
        if (await repository.HasMovementReversalAsync(id, ct))
            return Result.Failure<CashMovementCorrectionDto>(ApplicationError.Conflict("Este movimiento ya tiene una corrección."));
        var expected = await repository.GetExpectedAmountsAsync(shift.Id, ct);
        var cashBefore = CashMoney.Sum(expected.Where(x => x.AffectsCash).Select(x => x.ExpectedAmount));
        var reverseDelta = original.Tipo == "INGRESO" ? -original.Valor : original.Valor;
        var replacementDelta = request.ReplacementKind is null ? 0 : request.ReplacementKind == "INGRESO"
            ? request.ReplacementAmount!.Value : -request.ReplacementAmount!.Value;
        decimal cashAfter;
        try { cashAfter = CashMoney.Require(CashMoney.Add(CashMoney.Add(cashBefore, reverseDelta), replacementDelta), false); }
        catch (CashMoneyException)
        {
            var leaving = -(reverseDelta + replacementDelta);
            return Result.Failure<CashMovementCorrectionDto>(ApplicationError.Conflict(
                $"Con esta corrección saldrían ${leaving.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-CO"))} de la caja, pero solo tiene ${Math.Max(0, cashBefore).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-CO"))} en efectivo. Revisa el monto correcto."));
        }
        Usuario? authorizer = null;
        if (!self)
        {
            var validation = await ValidateAuthorizerAsync(actor, request.Authorization, ct);
            if (validation.IsFailure)
            {
                await AuditWriter.WriteCashFailureAsync(auditRepository, clock, actor.UserId, AuditAction.CashCorrectionAuthorizationRejected,
                    nameof(MovimientoCaja), id, new { Operation = "CORREGIR_MOVIMIENTO_CAJA" }, Guid.CreateVersion7(), terminal.Id, null, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Failure<CashMovementCorrectionDto>(validation.Error!);
            }
            authorizer = validation.Value!;
        }
        var now = clock.UtcNow; var correlation = Guid.CreateVersion7();
        var reversal = MovimientoCaja.Create(shift.Id, actor.UserId, sessionCheck.Session?.Id, "REVERSO", "CORRECCION_MANUAL",
            original.Valor, request.Reason, correlation, now);
        reversal.MovimientoRevertidoId = original.Id;
        await repository.AddMovementAsync(reversal, ct);
        MovimientoCaja? replacement = null;
        if (request.ReplacementKind is not null)
        {
            var category = request.ReplacementKind == "INGRESO" ? CashConstants.ManualIncomeCategory :
                request.ReplacementKind == "SANGRIA" ? CashConstants.CashDropCategory : CashConstants.ExpenseCategory;
            replacement = MovimientoCaja.Create(shift.Id, actor.UserId, sessionCheck.Session?.Id,
                request.ReplacementKind == "INGRESO" ? "INGRESO" : "EGRESO", category,
                request.ReplacementAmount!.Value, request.ReplacementConcept!, correlation, now);
            await repository.AddMovementAsync(replacement, ct);
        }
        repository.MarkShiftChanged(shift);
        if (authorizer is not null) { authorizer.RegisterSuccessfulLogin(now); await userRepository.UpdateAsync(authorizer, ct); }
        var response = new CashMovementCorrectionDto(id, reversal.Id, replacement?.Id, cashAfter, correlation);
        await AddCorrectionReceiptAsync(installation.Id, terminal.Id, actor.UserId, id, key, "CORREGIR_MOVIMIENTO_CAJA", hash, response, correlation, ct);
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.CashMovementCorrected,
            nameof(MovimientoCaja), original.Id, new { OriginalId = id, ReversalId = reversal.Id, ReplacementId = replacement?.Id,
                Reason = request.Reason, CashBefore = cashBefore, CashAfter = cashAfter, AuthorizationMode = self ? "ADMINISTRATOR_SELF" : "OTHER_USER" },
            correlation, authorizer?.Id ?? actor.UserId, sessionCheck.Session?.Id, terminal.Id, null, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(response);
    }

    public static bool IsCorrectableManualMovement(MovimientoCaja movement) => movement.MovimientoRevertidoId is null &&
        (movement.Tipo == "INGRESO" && movement.CategoriaMovimiento == CashConstants.ManualIncomeCategory ||
         movement.Tipo == "EGRESO" && movement.CategoriaMovimiento is CashConstants.ExpenseCategory or CashConstants.CashDropCategory);

    public async Task<Result<IReadOnlyCollection<CashShiftDto>>> ListClosedShiftsAsync(ActorContext actor, CancellationToken ct = default)
    {
        var error = await RequireClosureReviewAdministratorAsync(actor, ct);
        if (error is not null) return Result.Failure<IReadOnlyCollection<CashShiftDto>>(error);
        var cash = await repository.GetCashRegisterAsync(false, ct);
        return cash is null ? Result.Success<IReadOnlyCollection<CashShiftDto>>([]) :
            Result.Success<IReadOnlyCollection<CashShiftDto>>((await repository.ListClosedShiftsAsync(cash.InstalacionId, ct)).Select(Map).ToArray());
    }

    public async Task<Result<IReadOnlyCollection<CashClosureReviewDto>>> ListClosureReviewsAsync(ActorContext actor, Guid shiftId, CancellationToken ct = default)
    {
        var error = await RequireClosureReviewAdministratorAsync(actor, ct);
        if (error is not null) return Result.Failure<IReadOnlyCollection<CashClosureReviewDto>>(error);
        var shift = await repository.GetShiftForUpdateAsync(shiftId, ct);
        var cash = await repository.GetCashRegisterAsync(false, ct);
        if (shift is null || cash is null || shift.CajaId != cash.Id || shift.Estado != "CERRADA")
            return Result.Failure<IReadOnlyCollection<CashClosureReviewDto>>(ApplicationError.NotFound("el cierre"));
        return Result.Success<IReadOnlyCollection<CashClosureReviewDto>>((await repository.ListClosureReviewsAsync(shiftId, ct)).Select(MapReview).ToArray());
    }

    public async Task<Result<CashClosureReviewDto>> ReviewClosureAsync(ActorContext actor, Guid shiftId, string key,
        ReviewCashClosureRequest request, CancellationToken ct = default)
    {
        var error = await RequireClosureReviewAdministratorAsync(actor, ct);
        if (error is not null) return Result.Failure<CashClosureReviewDto>(error);
        if (request is null || shiftId == Guid.Empty || string.IsNullOrWhiteSpace(key) || key.Length > 100 ||
            request.ExpectedShiftVersion <= 0 || !IsWholeNonNegativeMoney(request.ReviewedCount) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Result.Failure<CashClosureReviewDto>(Validation("Indica conteo entero no negativo, versión y motivo de hasta 500 caracteres."));
        await using var transaction = await transactionManager.BeginAsync(cancellationToken: ct);
        var installation = await repository.GetActiveInstallationForUpdateAsync(ct);
        if (installation is null) return Result.Failure<CashClosureReviewDto>(ApplicationError.NotFound("la instalación activa"));
        error = await RequireClosureReviewAdministratorAsync(actor, ct);
        if (error is not null) return Result.Failure<CashClosureReviewDto>(error);
        var hash = CorrectionHash(new { Shift = shiftId, request.ExpectedShiftVersion, request.ReviewedCount, Reason = request.Reason.Trim() });
        var prior = await repository.GetCorrectionIdempotencyAsync(installation.Id, key.Trim(), ct);
        if (prior is not null) return ReplayCorrection<CashClosureReviewDto>(prior, actor.UserId, shiftId, "REVISAR_CIERRE_CAJA", hash);
        var shift = await repository.GetShiftForUpdateAsync(shiftId, ct);
        var cash = await repository.GetCashRegisterAsync(false, ct);
        if (shift is null || cash is null || shift.CajaId != cash.Id || shift.Estado != "CERRADA")
            return Result.Failure<CashClosureReviewDto>(ApplicationError.Conflict("El turno debe estar cerrado para registrar una revisión."));
        if (shift.Version != request.ExpectedShiftVersion) return Result.Failure<CashClosureReviewDto>(ApplicationError.ConcurrencyConflict("El turno cambió. Actualiza la pantalla antes de registrar la revisión."));
        if (shift.EfectivoContado is not decimal counted || shift.EfectivoEsperado is not decimal expected)
            return Result.Failure<CashClosureReviewDto>(ApplicationError.Conflict("El cierre no conserva un conteo verificable."));
        var now = clock.UtcNow; var correlation = Guid.CreateVersion7();
        var review = new RevisionCierreCaja { Id = Guid.CreateVersion7(), TurnoCajaId = shiftId, UsuarioId = actor.UserId,
            ContadoOriginal = counted, EfectivoEsperado = expected, ContadoRevisado = request.ReviewedCount,
            Motivo = request.Reason.Trim(), FechaHora = now.UtcDateTime, CorrelacionId = correlation };
        await repository.AddClosureReviewAsync(review, ct);
        var response = MapReview(review);
        await AddCorrectionReceiptAsync(installation.Id, shift.TerminalId, actor.UserId, shiftId, key.Trim(), "REVISAR_CIERRE_CAJA", hash, response, correlation, ct);
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.CashClosureReviewed,
            nameof(TurnoCaja), shiftId, new { ReviewId = review.Id, OriginalCount = counted, ReviewedCount = request.ReviewedCount,
                ExpectedCash = expected, Reason = review.Motivo }, correlation, actor.UserId, null, shift.TerminalId, null, ct);
        await unitOfWork.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return Result.Success(response);
    }

    private async Task<ApplicationError?> RequireClosureReviewAdministratorAsync(ActorContext actor, CancellationToken ct)
    {
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        return snapshot is null ? ApplicationError.Unauthorized() :
            snapshot.HighestRoleLevel != RoleLevel.Administrador || !snapshot.PermissionCodes.Contains(PermissionCodes.CashShiftManage, StringComparer.OrdinalIgnoreCase)
                ? ApplicationError.Forbidden() : null;
    }
    private async Task<(SesionOperador? Session, ApplicationError? Error)> CorrectionSessionAsync(TurnoCaja shift, ActorContext actor, Guid? id, CancellationToken ct)
    {
        if (shift.ModoOperacion == "INDIVIDUAL") return shift.UsuarioResponsableId == actor.UserId && id is null
            ? (null, null) : (null, ApplicationError.Forbidden("Opera desde tu turno individual."));
        var session = id is Guid sessionId ? await repository.GetOperatorSessionForUpdateAsync(sessionId, ct) : null;
        return session is { IsActive: true } && session.TurnoCajaId == shift.Id && session.TerminalId == shift.TerminalId && session.UsuarioId == actor.UserId
            ? (session, null) : (null, SharedShift.CardRequired());
    }
    private Task AddCorrectionReceiptAsync<T>(Guid installation, Guid terminal, Guid actor, Guid entity, string key,
        string type, string hash, T response, Guid correlation, CancellationToken ct) => repository.AddCorrectionIdempotencyAsync(new OperacionIdempotente
        { Id = Guid.CreateVersion7(), InstalacionId = installation, TerminalId = terminal, UsuarioId = actor,
          ClaveIdempotencia = key, TipoOperacion = type, HashSolicitud = hash, Entidad = "CorreccionCaja", EntidadId = entity,
          Resultado = JsonSerializer.Serialize(response), CorrelacionId = correlation, FechaRegistro = clock.UtcNow.UtcDateTime }, ct);
    private static Result<T> ReplayCorrection<T>(OperacionIdempotente previous, Guid actor, Guid entity, string type, string hash) where T : class
    {
        if (previous.UsuarioId != actor || previous.EntidadId != entity || previous.TipoOperacion != type || previous.HashSolicitud != hash || previous.Resultado is null)
            return Result.Failure<T>(ApplicationError.Conflict("La clave ya pertenece a otra operación."));
        try { var response = JsonSerializer.Deserialize<T>(previous.Resultado); return response is null
            ? Result.Failure<T>(ApplicationError.Conflict("No se puede recuperar la corrección.")) : Result.Success(response); }
        catch (JsonException) { return Result.Failure<T>(ApplicationError.Conflict("No se puede recuperar la corrección.")); }
    }
    private static string CorrectionHash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static CashClosureReviewDto MapReview(RevisionCierreCaja x) => new(x.Id, x.TurnoCajaId, x.UsuarioId, x.EfectivoEsperado,
        x.ContadoOriginal, x.ContadoRevisado, x.ContadoRevisado - x.EfectivoEsperado, x.Motivo, new DateTimeOffset(DateTime.SpecifyKind(x.FechaHora, DateTimeKind.Utc)));
}
