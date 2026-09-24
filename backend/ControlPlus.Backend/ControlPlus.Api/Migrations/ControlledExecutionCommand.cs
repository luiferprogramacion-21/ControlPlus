namespace ControlPlus.Api.Migrations;

public enum ControlledExecutionMode
{
    Api,
    HealthCheck,
    Migration,
    Preflight
}

public sealed record ControlledExecutionCommand(
    bool IsValid,
    ControlledExecutionMode Mode,
    string? TargetMigration,
    string Message)
{
    public const string EnabledEnvironmentVariable = "CONTROLPLUS_MIGRATIONS_ENABLED";
    public const string HealthCheckArgument = "--health-check";
    public const string MigrationTargetArgument = "--migrate-to";
    public const string PreflightTargetArgument = "--preflight-to";

    private const string InvalidArgumentsMessage =
        "Command line rejected: expected no arguments, --health-check, " +
        "--migrate-to <MigrationId>, or --preflight-to <MigrationId>.";

    public static ControlledExecutionCommand Parse(
        IReadOnlyList<string> arguments,
        string? enabledValue)
    {
        if (arguments.Count == 0)
        {
            return Valid(ControlledExecutionMode.Api);
        }

        if (arguments.Count == 1 &&
            string.Equals(arguments[0], HealthCheckArgument, StringComparison.Ordinal))
        {
            return Valid(ControlledExecutionMode.HealthCheck);
        }

        if (arguments.Count != 2)
        {
            return Rejected(InvalidArgumentsMessage);
        }

        var mode = arguments[0] switch
        {
            MigrationTargetArgument => ControlledExecutionMode.Migration,
            PreflightTargetArgument => ControlledExecutionMode.Preflight,
            _ => (ControlledExecutionMode?)null
        };
        if (mode is null)
        {
            return Rejected(InvalidArgumentsMessage);
        }

        if (!IsValidMigrationId(arguments[1]))
        {
            return Rejected(InvalidArgumentsMessage);
        }

        if (!string.Equals(enabledValue, "true", StringComparison.Ordinal))
        {
            return Rejected(
                "Controlled database mode rejected: CONTROLPLUS_MIGRATIONS_ENABLED must be exactly true.");
        }

        return Valid(mode.Value, arguments[1]);
    }

    public static bool IsValidMigrationId(string? migrationId) =>
        migrationId is { Length: >= 1 and <= 200 } &&
        !migrationId.StartsWith("--", StringComparison.Ordinal) &&
        migrationId.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static ControlledExecutionCommand Valid(
        ControlledExecutionMode mode,
        string? targetMigration = null) =>
        new(true, mode, targetMigration, string.Empty);

    private static ControlledExecutionCommand Rejected(string message) =>
        new(false, ControlledExecutionMode.Api, null, message);
}
