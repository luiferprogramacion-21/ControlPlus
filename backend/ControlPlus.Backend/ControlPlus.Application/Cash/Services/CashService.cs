using System.Data;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Cash.Services;

public sealed class CashService(
    ICashRepository repository,
    ICashTransactionManager transactionManager,
    IUnitOfWork unitOfWork,
    IPermissionChecker permissionChecker,
    IUserRepository userRepository,
    IPermissionRepository permissionRepository,
    IPasswordHasher passwordHasher,
    ICredentialTokenGenerator tokenGenerator,
    IAuditRepository auditRepository,
    IClock clock,
    ILoginAttemptCoordinator loginAttemptCoordinator) : ICashService
{
    private const int MaximumPageSize = 200;

    public async Task<Result<CashStateDto>> GetStateAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CashOwnRead, cancellationToken);
        if (error is not null) return Result.Failure<CashStateDto>(error);

        var establishment = await repository.GetEstablishmentAsync(false, cancellationToken);
        if (establishment is null) return Result.Failure<CashStateDto>(ApplicationError.NotFound("el establecimiento activo"));
        var cash = await repository.GetCashRegisterAsync(false, cancellationToken);
        var shift = cash is null ? null : await repository.GetOpenShiftAsync(false, cancellationToken);
        var authorization = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        var canManage = authorization is not null &&
            (authorization.PermissionCodes.Contains(PermissionCodes.CashShiftManage) ||
             authorization.PermissionCodes.Contains(PermissionCodes.CashMovementsManage) ||
             authorization.PermissionCodes.Contains(PermissionCodes.CashShiftModeManage));
        if (canManage || (shift?.ModoOperacion == CashConstants.IndividualMode && shift.UsuarioResponsableId == actor.UserId))
            return Result.Success(MapState(cash, shift, establishment));

        var shared = shift?.ModoOperacion == CashConstants.SharedMode;
        var ownSession = shared
            ? (await repository.ListActiveOperatorSessionsAsync(shift!.Id, cancellationToken))
                .FirstOrDefault(session => session.UsuarioId == actor.UserId)
            : null;
        return Result.Success(new CashStateDto(null, null, null, null, false, null,
            cash is not null, shift is not null, shared && ownSession is null,
            ownSession is null ? null : Map(ownSession)));
    }

    public async Task<Result<CashRegisterDto>> ConfigureCashRegisterAsync(
        ActorContext actor,
        ConfigureCashRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.ConfigurationManage, cancellationToken);
        if (error is not null) return Result.Failure<CashRegisterDto>(error);
        if (request is null || string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50)
            return Result.Failure<CashRegisterDto>(Validation("El código de la caja es obligatorio y admite hasta 50 caracteres."));
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            return Result.Failure<CashRegisterDto>(Validation("El nombre de la caja es obligatorio y admite hasta 100 caracteres."));

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.Serializable, cancellationToken);
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Result.Failure<CashRegisterDto>(ApplicationError.NotFound("la instalación activa"));
        if (await repository.GetCashRegisterAsync(false, cancellationToken) is not null)
            return Result.Failure<CashRegisterDto>(ApplicationError.Conflict("La caja única de ControlPlus ya está configurada."));

        var cash = Caja.Create(installation.Id, request.Code, request.Name, clock.UtcNow);
        await repository.AddCashRegisterAsync(cash, cancellationToken);
        var correlationId = Guid.CreateVersion7();
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.CashRegisterConfigured,
            nameof(Caja), cash.Id, new { cash.Codigo, cash.Nombre }, correlationId, null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(cash));
    }

    public async Task<Result<CashStateDto>> UpdateShiftModeAsync(
        ActorContext actor,
        UpdateCashShiftModeRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CashShiftModeManage, cancellationToken);
        if (error is not null) return Result.Failure<CashStateDto>(error);
        var mode = NormalizeMode(request?.Mode);
        if (mode is null) return Result.Failure<CashStateDto>(Validation("El modo debe ser INDIVIDUAL o COMPARTIDO."));

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.Serializable, cancellationToken);
        var establishment = await repository.GetEstablishmentAsync(true, cancellationToken);
        if (establishment is null) return Result.Failure<CashStateDto>(ApplicationError.NotFound("el establecimiento activo"));
        if (establishment.Version != request!.Version)
            return Result.Failure<CashStateDto>(ApplicationError.ConcurrencyConflict("La configuración fue modificada por otra operación."));
        if (await repository.GetOpenShiftAsync(false, cancellationToken) is not null)
            return Result.Failure<CashStateDto>(ApplicationError.Conflict("No se puede cambiar el modo mientras exista un turno abierto."));

        establishment.ChangeCashShiftMode(mode, clock.UtcNow);
        var correlationId = Guid.CreateVersion7();
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.CashShiftModeUpdated,
            nameof(Establecimiento), establishment.Id, new { Mode = mode }, correlationId, null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var cash = await repository.GetCashRegisterAsync(false, cancellationToken);
        return Result.Success(MapState(cash, null, establishment));
    }

    public async Task<Result<CashShiftDto>> OpenShiftAsync(
        ActorContext actor,
        OpenCashShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CashShiftManage, cancellationToken);
        if (error is not null) return Result.Failure<CashShiftDto>(error);
        if (request is null || !IsWholeNonNegativeMoney(request.InitialAmount))
            return Result.Failure<CashShiftDto>(Validation("El monto inicial debe ser un valor entero de COP mayor o igual a cero."));
        if (request.Observations?.Length > 2000)
            return Result.Failure<CashShiftDto>(Validation("Las observaciones admiten hasta 2000 caracteres."));

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.Serializable, cancellationToken);
        var cash = await repository.GetCashRegisterAsync(true, cancellationToken);
        if (cash is null) return Result.Failure<CashShiftDto>(ApplicationError.Conflict("Debe configurar la caja antes de abrir el primer turno."));
        if (!cash.Activo) return Result.Failure<CashShiftDto>(ApplicationError.Conflict("La caja configurada está inactiva."));
        var existing = await repository.GetOpenShiftAsync(false, cancellationToken);
        if (existing is not null)
        {
            var today = await OperatingDateAsync(cancellationToken);
            var message = existing.FechaOperativa < today
                ? "Existe un turno abierto de un día anterior. Debe arquearlo y cerrarlo antes de continuar."
                : "Ya existe un turno abierto para la caja.";
            return Result.Failure<CashShiftDto>(ApplicationError.Conflict(message));
        }

        var establishment = await repository.GetEstablishmentAsync(false, cancellationToken);
        if (establishment is null) return Result.Failure<CashShiftDto>(ApplicationError.NotFound("el establecimiento activo"));
        var terminal = await repository.GetActiveTerminalAsync(cash.InstalacionId, cancellationToken);
        if (terminal is null) return Result.Failure<CashShiftDto>(ApplicationError.NotFound("la terminal activa"));
        var now = clock.UtcNow;
        var shift = TurnoCaja.Open(cash.Id, terminal.Id, actor.UserId, establishment.ModoTurnoPredeterminado,
            ToOperatingDate(now, establishment.ZonaHoraria), request.InitialAmount, request.Observations, now);
        await repository.AddShiftAsync(shift, cancellationToken);

        var correlationId = Guid.CreateVersion7();
        if (request.InitialAmount > 0)
        {
            await repository.AddMovementAsync(MovimientoCaja.Create(shift.Id, actor.UserId, null,
                CashConstants.OpeningMovement, CashConstants.OpeningMovement, request.InitialAmount,
                "Fondo inicial", correlationId, now), cancellationToken);
        }

        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.CashShiftOpened,
            nameof(TurnoCaja), shift.Id, new { shift.ModoOperacion, shift.FechaOperativa, shift.MontoInicial },
            correlationId, null, null, terminal.Id, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(shift));
    }

    public async Task<Result<PagedResult<CashMovementDto>>> ListMovementsAsync(
        ActorContext actor,
        CashMovementListQuery query,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CashOwnRead, cancellationToken);
        if (error is not null) return Result.Failure<PagedResult<CashMovementDto>>(error);
        if (query.Page < 1 || query.PageSize is < 1 or > MaximumPageSize)
            return Result.Failure<PagedResult<CashMovementDto>>(Validation("La página debe ser positiva y pageSize debe estar entre 1 y 200."));
        var shift = await repository.GetOpenShiftAsync(false, cancellationToken);
        if (shift is null) return Result.Failure<PagedResult<CashMovementDto>>(ApplicationError.NotFound("un turno abierto"));
        var canSeeAll = await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.CashShiftManage, cancellationToken)
            || await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.CashMovementsManage, cancellationToken);
        var result = await repository.ListMovementsAsync(shift.Id, canSeeAll ? null : actor.UserId, query.Page, query.PageSize, cancellationToken);
        return Result.Success(new PagedResult<CashMovementDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount));
    }

    public Task<Result<CashMovementDto>> RegisterIncomeAsync(
        ActorContext actor, CreateCashMovementRequest request, CancellationToken cancellationToken = default) =>
        RegisterMovementAsync(actor, request, CashConstants.IncomeMovement, CashConstants.ManualIncomeCategory, AuditAction.CashMovementRegistered, cancellationToken);

    public Task<Result<CashMovementDto>> RegisterExpenseAsync(
        ActorContext actor, CreateCashMovementRequest request, CancellationToken cancellationToken = default) =>
        RegisterMovementAsync(actor, request, CashConstants.ExpenseMovement, CashConstants.ExpenseCategory, AuditAction.CashMovementRegistered, cancellationToken);

    public Task<Result<CashMovementDto>> RegisterCashDropAsync(
        ActorContext actor, CreateCashMovementRequest request, CancellationToken cancellationToken = default) =>
        RegisterMovementAsync(actor, request, CashConstants.ExpenseMovement, CashConstants.CashDropCategory, AuditAction.CashMovementRegistered, cancellationToken);

    public async Task<Result<CashReconciliationDto>> GetReconciliationAsync(
        ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CashShiftManage, cancellationToken);
        if (error is not null) return Result.Failure<CashReconciliationDto>(error);
        var shift = await repository.GetOpenShiftAsync(false, cancellationToken);
        if (shift is null) return Result.Failure<CashReconciliationDto>(ApplicationError.NotFound("un turno abierto"));
        var expected = await repository.GetExpectedAmountsAsync(shift.Id, cancellationToken);
        var validation = ValidateExpectedMethods(expected);
        return validation is null
            ? Result.Success(MapReconciliation(shift, expected, null))
            : Result.Failure<CashReconciliationDto>(validation);
    }

    public async Task<Result<CashReconciliationDto>> CloseShiftAsync(
        ActorContext actor,
        Guid shiftId,
        CloseCashShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CashShiftManage, cancellationToken);
        if (error is not null) return Result.Failure<CashReconciliationDto>(error);
        if (request?.PaymentMethods is null || request.PaymentMethods.Count == 0)
            return Result.Failure<CashReconciliationDto>(Validation("Debe registrar el valor contado o verificado para cada medio de pago."));
        if (request.PaymentMethods.Any(x => x is null || x.PaymentMethodId == Guid.Empty || !CashMoney.IsValid(x.CountedAmount)))
            return Result.Failure<CashReconciliationDto>(Validation("Cada valor contado debe ser un entero de COP dentro del rango monetario permitido."));
        if (request.PaymentMethods.Select(x => x.PaymentMethodId).Distinct().Count() != request.PaymentMethods.Count)
            return Result.Failure<CashReconciliationDto>(Validation("No se puede repetir un medio de pago en el arqueo."));
        if (request.DifferenceObservation?.Length > 2000)
            return Result.Failure<CashReconciliationDto>(Validation("La observación de diferencia admite hasta 2000 caracteres."));

        // The same account lock as login covers credentials and the complete financial
        // transaction. Failed credentials commit only their counter and failure audit;
        // successful credentials remain atomic with the closing, including COMMIT.
        if (!string.IsNullOrWhiteSpace(request.Authorization?.UserName))
            return await loginAttemptCoordinator.ExecuteAsync(
                request.Authorization.UserName.Trim().ToUpperInvariant(),
                token => CloseWithinTransactionAsync(actor, shiftId, request, token), cancellationToken);

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.Serializable, cancellationToken);
        var result = await CloseWithinTransactionAsync(actor, shiftId, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<Result<CashReconciliationDto>> CloseWithinTransactionAsync(
        ActorContext actor, Guid shiftId, CloseCashShiftRequest request, CancellationToken cancellationToken)
    {
        var permissionError = await RequireAsync(actor, PermissionCodes.CashShiftManage, cancellationToken);
        if (permissionError is not null) return Result.Failure<CashReconciliationDto>(permissionError);
        var shift = await repository.GetShiftForUpdateAsync(shiftId, cancellationToken);
        if (shift is null) return Result.Failure<CashReconciliationDto>(ApplicationError.NotFound("el turno de caja"));
        var correlationId = Guid.CreateVersion7();
        Usuario? authorizer = null;
        if (request.Authorization is not null)
        {
            var credentials = await ValidateAuthorizerAsync(actor, request.Authorization, cancellationToken);
            if (credentials.IsFailure)
                return await RejectCloseAuthorizationAsync(actor, shift, correlationId, credentials.Error!, cancellationToken);
            authorizer = credentials.Value!;
        }
        if (!shift.IsOpen) return Result.Failure<CashReconciliationDto>(ApplicationError.Conflict("El turno ya está cerrado."));
        if (shift.Version != request.Version)
            return Result.Failure<CashReconciliationDto>(ApplicationError.ConcurrencyConflict("El turno cambió después del arqueo. Actualice los valores esperados."));

        var expected = await repository.GetExpectedAmountsAsync(shift.Id, cancellationToken);
        var expectedValidation = ValidateExpectedMethods(expected);
        if (expectedValidation is not null) return Result.Failure<CashReconciliationDto>(expectedValidation);
        var counted = request.PaymentMethods.ToDictionary(x => x.PaymentMethodId, x => x.CountedAmount);
        if (!expected.Select(x => x.PaymentMethodId).ToHashSet().SetEquals(counted.Keys))
            return Result.Failure<CashReconciliationDto>(Validation("El arqueo debe incluir exactamente todos los medios de pago activos o utilizados."));
        if (expected.Any(x => x.AffectsCash && counted[x.PaymentMethodId] < 0))
            return Result.Failure<CashReconciliationDto>(Validation("El efectivo físico contado no puede ser negativo."));

        var details = expected.Select(item => new DetalleArqueoMedioPago
        {
            Id = Guid.CreateVersion7(),
            TurnoCajaId = shift.Id,
            MetodoPagoId = item.PaymentMethodId,
            ValorEsperado = item.ExpectedAmount,
            ValorContado = counted[item.PaymentMethodId],
            Diferencia = CashMoney.Subtract(counted[item.PaymentMethodId], item.ExpectedAmount)
        }).ToArray();
        var totalExpected = CashMoney.Sum(details.Select(x => x.ValorEsperado));
        var totalCounted = CashMoney.Sum(details.Select(x => x.ValorContado));
        var totalDifference = CashMoney.Subtract(totalCounted, totalExpected);
        CashMoney.Sum(details.Select(x => x.Diferencia));
        var cashExpected = CashMoney.Sum(details.Where(x => expected.Single(e => e.PaymentMethodId == x.MetodoPagoId).AffectsCash).Select(x => x.ValorEsperado));
        var cashCounted = CashMoney.Sum(details.Where(x => expected.Single(e => e.PaymentMethodId == x.MetodoPagoId).AffectsCash).Select(x => x.ValorContado));
        var now = clock.UtcNow;
        MotivoOperacion? reason = null;
        AutorizacionOperacion? authorization = null;

        if (totalDifference == 0)
        {
            if (request.DifferenceReasonId is not null || request.Authorization is not null)
                return Result.Failure<CashReconciliationDto>(Validation("Un cierre sin diferencia no requiere motivo ni autorización."));
        }
        else
        {
            if (request.DifferenceReasonId is not Guid reasonId)
                return Result.Failure<CashReconciliationDto>(Validation("Debe seleccionar un motivo para la diferencia."));
            reason = await repository.GetActiveDifferenceReasonAsync(reasonId, cancellationToken);
            if (reason is null) return Result.Failure<CashReconciliationDto>(ApplicationError.NotFound("el motivo activo de diferencia de caja"));
            if (authorizer is null)
                return await RejectCloseAuthorizationAsync(actor, shift, correlationId,
                    ApplicationError.Forbidden("El cierre con diferencia requiere credenciales de autorización."), cancellationToken);
            var permission = await permissionRepository.GetByCodeAsync(PermissionCodes.CashShiftManage, cancellationToken);
            if (permission is null) return Result.Failure<CashReconciliationDto>(ApplicationError.Conflict("No existe el permiso estable CASH.SHIFT_MANAGE."));
            authorization = new AutorizacionOperacion
            {
                Id = Guid.CreateVersion7(),
                UsuarioSolicitanteId = actor.UserId,
                UsuarioAutorizadorId = authorizer.Id,
                PermisoId = permission.Id,
                TipoOperacion = CashConstants.CashDifferenceAuthorizationType,
                CorrelacionId = correlationId,
                DescripcionOperacion = "Autorización de cierre de caja con diferencia.",
                Motivo = reason.Codigo,
                MetodoAutenticacion = "PASSWORD",
                Resultado = "EXITOSA",
                Estado = "AUTORIZADA",
                FechaSolicitud = now.UtcDateTime,
                FechaExpiracion = now.AddMinutes(5).UtcDateTime
            };
            await repository.AddAuthorizationAsync(authorization, cancellationToken);
        }

        // Details must be inserted while the shift is still open. These saves are
        // deliberately inside one transaction: deferred validation happens at COMMIT.
        await repository.AddReconciliationDetailsAsync(details, cancellationToken);
        if (authorizer is not null)
        {
            authorizer.RegisterSuccessfulLogin(now);
            await userRepository.UpdateAsync(authorizer, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (authorization is not null)
        {
            var cash = await repository.GetCashRegisterAsync(false, cancellationToken);
            var establishment = await repository.GetEstablishmentAsync(false, cancellationToken);
            var link = new AutorizacionCierreTurno
            {
                TurnoCajaId = shift.Id, AutorizacionId = authorization.Id, CorrelacionId = correlationId,
                UsuarioEjecutorId = actor.UserId, FechaVinculacion = now.UtcDateTime,
                InstalacionId = cash!.InstalacionId, EstablecimientoId = establishment!.Id
            };
            await repository.AddCloseAuthorizationLinkAsync(link, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        var sessions = await repository.ListActiveOperatorSessionsAsync(shift.Id, cancellationToken);
        foreach (var session in sessions)
        {
            session.Close(now, "CIERRE_TURNO");
            await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.OperatorSessionClosed,
                nameof(SesionOperador), session.Id,
                new { session.UsuarioId, session.TurnoCajaId, session.Estado, session.MotivoCierre, Automatic = true },
                correlationId, authorizer?.Id, session.Id, session.TerminalId, "CIERRE_TURNO", cancellationToken);
        }
        shift.Close(actor.UserId, cashExpected, cashCounted, totalDifference, reason?.Id,
            authorization?.Id, request.DifferenceObservation, now);
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.CashReconciled,
            nameof(DetalleArqueoMedioPago), shift.Id,
            new { TotalExpected = totalExpected, TotalCounted = totalCounted, CashDifference = shift.Diferencia, TotalDifference = totalDifference,
                PaymentMethods = details.Select(x => new { x.MetodoPagoId, x.ValorEsperado, x.ValorContado, x.Diferencia }) },
            correlationId, authorizer?.Id, null, shift.TerminalId, reason?.Codigo, cancellationToken);
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.CashShiftClosed,
            nameof(TurnoCaja), shift.Id, new { shift.FechaOperativa, CashDifference = shift.Diferencia, TotalDifference = totalDifference },
            correlationId, authorizer?.Id, null, shift.TerminalId, reason?.Codigo, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(MapReconciliation(shift, expected, counted));
    }

    private async Task<Result<CashReconciliationDto>> RejectCloseAuthorizationAsync(
        ActorContext actor, TurnoCaja shift, Guid correlationId, ApplicationError error, CancellationToken cancellationToken)
    {
        await AuditWriter.WriteCashFailureAsync(auditRepository, clock, actor.UserId,
            AuditAction.CashCloseAuthorizationRejected, nameof(TurnoCaja), shift.Id,
            new { Failure = "INVALID_AUTHORIZER_CREDENTIALS_OR_PERMISSION" }, correlationId,
            shift.TerminalId, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Failure<CashReconciliationDto>(error);
    }

    public async Task<Result<IssuedOperatorCredentialDto>> IssueOperatorCredentialAsync(
        ActorContext actor,
        Guid userId,
        IssueOperatorCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.UsersManage, cancellationToken);
        if (error is not null) return Result.Failure<IssuedOperatorCredentialDto>(error);
        var expiresAtUtc = request?.ExpiresAtUtc;
        if (expiresAtUtc is DateTimeOffset expires && (expires.Offset != TimeSpan.Zero || expires <= clock.UtcNow))
            return Result.Failure<IssuedOperatorCredentialDto>(Validation("La fecha de vencimiento debe ser UTC y posterior al momento actual."));

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.Serializable, cancellationToken);
        var user = await repository.GetUserForUpdateAsync(userId, cancellationToken);
        if (user is null) return Result.Failure<IssuedOperatorCredentialDto>(ApplicationError.NotFound("el usuario"));
        if (!user.IsActive) return Result.Failure<IssuedOperatorCredentialDto>(ApplicationError.Conflict("Solo un usuario activo y no bloqueado puede recibir credencial."));
        var now = clock.UtcNow;
        var existing = await repository.GetActiveCredentialForUserAsync(userId, cancellationToken);
        existing?.Revoke(actor.UserId, now, "REEMPLAZO");

        var token = tokenGenerator.Generate();
        var fragment = token[^Math.Min(8, token.Length)..];
        var credential = new CredencialUsuario
        {
            Id = Guid.CreateVersion7(), UsuarioId = user.Id, TokenHash = passwordHasher.Hash(token),
            FragmentoVisible = fragment, FormatoCodigo = "CODE128", Estado = "ACTIVA", EmitidaPorId = actor.UserId,
            FechaEmision = now.UtcDateTime, FechaVencimiento = expiresAtUtc?.UtcDateTime, Version = 1
        };
        await repository.AddCredentialAsync(credential, cancellationToken);
        var correlationId = Guid.CreateVersion7();
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.OperatorCredentialIssued,
            nameof(CredencialUsuario), credential.Id, new { credential.UsuarioId, credential.FormatoCodigo, credential.FragmentoVisible, credential.FechaVencimiento },
            correlationId, null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(new IssuedOperatorCredentialDto(credential.Id, credential.UsuarioId, credential.FormatoCodigo,
            token, credential.FragmentoVisible, Utc(credential.FechaEmision), OptionalUtc(credential.FechaVencimiento)));
    }

    public async Task<Result<OperatorSessionDto>> StartOperatorSessionAsync(
        ActorContext actor,
        StartOperatorSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CashOwnRead, cancellationToken);
        if (error is not null) return Result.Failure<OperatorSessionDto>(error);
        if (request is null || string.IsNullOrWhiteSpace(request.CredentialToken))
            return Result.Failure<OperatorSessionDto>(Validation("La credencial Code 128 es obligatoria."));

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.Serializable, cancellationToken);
        var shift = await repository.GetOpenShiftAsync(true, cancellationToken);
        if (shift is null) return Result.Failure<OperatorSessionDto>(ApplicationError.NotFound("un turno abierto"));
        if (shift.ModoOperacion != CashConstants.SharedMode)
            return Result.Failure<OperatorSessionDto>(ApplicationError.Conflict("Las sesiones rápidas solo están disponibles en turnos compartidos."));
        var credential = await repository.GetActiveCredentialForUserAsync(actor.UserId, cancellationToken);
        var now = clock.UtcNow;
        if (credential is null || credential.FormatoCodigo != "CODE128" ||
            (credential.FechaVencimiento is not null && credential.FechaVencimiento <= now.UtcDateTime) ||
            !passwordHasher.Verify(request.CredentialToken, credential.TokenHash))
            return Result.Failure<OperatorSessionDto>(ApplicationError.Unauthorized("La credencial de operador es inválida, está inactiva o venció."));
        if (await repository.GetActiveOperatorSessionForUpdateAsync(shift.TerminalId, cancellationToken) is not null)
            return Result.Failure<OperatorSessionDto>(ApplicationError.Conflict("La terminal ya tiene una sesión de operador activa."));

        var correlationId = Guid.CreateVersion7();
        var session = new SesionOperador
        {
            Id = Guid.CreateVersion7(), TurnoCajaId = shift.Id, TerminalId = shift.TerminalId,
            UsuarioId = actor.UserId, CredencialUsuarioId = credential.Id,
            FechaInicio = now.UtcDateTime, FechaUltimoUso = now.UtcDateTime,
            Estado = CashConstants.ActiveSessionState, CorrelacionId = correlationId
        };
        await repository.AddOperatorSessionAsync(session, cancellationToken);
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.OperatorSessionStarted,
            nameof(SesionOperador), session.Id, new { session.TurnoCajaId, session.TerminalId, credential.FragmentoVisible },
            correlationId, null, session.Id, session.TerminalId, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(session));
    }

    public async Task<Result<OperatorSessionDto>> CloseOperatorSessionAsync(
        ActorContext actor, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var error = await RequireAsync(actor, PermissionCodes.CashOwnRead, cancellationToken);
        if (error is not null) return Result.Failure<OperatorSessionDto>(error);
        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await repository.GetOperatorSessionForUpdateAsync(sessionId, cancellationToken);
        if (session is null) return Result.Failure<OperatorSessionDto>(ApplicationError.NotFound("la sesión de operador"));
        var canManage = await permissionChecker.HasPermissionAsync(actor.UserId, PermissionCodes.CashShiftManage, cancellationToken);
        if (session.UsuarioId != actor.UserId && !canManage)
            return Result.Failure<OperatorSessionDto>(ApplicationError.Forbidden());
        if (!session.IsActive) return Result.Failure<OperatorSessionDto>(ApplicationError.Conflict("La sesión de operador ya finalizó."));
        session.Close(clock.UtcNow, "MANUAL");
        var correlationId = Guid.CreateVersion7();
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, AuditAction.OperatorSessionClosed,
            nameof(SesionOperador), session.Id, new { session.Estado, session.MotivoCierre }, correlationId,
            null, session.Id, session.TerminalId, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(session));
    }

    private async Task<Result<CashMovementDto>> RegisterMovementAsync(
        ActorContext actor,
        CreateCashMovementRequest request,
        string type,
        string category,
        AuditAction action,
        CancellationToken cancellationToken)
    {
        var error = await RequireAsync(actor, PermissionCodes.CashMovementsManage, cancellationToken);
        if (error is not null) return Result.Failure<CashMovementDto>(error);
        if (request is null || !IsWholePositiveMoney(request.Amount))
            return Result.Failure<CashMovementDto>(Validation("El monto debe ser un valor entero de COP mayor que cero."));
        if (string.IsNullOrWhiteSpace(request.Concept) || request.Concept.Trim().Length > 500)
            return Result.Failure<CashMovementDto>(Validation("El concepto es obligatorio y admite hasta 500 caracteres."));

        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.Serializable, cancellationToken);
        var shift = await repository.GetOpenShiftAsync(true, cancellationToken);
        if (shift is null) return Result.Failure<CashMovementDto>(ApplicationError.NotFound("un turno abierto"));
        if (!shift.IsOpen) return Result.Failure<CashMovementDto>(ApplicationError.Conflict("No se pueden registrar movimientos en un turno cerrado."));
        SesionOperador? session = null;
        if (shift.ModoOperacion == CashConstants.IndividualMode)
        {
            if (request.OperatorSessionId is not null)
                return Result.Failure<CashMovementDto>(Validation("Un turno individual no utiliza sesión rápida de operador."));
            if (shift.UsuarioResponsableId != actor.UserId)
                return Result.Failure<CashMovementDto>(ApplicationError.Forbidden("Solo el responsable puede operar un turno individual."));
        }
        else
        {
            if (request.OperatorSessionId is not Guid sessionId)
                return Result.Failure<CashMovementDto>(ApplicationError.Conflict("El turno compartido requiere una sesión de operador activa."));
            session = await repository.GetOperatorSessionForUpdateAsync(sessionId, cancellationToken);
            if (session is not null && session.UsuarioId != actor.UserId)
                return Result.Failure<CashMovementDto>(ApplicationError.Forbidden("La sesión de operador no pertenece al usuario autenticado."));
            if (session is null || !session.IsActive || session.TurnoCajaId != shift.Id || session.TerminalId != shift.TerminalId || session.UsuarioId != actor.UserId)
                return Result.Failure<CashMovementDto>(ApplicationError.Conflict("La sesión de operador no está activa o no corresponde al turno, terminal y usuario."));
            session.FechaUltimoUso = clock.UtcNow.UtcDateTime;
        }

        var expectedAmounts = await repository.GetExpectedAmountsAsync(shift.Id, cancellationToken);
        var expectedValidation = ValidateExpectedMethods(expectedAmounts);
        if (expectedValidation is not null) return Result.Failure<CashMovementDto>(expectedValidation);
        var availableCash = CashMoney.Sum(expectedAmounts.Where(x => x.AffectsCash).Select(x => x.ExpectedAmount));
        if (type == CashConstants.ExpenseMovement)
        {
            if (request.Amount > availableCash)
                return Result.Failure<CashMovementDto>(ApplicationError.Conflict(
                    "El egreso o la sangría no puede superar el efectivo esperado disponible."));
        }
        var signedAmount = type == CashConstants.ExpenseMovement ? -request.Amount : request.Amount;
        CashMoney.Require(CashMoney.Add(availableCash, signedAmount), allowNegative: false);
        CashMoney.Add(CashMoney.Sum(expectedAmounts.Select(x => x.ExpectedAmount)), signedAmount);

        var correlationId = Guid.CreateVersion7();
        var movement = MovimientoCaja.Create(shift.Id, actor.UserId, session?.Id, type, category,
            request.Amount, request.Concept, correlationId, clock.UtcNow);
        await repository.AddMovementAsync(movement, cancellationToken);
        repository.MarkShiftChanged(shift);
        await AuditWriter.WriteCashAsync(auditRepository, clock, actor.UserId, action, nameof(MovimientoCaja), movement.Id,
            new { movement.Tipo, movement.CategoriaMovimiento, movement.Valor, movement.Concepto }, correlationId,
            null, session?.Id, shift.TerminalId, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(Map(movement));
    }

    private async Task<Result<Usuario>> ValidateAuthorizerAsync(
        ActorContext actor,
        CashCloseAuthorizationRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            return Result.Failure<Usuario>(ApplicationError.Forbidden("El cierre con diferencia requiere credenciales de autorización."));
        var user = await userRepository.GetByUserNameAsync(request.UserName, cancellationToken);
        if (user is null)
            return Result.Failure<Usuario>(ApplicationError.Forbidden("El autorizador debe ser otro usuario Supervisor o Administrador, activo y con permiso vigente."));

        user = await repository.GetUserForUpdateAsync(user.Id, cancellationToken) ?? user;
        if (!user.IsActive || user.Id == actor.UserId || user.HighestRoleLevel < RoleLevel.Supervisor)
            return Result.Failure<Usuario>(ApplicationError.Forbidden("El autorizador debe ser otro usuario Supervisor o Administrador, activo y con permiso vigente."));

        var now = clock.UtcNow;
        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            var wasLocked = user.RegisterFailedLogin(now);
            await userRepository.UpdateAsync(user, cancellationToken);
            if (wasLocked)
            {
                await AuditWriter.WriteAsync(auditRepository, clock, user.Id, AuditAction.UserLocked,
                    nameof(Usuario), user.Id,
                    new { user.UserName, FailedLoginAttempts = user.FailedLoginAttempts }, cancellationToken);
            }

            return Result.Failure<Usuario>(ApplicationError.Forbidden("El autorizador debe ser otro usuario Supervisor o Administrador, activo y con permiso vigente."));
        }

        if (!await permissionChecker.HasPermissionAsync(user.Id, PermissionCodes.CashShiftManage, cancellationToken))
            return Result.Failure<Usuario>(ApplicationError.Forbidden("El autorizador debe ser otro usuario Supervisor o Administrador, activo y con permiso vigente."));

        return Result.Success(user);
    }

    private async Task<ApplicationError?> RequireAsync(ActorContext actor, string permission, CancellationToken cancellationToken) =>
        await AuthorizationGuard.RequirePermissionAsync(permissionChecker, actor, permission, cancellationToken);

    private async Task<DateOnly> OperatingDateAsync(CancellationToken cancellationToken)
    {
        var establishment = await repository.GetEstablishmentAsync(false, cancellationToken);
        return establishment is null ? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime) : ToOperatingDate(clock.UtcNow, establishment.ZonaHoraria);
    }

    private static ApplicationError? ValidateExpectedMethods(IReadOnlyCollection<ExpectedPaymentMethodAmount> methods)
    {
        if (methods.Count == 0) return ApplicationError.Conflict("Debe configurar al menos un medio de pago antes del arqueo.");
        if (methods.Count(x => x.AffectsCash) != 1)
            return ApplicationError.Conflict("Debe existir exactamente un medio de pago activo o utilizado que afecte efectivo.");
        foreach (var method in methods) CashMoney.Require(method.ExpectedAmount, allowNegative: !method.AffectsCash);
        CashMoney.Sum(methods.Select(x => x.ExpectedAmount));
        return null;
    }

    private CashStateDto MapState(Caja? cash, TurnoCaja? shift, Establecimiento establishment)
    {
        var today = ToOperatingDate(clock.UtcNow, establishment.ZonaHoraria);
        var previous = shift is not null && shift.FechaOperativa < today;
        return new(cash is null ? null : Map(cash), shift is null ? null : Map(shift), establishment.ModoTurnoPredeterminado,
            establishment.Version, previous, previous ? "Existe un turno abierto de un día anterior. Debe arquearlo y cerrarlo antes de abrir otro." : null,
            cash is not null, shift is not null, false, null);
    }

    private static CashRegisterDto Map(Caja cash) => new(cash.Id, cash.Codigo, cash.Nombre, cash.Activo, cash.Version);

    private static CashShiftDto Map(TurnoCaja shift) => new(
        shift.Id, shift.CajaId, shift.TerminalId, shift.ModoOperacion, shift.UsuarioAperturaId,
        shift.UsuarioResponsableId, shift.UsuarioCierreId, shift.FechaOperativa, Utc(shift.FechaHoraApertura),
        OptionalUtc(shift.FechaHoraCierre), shift.MontoInicial, shift.EfectivoEsperado, shift.EfectivoContado,
        shift.Diferencia, shift.DiferenciaTotal, shift.Estado, shift.Observaciones, shift.ObservacionDiferencia, shift.MotivoDiferenciaId,
        shift.AutorizacionCierre?.AutorizacionId, shift.Version);

    private static CashMovementDto Map(MovimientoCaja movement) => new(
        movement.Id, movement.TurnoCajaId, movement.UsuarioId, movement.SesionOperadorId, movement.Tipo,
        movement.CategoriaMovimiento, movement.Valor, movement.Concepto, Utc(movement.FechaHora), movement.CorrelacionId);

    private static OperatorSessionDto Map(SesionOperador session) => new(
        session.Id, session.TurnoCajaId, session.TerminalId, session.UsuarioId, session.CredencialUsuarioId,
        Utc(session.FechaInicio), Utc(session.FechaUltimoUso), OptionalUtc(session.FechaFin), session.Estado,
        session.MotivoCierre, session.CorrelacionId);

    private static CashReconciliationDto MapReconciliation(
        TurnoCaja shift,
        IReadOnlyCollection<ExpectedPaymentMethodAmount> expected,
        IReadOnlyDictionary<Guid, decimal>? counted)
    {
        var methods = expected.Select(item =>
        {
            decimal? countedAmount = counted?.GetValueOrDefault(item.PaymentMethodId);
            return new PaymentMethodReconciliationDto(item.PaymentMethodId, item.Code, item.Name, item.AffectsCash,
                item.ExpectedAmount, countedAmount, countedAmount is null ? null : CashMoney.Subtract(countedAmount.Value, item.ExpectedAmount));
        }).ToArray();
        return new CashReconciliationDto(shift.Id, shift.Version, CashMoney.Sum(methods.Select(x => x.ExpectedAmount)),
            counted is null ? null : CashMoney.Sum(methods.Select(x => x.CountedAmount!.Value)),
            counted is null ? null : CashMoney.Sum(methods.Select(x => x.Difference!.Value)), methods,
            counted is null ? null : CashMoney.Sum(methods.Where(x => x.AffectsCash).Select(x => x.Difference!.Value)));
    }

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return null;
        var normalized = mode.Trim().ToUpperInvariant();
        return normalized is CashConstants.IndividualMode or CashConstants.SharedMode ? normalized : null;
    }

    private static DateOnly ToOperatingDate(DateTimeOffset utcNow, string timeZoneId)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);
    }

    private static bool IsWholePositiveMoney(decimal value) => value > 0 && CashMoney.IsValid(value, allowNegative: false);
    private static bool IsWholeNonNegativeMoney(decimal value) => CashMoney.IsValid(value, allowNegative: false);
    private static ApplicationError Validation(string message) => ApplicationError.Validation(message);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? OptionalUtc(DateTime? value) => value is null ? null : Utc(value.Value);
}
