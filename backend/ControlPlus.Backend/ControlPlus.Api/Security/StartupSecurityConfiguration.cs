namespace ControlPlus.Api.Security;

public static class StartupSecurityConfiguration
{
    public static void EnsureRequiredSecrets(
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var missingVariables = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration["Installation:MasterKey"]))
        {
            missingVariables.Add("CONTROLPLUS_MASTER_KEY");
        }

        if (string.IsNullOrWhiteSpace(configuration["Jwt:SigningKey"]))
        {
            missingVariables.Add("JWT_SIGNING_KEY");
        }

        if (missingVariables.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required security environment variable(s): {string.Join(", ", missingVariables)}. " +
                "Configure them outside the repository before starting ControlPlus.Api.");
        }
    }
}
