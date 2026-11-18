using ControlPlus.Api.Security;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlus.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase(CurrentActorContextResolver actorContextResolver) : ControllerBase
{
    protected async Task<ActorContext?> GetActorAsync(CancellationToken cancellationToken) =>
        await actorContextResolver.ResolveAsync(User, cancellationToken);

    protected IActionResult FromResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return ToProblem(result.Error!);
    }

    protected IActionResult FromResult<T>(Result<T> result, Func<T, IActionResult> success)
    {
        if (result.IsSuccess)
        {
            return success(result.Value!);
        }

        return ToProblem(result.Error!);
    }

    protected IActionResult MissingActor() => Unauthorized(new ProblemDetails
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "No se pudo resolver la sesión actual.",
        Type = "https://httpstatuses.com/401"
    });

    private IActionResult ToProblem(ApplicationError error)
    {
        if (error.Code.StartsWith("validation.", StringComparison.Ordinal))
        {
            var validationDetails = new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["request"] = [error.Message]
                })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Uno o más campos de la solicitud son inválidos.",
                Type = $"https://controlplus.local/problems/{error.Code}"
            };
            validationDetails.Extensions["code"] = error.Code;

            return ValidationProblem(validationDetails);
        }

        var statusCode = error.Code switch
        {
            "resource.not_found" => StatusCodes.Status404NotFound,
            "resource.conflict" or "concurrency.conflict" or "numbering.exhausted" or
                "authentication.bootstrap_completed" => StatusCodes.Status409Conflict,
            "authorization.forbidden" or "authorization.insufficient_role_level" => StatusCodes.Status403Forbidden,
            "authentication.invalid_credentials" or "authentication.unauthorized" => StatusCodes.Status401Unauthorized,
            "security.bootstrap_role_missing" or "email.not_configured" or "email.delivery_failed" =>
                StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };

        return Problem(
            statusCode: statusCode,
            title: error.Message,
            type: $"https://controlplus.local/problems/{error.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error.Code
            });
    }
}
