using ControlPlus.Api.Authorization;
using ControlPlus.Api.Security;
using ControlPlus.Application.Backups;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

/// <summary>Soporte y continuidad: respaldos de Fase 4. Solo Administrador con BACKUPS.MANAGE efectivo.</summary>
[Route("api/backups")]
public sealed class BackupsController(CurrentActorContextResolver actorContextResolver, IBackupService backups)
    : ApiControllerBase(actorContextResolver)
{
    /// <summary>Último respaldo, copias por destino, espacio libre y eventos recientes.</summary>
    [HttpGet("status")]
    [PermissionAuthorize(PermissionCodes.BackupsManage)]
    [ProducesResponseType<BackupStatusDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await backups.GetStatusAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Pide un respaldo manual; se ejecuta en segundo plano y su resultado aparece en el estado.</summary>
    [HttpPost("run")]
    [PermissionAuthorize(PermissionCodes.BackupsManage)]
    [ProducesResponseType<BackupRunRequested>(StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor()
            : FromResult(await backups.RequestManualAsync(actor, cancellationToken), value => Accepted(value));
    }

    /// <summary>Metas de RPO/RTO, última prueba de restauración y copias disponibles (maqueta 22c).</summary>
    [HttpGet("restore")]
    [PermissionAuthorize(PermissionCodes.BackupsManage)]
    [ProducesResponseType<RestoreOverviewDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetRestore(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await backups.GetRestoreOverviewAsync(actor, cancellationToken), Ok);
    }

    /// <summary>Prueba restaurar la copia en una base temporal que se borra al terminar; nunca toca la base real.</summary>
    [HttpPost("copies/{copyId:guid}/restore-test")]
    [PermissionAuthorize(PermissionCodes.BackupsManage)]
    [ProducesResponseType<RestoreTestDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> TestRestore(Guid copyId, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor() : FromResult(await backups.TestRestoreAsync(actor, copyId, cancellationToken), Ok);
    }

    /// <summary>Cambia las metas de respaldo y recuperación (pérdida máxima, tiempo para recuperar y días entre pruebas).</summary>
    [HttpPut("restore/goals")]
    [PermissionAuthorize(PermissionCodes.BackupsManage)]
    [Consumes("application/json")]
    [ProducesResponseType<RestoreOverviewDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> UpdateRestoreGoals([FromBody] UpdateRestoreGoalsRequest request, CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor()
            : FromResult(await backups.UpdateRestoreGoalsAsync(actor, request, cancellationToken), Ok);
    }

    /// <summary>Configura la carpeta de un destino (LOCAL_FISICO_SEPARADO o EXTERNO_CIFRADO) y si está activo.</summary>
    [HttpPut("destinations/{type}")]
    [PermissionAuthorize(PermissionCodes.BackupsManage)]
    [Consumes("application/json")]
    [ProducesResponseType<BackupDestinationDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> UpdateDestination(string type, [FromBody] UpdateBackupDestinationRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        return actor is null ? MissingActor()
            : FromResult(await backups.UpdateDestinationAsync(actor, type, request, cancellationToken), Ok);
    }
}
