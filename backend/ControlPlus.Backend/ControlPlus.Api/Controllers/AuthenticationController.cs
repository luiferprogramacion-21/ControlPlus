using ControlPlus.Api.Security;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Infrastructure.Security;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ControlPlus.Api.Controllers;

[Route("api/auth")]
public sealed class AuthenticationController(
    CurrentActorContextResolver actorContextResolver,
    IAuthenticationService authenticationService,
    IUserRepository userRepository,
    BootstrapAccessValidator bootstrapAccessValidator,
    SecurityCatalogSeeder securityCatalogSeeder,
    InstallationBootstrapper installationBootstrapper,
    OfficialControlPlusDbContext dbContext) : ApiControllerBase(actorContextResolver)
{
    [HttpPost("setup")]
    [AllowAnonymous]
    [EnableRateLimiting("installation")]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SetupFirstAdministrator(
        [FromBody] SetupFirstAdministratorRequest request,
        CancellationToken cancellationToken)
    {
        if (!bootstrapAccessValidator.IsConfigured)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "La configuración inicial requiere Installation:MasterKey.",
                type: "https://controlplus.local/problems/bootstrap-not-configured");
        }

        if (!bootstrapAccessValidator.IsValid(Request.Headers["X-ControlPlus-Master-Key"].ToString()))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "No está autorizado para configurar la instalación.",
                type: "https://controlplus.local/problems/bootstrap-forbidden");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(2026091201)",
            [],
            cancellationToken);

        if (await userRepository.HasAnyUsersAsync(cancellationToken))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "El administrador inicial ya fue configurado.",
                type: "https://controlplus.local/problems/bootstrap-completed");
        }

        await installationBootstrapper.EnsureCreatedAsync(request, cancellationToken);
        await securityCatalogSeeder.EnsureSeededAsync(cancellationToken);

        var result = await authenticationService.SetupFirstAdministratorAsync(request, cancellationToken);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return FromResult(result, user => CreatedAtAction(nameof(Me), user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType<AuthenticationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(request, cancellationToken);
        return FromResult(result, Ok);
    }

    [HttpPost("recover-initial-administrator")]
    [AllowAnonymous]
    [EnableRateLimiting("installation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RecoverInitialAdministrator(
        [FromBody] RecoverInitialAdministratorRequest request,
        CancellationToken cancellationToken)
    {
        if (!bootstrapAccessValidator.IsConfigured)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "La recuperación requiere Installation:MasterKey.",
                type: "https://controlplus.local/problems/bootstrap-not-configured");
        }

        if (!bootstrapAccessValidator.IsValid(Request.Headers["X-ControlPlus-Master-Key"].ToString()))
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "No está autorizado para recuperar el Administrador inicial.",
                type: "https://controlplus.local/problems/bootstrap-forbidden");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(2026091202)",
            [],
            cancellationToken);

        var result = await authenticationService.RecoverInitialAdministratorAsync(request, cancellationToken);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return FromResult(result);
    }

    /// <summary>
    /// Renueva la sesión vigente: devuelve un token nuevo con la duración configurada. La pantalla la llama mientras
    /// el usuario trabaja, para que una venta en curso no se pierda al vencer el token. No se audita (no es una acción
    /// de negocio); una sesión revocada, un usuario bloqueado o inactivo recibe 401 como cualquier otra solicitud.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthenticationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        return FromResult(await authenticationService.RenewAsync(actor.UserId, cancellationToken), Ok);
    }

    [HttpGet("me")]
    [ProducesResponseType<AuthenticatedUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null)
        {
            return MissingActor();
        }

        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null)
        {
            return MissingActor();
        }

        return Ok(new AuthenticatedUserDto(
            snapshot.UserId,
            snapshot.UserName,
            snapshot.DisplayName,
            snapshot.RoleCodes,
            snapshot.PermissionCodes));
    }
}
