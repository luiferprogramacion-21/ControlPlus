using Microsoft.AspNetCore.Diagnostics;

namespace ControlPlus.Api.Security;

/// <summary>
/// Only in the test environment (20 November 2026): an unexpected error answers 500 with its real cause, so a failing
/// test in CI says what broke. Registered last; anywhere else it does nothing and the usual generic 500 is returned.
/// </summary>
public sealed class TestingExceptionDetailHandler(IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (!environment.IsEnvironment("Testing")) return false;
        await Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "Error inesperado (solo pruebas).", detail: $"{exception.GetType().Name}: {exception.GetBaseException().Message}",
            instance: context.Request.Path).ExecuteAsync(context);
        return true;
    }
}
