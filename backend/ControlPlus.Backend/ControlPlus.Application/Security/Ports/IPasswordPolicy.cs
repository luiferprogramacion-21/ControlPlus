namespace ControlPlus.Application.Security.Ports;

/// <summary>
/// Contraseñas de 4 números (decisión del 19 de noviembre de 2026, para todos los roles). Se activa con
/// <c>Security:CredentialFormat = PIN4</c> (el servicio instalado lo trae en appsettings.Production.json). Las contraseñas
/// anteriores siguen sirviendo para entrar, pero la sesión obliga a cambiarlas antes de usar el sistema.
/// </summary>
public interface IPasswordPolicy
{
    bool RequiresPin { get; }

    /// <summary>True si la contraseña cumple el formato vigente (sin la regla, cualquiera no vacía).</summary>
    bool IsCompliant(string password);
}

public sealed class PasswordPolicy(bool requiresPin) : IPasswordPolicy
{
    public const string PinFormat = "PIN4";
    public const int PinLength = 4;

    /// <summary>Sin regla: la que usan las pruebas y el desarrollo.</summary>
    public static readonly PasswordPolicy Free = new(false);

    public bool RequiresPin { get; } = requiresPin;

    public bool IsCompliant(string password) =>
        !string.IsNullOrWhiteSpace(password) &&
        (!RequiresPin || (password.Length == PinLength && password.All(char.IsAsciiDigit)));
}
