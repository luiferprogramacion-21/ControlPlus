using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Application.Security.Services;

public sealed class AuditQueryService : IAuditQueryService
{
    private readonly IAuditRepository _auditRepository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAuditUserDirectory _userDirectory;

    public AuditQueryService(IAuditRepository auditRepository, IPermissionChecker permissionChecker,
        IAuditUserDirectory userDirectory)
    {
        _auditRepository = auditRepository;
        _permissionChecker = permissionChecker;
        _userDirectory = userDirectory;
    }

    public async Task<Result<PagedResult<AuditRecordDto>>> QueryAsync(
        ActorContext actor,
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        var authorizationError = await AuthorizationGuard.RequirePermissionAsync(
            _permissionChecker,
            actor,
            PermissionCodes.AuditRead,
            cancellationToken);
        if (authorizationError is not null)
        {
            return Result.Failure<PagedResult<AuditRecordDto>>(authorizationError);
        }

        var pageValidation = SecurityInput.ValidatePage(query.Page, query.PageSize);
        if (pageValidation.IsFailure)
        {
            return Result.Failure<PagedResult<AuditRecordDto>>(pageValidation.Error!);
        }

        if ((query.FromUtc is { } fromUtcOffset && fromUtcOffset.Offset != TimeSpan.Zero) ||
            (query.ToUtc is { } toUtcOffset && toUtcOffset.Offset != TimeSpan.Zero))
        {
            return Result.Failure<PagedResult<AuditRecordDto>>(
                ApplicationError.Validation("Las fechas de auditoría deben estar en UTC."));
        }

        if (query.FromUtc is { } fromUtc && query.ToUtc is { } toUtc && fromUtc > toUtc)
        {
            return Result.Failure<PagedResult<AuditRecordDto>>(
                ApplicationError.Validation("La fecha inicial no puede ser posterior a la fecha final."));
        }

        var records = await _auditRepository.QueryAsync(query, cancellationToken);
        var page = SecurityMappings.Map(records, SecurityMappings.ToDto);
        // Nombres de usuario (no datos personales adicionales) para que la pantalla muestre quién actuó y quién autorizó.
        var userIds = page.Items.SelectMany(x => new[] { x.ActorUserId, x.AuthorizerUserId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
        if (userIds.Length == 0) return Result.Success(page);
        var names = await _userDirectory.GetUserNamesAsync(userIds, cancellationToken);
        var items = page.Items.Select(x => x with
        {
            ActorUserName = x.ActorUserId is { } actor && names.TryGetValue(actor, out var actorName) ? actorName : null,
            AuthorizerUserName = x.AuthorizerUserId is { } authorizer && names.TryGetValue(authorizer, out var authorizerName) ? authorizerName : null
        }).ToArray();
        return Result.Success(page with { Items = items });
    }
}
