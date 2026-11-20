using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Sales.Services;

public sealed partial class SalesService
{
    /// <summary>
    /// Checks, at the moment a Cajero applies a discount or picks the wholesale price, that the credentials belong to an
    /// active user of a strictly higher role with SALES.DISCOUNT_AUTHORIZE. Nothing is recorded and the credentials are not
    /// kept: the sale confirmation validates them again and registers the authorization. A wrong password counts as a failed
    /// attempt exactly as in the sale.
    /// </summary>
    public async Task<Result<ConcessionAuthorizationDto>> VerifyConcessionAuthorizationAsync(ActorContext actor,
        VerifyConcessionAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var credentials = request is null ? null : new SaleCreditAuthorizationRequest(request.UserName, request.Password, request.CredentialToken);
        if (request is null || credentials is null || !credentials.IsComplete() || request.Kind is not ("DISCOUNT" or "WHOLESALE"))
            return Result.Failure<ConcessionAuthorizationDto>(Validation(
                "Indique usuario y contraseña del autorizador, o la credencial escaneada, y el tipo (DISCOUNT o WHOLESALE)."));

        return await loginAttemptCoordinator.ExecuteAsync(credentials.IdentityKey,
            token => VerifyConcessionWithinTransactionAsync(actor, credentials, token), cancellationToken);
    }

    private async Task<Result<ConcessionAuthorizationDto>> VerifyConcessionWithinTransactionAsync(
        ActorContext actor, SaleCreditAuthorizationRequest credentials, CancellationToken cancellationToken)
    {
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null)
            return Result.Failure<ConcessionAuthorizationDto>(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<ConcessionAuthorizationDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.SalesCreate, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<ConcessionAuthorizationDto>(ApplicationError.Forbidden());
        if (snapshot.HighestRoleLevel != RoleLevel.Cajero)
            return Result.Failure<ConcessionAuthorizationDto>(Validation(
                "Su rol no necesita autorización para aplicar descuentos ni precio mayorista."));
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, cancellationToken);
        if (terminal is null) return Result.Failure<ConcessionAuthorizationDto>(ApplicationError.NotFound("la terminal activa"));

        var (authorizer, _) = await VerifyAuthorizerAsync(credentials, actor.UserId, snapshot.HighestRoleLevel,
            terminal.Id, false, cancellationToken);
        if (authorizer is null) return Result.Failure<ConcessionAuthorizationDto>(ApplicationError.Forbidden());
        if (!await permissionChecker.HasPermissionAsync(authorizer.Id, PermissionCodes.SalesDiscountAuthorize, cancellationToken))
            return Result.Failure<ConcessionAuthorizationDto>(ApplicationError.Forbidden());

        return Result.Success(new ConcessionAuthorizationDto(authorizer.NombreCompleto));
    }

    /// <summary>
    /// Finds and verifies the second user of an authorization, by user name and password or by the scanned Code 128 credential
    /// (random token of at least 46 bits checked against its hash, so no lockout counter: an unknown token only leaves a rejection event). Requires a strictly higher role
    /// than the requester; the caller still checks the permission it needs. Returns a null authorizer when it must answer 403;
    /// a failed secret is recorded (and saved) here.
    /// </summary>
    private async Task<(Usuario? Authorizer, Guid? CredentialId)> VerifyAuthorizerAsync(
        SaleCreditAuthorizationRequest credentials, Guid actorId, RoleLevel actorLevel, Guid terminalId, bool forCredit,
        CancellationToken cancellationToken)
    {
        Usuario? authorizer;
        Guid? credentialId = null;
        if (credentials.UsesCredential())
        {
            var matched = await credentialResolver.ResolveAsync(credentials.CredentialToken!, cancellationToken);
            if (matched is null)
            {
                await WriteAuthorizerFailureAsync(forCredit, actorId, terminalId, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return (null, null);
            }
            var matchedUser = await userRepository.GetByUserNameAsync(matched.UserName, cancellationToken);
            authorizer = matchedUser is null ? null : await repository.GetUserForUpdateAsync(matchedUser.Id, cancellationToken);
            credentialId = matched.CredentialId;
        }
        else
        {
            var candidate = await userRepository.GetByUserNameAsync(credentials.UserName!.Trim(), cancellationToken);
            if (candidate is null) return (null, null);
            authorizer = await repository.GetUserForUpdateAsync(candidate.Id, cancellationToken);
        }

        if (authorizer is null || !authorizer.IsActive || authorizer.Id == actorId ||
            !RoleHierarchy.IsHigherThan(authorizer.HighestRoleLevel, actorLevel))
            return (null, null);

        if (!credentials.UsesCredential() && !passwordHasher.Verify(credentials.Password!, authorizer.PasswordHash))
        {
            var locked = authorizer.RegisterFailedLogin(clock.UtcNow);
            await userRepository.UpdateAsync(authorizer, cancellationToken);
            if (locked)
                await AuditWriter.WriteAsync(auditRepository, clock, authorizer.Id, AuditAction.UserLocked,
                    nameof(Usuario), authorizer.Id, new { authorizer.Id, authorizer.FailedLoginAttempts }, cancellationToken);
            await WriteAuthorizerFailureAsync(forCredit, actorId, terminalId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return (null, null);
        }

        return (authorizer, credentialId);
    }

    private Task WriteAuthorizerFailureAsync(bool forCredit, Guid actorId, Guid terminalId, CancellationToken cancellationToken) =>
        forCredit
            ? AuditWriter.WriteCreditAuthorizationFailureAsync(auditRepository, clock, actorId, Guid.CreateVersion7(), terminalId, cancellationToken)
            : AuditWriter.WriteSaleDiscountAuthorizationFailureAsync(auditRepository, clock, actorId, Guid.CreateVersion7(), terminalId, cancellationToken);

    /// <summary>
    /// Turns a scanned credential into the authorizer's user name before the usual flow runs, so cancellation and change reuse
    /// their own checks (higher role, permission, audit). An unknown credential becomes a user name that does not exist.
    /// </summary>
    private async Task<(SaleCreditAuthorizationRequest? Authorization, ApplicationError? Error)> ResolveScannedAuthorizationAsync(
        SaleCreditAuthorizationRequest? authorization, CancellationToken cancellationToken)
    {
        if (authorization is null || !authorization.UsesCredential()) return (authorization, null);
        if (!string.IsNullOrWhiteSpace(authorization.UserName) || !string.IsNullOrWhiteSpace(authorization.Password))
            return (null, Validation("Envíe la credencial escaneada o usuario y contraseña, no ambos."));
        var match = await credentialResolver.ResolveAsync(authorization.CredentialToken!, cancellationToken);
        return (authorization with
        {
            UserName = match?.UserName ?? AuthorizerCredentialResolver.UnknownUserName,
            ResolvedCredentialId = match?.CredentialId ?? Guid.Empty
        }, null);
    }
}
