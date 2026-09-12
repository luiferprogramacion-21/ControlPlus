using ControlPlus.Application.Common;

namespace ControlPlus.Application.Security.Services;

internal static class SecurityInput
{
    public static Result<string> RequiredText(string? value, string fieldName)
    {
        var normalized = value?.Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? Result.Failure<string>(ApplicationError.Validation($"El campo {fieldName} es obligatorio."))
            : Result.Success(normalized);
    }

    public static Result<string> RequiredPassword(string? value, string fieldName = "contraseña")
    {
        return string.IsNullOrWhiteSpace(value)
            ? Result.Failure<string>(ApplicationError.Validation($"La {fieldName} es obligatoria."))
            : Result.Success(value);
    }

    public static Result ValidatePage(int page, int pageSize)
    {
        if (page < 1)
        {
            return Result.Failure(ApplicationError.Validation("La página debe ser mayor o igual a 1."));
        }

        if (pageSize is < 1 or > 200)
        {
            return Result.Failure(ApplicationError.Validation("El tamaño de página debe estar entre 1 y 200."));
        }

        return Result.Success();
    }

    public static string NormalizeRoleCode(string code) => code.Trim().ToUpperInvariant();

    public static string NormalizePermissionCode(string code) => code.Trim().ToUpperInvariant();
}
