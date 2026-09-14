namespace ControlPlus.Application.Common;

/// <summary>
/// Error returned by an application use case. It deliberately contains no HTTP concern.
/// </summary>
public sealed record ApplicationError(string Code, string Message)
{
    public static ApplicationError Validation(string message) => new("validation.failed", message);

    public static ApplicationError NotFound(string resource) => new("resource.not_found", $"No se encontró {resource}.");

    public static ApplicationError Conflict(string message) => new("resource.conflict", message);

    public static ApplicationError ConcurrencyConflict(string message) => new("concurrency.conflict", message);

    public static ApplicationError Forbidden(string message = "No tiene permiso para realizar esta acción.") =>
        new("authorization.forbidden", message);

    public static ApplicationError Unauthorized(string message = "No está autenticado.") =>
        new("authentication.unauthorized", message);
}
