using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Api.Security;

/// <summary>
/// Converts persistence conflicts into a stable API contract without exposing database details.
/// </summary>
public sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException)
        {
            return false;
        }

        await Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "El registro fue modificado por otra operación. Actualice la información e inténtelo de nuevo.",
                type: "https://controlplus.local/problems/concurrency-conflict",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "concurrency.conflict"
                })
            .ExecuteAsync(httpContext);

        return true;
    }
}
