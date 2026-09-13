using System.ComponentModel.DataAnnotations;

namespace ControlPlus.Application.Security.Contracts;

public sealed record SetupFirstAdministratorRequest(
    [Required, MaxLength(200)] string EstablishmentName,
    [Required, MaxLength(30)] string EstablishmentIdentification,
    [Required, MaxLength(50)] string InstallationCode,
    [Required, MaxLength(10)] string InstallationSerial,
    [Required, MaxLength(50)] string TerminalCode,
    [Required, MaxLength(100)] string TerminalName,
    [Required, MaxLength(100)] string UserName,
    [Required, MaxLength(200)] string DisplayName,
    [Required] string Password);

public sealed record LoginRequest(string UserName, string Password);

public sealed record RecoverInitialAdministratorRequest(
    [Required, MaxLength(100)] string UserName,
    [Required] string NewPassword);

public sealed record AuthenticationResult(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    AuthenticatedUserDto User);

public sealed record AuthenticatedUserDto(
    Guid Id,
    string UserName,
    string DisplayName,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
