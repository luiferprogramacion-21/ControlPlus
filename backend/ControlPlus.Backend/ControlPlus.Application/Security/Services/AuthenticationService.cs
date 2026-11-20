using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using User = ControlPlus.Domain.OfficialModel.Usuario;

namespace ControlPlus.Application.Security.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenIssuer _tokenIssuer;
    private readonly IAuditRepository _auditRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IPasswordPolicy _passwordPolicy;
    private readonly ILoginAttemptCoordinator _loginAttemptCoordinator;

    public AuthenticationService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        ITokenIssuer tokenIssuer,
        IAuditRepository auditRepository,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILoginAttemptCoordinator loginAttemptCoordinator,
        IPasswordPolicy? passwordPolicy = null)
    {
        _passwordPolicy = passwordPolicy ?? PasswordPolicy.Free;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
        _auditRepository = auditRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _loginAttemptCoordinator = loginAttemptCoordinator;
    }

    public async Task<Result<UserDto>> SetupFirstAdministratorAsync(
        SetupFirstAdministratorRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userName = SecurityInput.RequiredText(request.UserName, "nombre de usuario");
        if (userName.IsFailure)
        {
            return Result.Failure<UserDto>(userName.Error!);
        }

        var displayName = SecurityInput.RequiredText(request.DisplayName, "nombre visible");
        if (displayName.IsFailure)
        {
            return Result.Failure<UserDto>(displayName.Error!);
        }

        var password = SecurityInput.NewPassword(request.Password, _passwordPolicy);
        if (password.IsFailure)
        {
            return Result.Failure<UserDto>(password.Error!);
        }

        if (await _userRepository.HasAnyUsersAsync(cancellationToken))
        {
            return Result.Failure<UserDto>(SecurityErrors.InitialAdministratorAlreadyConfigured);
        }

        var administratorRole = await _roleRepository.GetByCodeAsync(RoleCodes.Administrator, cancellationToken);
        if (administratorRole is null ||
            !administratorRole.IsActive ||
            administratorRole.Level != RoleLevel.Administrador)
        {
            return Result.Failure<UserDto>(SecurityErrors.AdministratorRoleMissing);
        }

        var now = _clock.UtcNow;
        var user = User.Create(userName.Value!, displayName.Value!, _passwordHasher.Hash(password.Value!), now);
        user.AddRole(administratorRole, now);

        await _userRepository.AddAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actorUserId: null,
            AuditAction.UserCreated,
            nameof(User),
            user.Id,
            new { user.UserName, user.DisplayName, Bootstrap = true, Roles = new[] { administratorRole.Code } },
            cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actorUserId: null,
            AuditAction.RoleAssigned,
            nameof(User),
            user.Id,
            new { RoleId = administratorRole.Id, administratorRole.Code, Bootstrap = true },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(SecurityMappings.ToDto(user));
    }

    public async Task<Result<AuthenticationResult>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userName = SecurityInput.RequiredText(request.UserName, "nombre de usuario");
        var password = SecurityInput.RequiredPassword(request.Password);
        if (userName.IsFailure || password.IsFailure)
        {
            // A login response intentionally does not reveal which credential was malformed.
            return Result.Failure<AuthenticationResult>(SecurityErrors.InvalidCredentials);
        }

        return await _loginAttemptCoordinator.ExecuteAsync(
            userName.Value!.ToUpperInvariant(),
            attemptCancellationToken => LoginWithinAttemptLockAsync(
                userName.Value!,
                password.Value!,
                attemptCancellationToken),
            cancellationToken);
    }

    private async Task<Result<AuthenticationResult>> LoginWithinAttemptLockAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var user = await _userRepository.GetByUserNameAsync(userName, cancellationToken);
        if (user is null)
        {
            await AuditWriter.WriteAsync(
                _auditRepository,
                _clock,
                actorUserId: null,
                AuditAction.LoginFailed,
                nameof(User),
                entityId: null,
                new { UserName = userName, Reason = "invalid_credentials" },
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthenticationResult>(SecurityErrors.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            await AuditWriter.WriteAsync(
                _auditRepository,
                _clock,
                user.Id,
                AuditAction.LoginFailed,
                nameof(User),
                user.Id,
                new { user.UserName, Reason = user.IsLocked ? "locked" : "inactive" },
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthenticationResult>(SecurityErrors.InvalidCredentials);
        }

        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            var wasLocked = user.RegisterFailedLogin(now);
            await _userRepository.UpdateAsync(user, cancellationToken);
            await AuditWriter.WriteAsync(
                _auditRepository,
                _clock,
                user.Id,
                AuditAction.LoginFailed,
                nameof(User),
                user.Id,
                new { user.UserName, Reason = "invalid_credentials", Attempt = user.FailedLoginAttempts },
                cancellationToken);

            if (wasLocked)
            {
                await AuditWriter.WriteAsync(
                    _auditRepository,
                    _clock,
                    user.Id,
                    AuditAction.UserLocked,
                    nameof(User),
                    user.Id,
                    new { user.UserName, FailedLoginAttempts = user.FailedLoginAttempts },
                    cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthenticationResult>(SecurityErrors.InvalidCredentials);
        }

        user.RegisterSuccessfulLogin(now);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            user.Id,
            AuditAction.LoginSucceeded,
            nameof(User),
            user.Id,
            new { user.UserName },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Contraseña de 4 números: quien entra con una anterior debe cambiarla antes de usar el sistema.
        return await IssueAsync(user.Id, !_passwordPolicy.IsCompliant(password), cancellationToken);
    }

    public async Task<Result<AuthenticationResult>> RenewAsync(Guid userId, bool passwordChangeRequired = false,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Result.Failure<AuthenticationResult>(ApplicationError.Unauthorized());
        return await IssueAsync(userId, passwordChangeRequired, cancellationToken);
    }

    public async Task<Result<AuthenticationResult>> ChangeRequiredPasswordAsync(Guid userId,
        ChangeRequiredPasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (userId == Guid.Empty) return Result.Failure<AuthenticationResult>(ApplicationError.Unauthorized());
        var current = SecurityInput.RequiredPassword(request.CurrentPassword, "contraseña actual");
        if (current.IsFailure) return Result.Failure<AuthenticationResult>(current.Error!);
        var newPassword = SecurityInput.NewPassword(request.NewPassword, _passwordPolicy, "nueva contraseña");
        if (newPassword.IsFailure) return Result.Failure<AuthenticationResult>(newPassword.Error!);
        if (string.Equals(current.Value, newPassword.Value, StringComparison.Ordinal))
            return Result.Failure<AuthenticationResult>(ApplicationError.Validation("La nueva contraseña debe ser distinta de la actual."));

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive) return Result.Failure<AuthenticationResult>(ApplicationError.Unauthorized());
        if (!_passwordHasher.Verify(current.Value!, user.PasswordHash))
            return Result.Failure<AuthenticationResult>(SecurityErrors.IncorrectCurrentPassword);

        var now = _clock.UtcNow;
        user.ChangePasswordHash(_passwordHasher.Hash(newPassword.Value!), now);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(_auditRepository, _clock, user.Id, AuditAction.PasswordChanged, nameof(User), user.Id,
            new { user.UserName, ChangedBySelf = true, Reason = "formato_4_numeros" }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await IssueAsync(user.Id, false, cancellationToken);
    }

    private async Task<Result<AuthenticationResult>> IssueAsync(Guid userId, bool passwordChangeRequired,
        CancellationToken cancellationToken)
    {
        var authorization = await _userRepository.GetAuthorizationSnapshotAsync(userId, cancellationToken);
        if (authorization is null || authorization.HighestRoleLevel == RoleLevel.None)
        {
            return Result.Failure<AuthenticationResult>(ApplicationError.Forbidden(
                "El usuario no tiene un rol activo para iniciar sesión."));
        }

        var token = _tokenIssuer.Issue(new TokenSubject(
            authorization.UserId,
            authorization.UserName,
            authorization.DisplayName,
            authorization.SecurityStamp,
            passwordChangeRequired));

        return Result.Success(new AuthenticationResult(
            token.AccessToken,
            token.ExpiresAtUtc,
            new AuthenticatedUserDto(
                authorization.UserId,
                authorization.UserName,
                authorization.DisplayName,
                authorization.RoleCodes,
            authorization.PermissionCodes),
            passwordChangeRequired));
    }

    public async Task<Result> RecoverInitialAdministratorAsync(
        RecoverInitialAdministratorRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userName = SecurityInput.RequiredText(request.UserName, "nombre de usuario");
        if (userName.IsFailure)
        {
            return Result.Failure(userName.Error!);
        }

        var newPassword = SecurityInput.NewPassword(request.NewPassword, _passwordPolicy, "nueva contraseña");
        if (newPassword.IsFailure)
        {
            return Result.Failure(newPassword.Error!);
        }

        if (await _userRepository.HasAvailableAdministratorAsync(cancellationToken))
        {
            return Result.Failure(SecurityErrors.AdministratorRecoveryUnavailable);
        }

        var user = await _userRepository.GetByUserNameAsync(userName.Value!, cancellationToken);
        if (user is null ||
            user.UserRoles.SingleOrDefault()?.Role is not { IsActive: true } role ||
            role.Code != RoleCodes.Administrator)
        {
            return Result.Failure(SecurityErrors.InitialAdministratorRecoveryTargetInvalid);
        }

        var now = _clock.UtcNow;
        user.RecoverInitialAdministrator(_passwordHasher.Hash(newPassword.Value!), now);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await AuditWriter.WriteAsync(
            _auditRepository,
            _clock,
            actorUserId: null,
            AuditAction.InitialAdministratorRecovered,
            nameof(User),
            user.Id,
            new
            {
                user.UserName,
                RecoveryMethod = "installation_master_key",
                PasswordReset = true,
                LockCleared = true,
                SessionsInvalidated = true
            },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
