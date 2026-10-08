namespace ControlPlus.Api.Migrations;

public enum ControlledExecutionMode
{
    Api,
    HealthCheck,
    Migration,
    Preflight,
    Postflight
}

public sealed record ControlledExecutionCommand(
    bool IsValid,
    ControlledExecutionMode Mode,
    string? TargetMigration,
    string? ExpectedUnrelatedReasonsSha256,
    string Message)
{
    public const string EnabledEnvironmentVariable = "CONTROLPLUS_MIGRATIONS_ENABLED";
    public const string HealthCheckArgument = "--health-check";
    public const string MigrationTargetArgument = "--migrate-to";
    public const string PreflightTargetArgument = "--preflight-to";
    public const string PostflightTargetArgument = "--postflight-to";
    public const string ExpectedUnrelatedReasonsSha256Argument =
        "--expect-unrelated-reasons-sha256";

    private const string InvalidArgumentsMessage =
        "Command line rejected: expected no arguments, --health-check, " +
        "--migrate-to <MigrationId>, --preflight-to <MigrationId>, " +
        "--postflight-to 20261005010000_CustomerSearchIndexesV1, " +
        "or --postflight-to 20260930010000_SeedInventoryAdjustmentReasonsV1 " +
        "--expect-unrelated-reasons-sha256 <64-hex-sha256>.";

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

        if (arguments.Count == 4 &&
            string.Equals(arguments[0], PostflightTargetArgument, StringComparison.Ordinal) &&
            string.Equals(
                arguments[1],
                InventoryMigrationManifest.TargetMigration,
                StringComparison.Ordinal) &&
            string.Equals(
                arguments[2],
                ExpectedUnrelatedReasonsSha256Argument,
                StringComparison.Ordinal))
        {
            if (!IsValidMigrationId(arguments[1]) || !IsValidSha256(arguments[3]))
            {
                return Rejected(InvalidArgumentsMessage);
            }

            if (!string.Equals(enabledValue, "true", StringComparison.Ordinal))
            {
                return Rejected(
                    "Controlled database mode rejected: " +
                    "CONTROLPLUS_MIGRATIONS_ENABLED must be exactly true.");
            }

            return Valid(
                ControlledExecutionMode.Postflight,
                arguments[1],
                arguments[3].ToLowerInvariant());
        }

        if (arguments.Count != 2)
        {
            return Rejected(InvalidArgumentsMessage);
        }

        var mode = arguments[0] switch
        {
            MigrationTargetArgument => ControlledExecutionMode.Migration,
            PreflightTargetArgument => ControlledExecutionMode.Preflight,
            PostflightTargetArgument when string.Equals(
                arguments[1],
                CustomerSearchMigrationManifest.TargetMigration,
                StringComparison.Ordinal) => ControlledExecutionMode.Postflight,
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

    public static bool IsValidSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or
            >= 'a' and <= 'f' or
            >= 'A' and <= 'F');

    private static ControlledExecutionCommand Valid(
        ControlledExecutionMode mode,
        string? targetMigration = null,
        string? expectedUnrelatedReasonsSha256 = null) =>
        new(true, mode, targetMigration, expectedUnrelatedReasonsSha256, string.Empty);

    private static ControlledExecutionCommand Rejected(string message) =>
        new(false, ControlledExecutionMode.Api, null, null, message);
}
