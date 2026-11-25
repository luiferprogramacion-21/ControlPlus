using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Ports;

namespace ControlPlus.Application.Security.Services;

internal static partial class SecurityInput
{
    public static Result<string> RequiredText(string? value, string fieldName)
    {
        var normalized = value?.Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? Result.Failure<string>(ApplicationError.Validation($"El campo {fieldName} es obligatorio."))
            : Result.Success(normalized);
    }

    /// <summary>Correo opcional: vacío es «sin correo»; si se da, una dirección simple de hasta 254 caracteres.</summary>
    public static Result<string?> OptionalEmail(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return Result.Success<string?>(null);
        return normalized.Length <= 254 && EmailPattern().IsMatch(normalized)
            ? Result.Success<string?>(normalized)
            : Result.Failure<string?>(ApplicationError.Validation("El correo no es válido."));
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        1000)]
    private static partial System.Text.RegularExpressions.Regex EmailPattern();

    /// <summary>Contraseña nueva: obligatoria y, con la regla de 4 números, exactamente 4 dígitos.</summary>
    public static Result<string> NewPassword(string? value, IPasswordPolicy policy, string fieldName = "contraseña")
    {
        var required = RequiredPassword(value, fieldName);
        if (required.IsFailure) return required;
        return policy.IsCompliant(required.Value!)
            ? required
            : Result.Failure<string>(ApplicationError.Validation(
                $"La {fieldName} debe tener exactamente {PasswordPolicy.PinLength} números."));
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
