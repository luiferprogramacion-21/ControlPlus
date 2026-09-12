using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Application.Security.Services;

public sealed class AuditQueryService : IAuditQueryService
{
    private readonly IAuditRepository _auditRepository;
    private readonly IPermissionChecker _permissionChecker;

    public AuditQueryService(IAuditRepository auditRepository, IPermissionChecker permissionChecker)
    {
        _auditRepository = auditRepository;
        _permissionChecker = permissionChecker;
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
        return Result.Success(SecurityMappings.Map(records, SecurityMappings.ToDto));
    }
}
