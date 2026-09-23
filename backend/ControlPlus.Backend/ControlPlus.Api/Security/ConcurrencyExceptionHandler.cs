using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        var conflict = Classify(exception);
        if (conflict is null)
        {
            return false;
        }

        await Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: conflict.Value.Title,
                type: $"https://controlplus.local/problems/{conflict.Value.Code.Replace('.', '-')}",
                instance: httpContext.Request.Path,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = conflict.Value.Code
                })
            .ExecuteAsync(httpContext);

        return true;
    }

    private static (string Code, string Title)? Classify(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict();
        }

        var postgresException = FindPostgresException(exception);
        if (postgresException is null)
        {
            return null;
        }

        return postgresException.SqlState switch
        {
            // Transaction serialization and deadlock failures are safe to retry from a fresh representation.
            "40001" or "40P01" => ConcurrencyConflict(),

            // Constraint and trigger rejections describe only a stable resource-state conflict here.
            // PostgreSQL messages, table names and constraint names are deliberately not returned.
            "23503" or "23505" or "23514" or "23P01" or "P0001" =>
                ("resource.conflict", "La operación entra en conflicto con el estado actual del recurso."),
            _ => null
        };
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }

    private static (string Code, string Title) ConcurrencyConflict() =>
        ("concurrency.conflict", "El registro fue modificado por otra operación. Actualice la información e inténtelo de nuevo.");
}
