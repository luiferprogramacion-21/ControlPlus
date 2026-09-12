using ControlPlus.Api.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class SecurityStartupConfigurationTests
{
    [Fact]
    public void DevelopmentWithoutSecrets_FailsWithEnvironmentVariableNames()
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupSecurityConfiguration.EnsureRequiredSecrets(configuration, "Development"));

        Assert.Contains("CONTROLPLUS_MASTER_KEY", exception.Message, StringComparison.Ordinal);
        Assert.Contains("JWT_SIGNING_KEY", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Installation:MasterKey", "configured-locally", "JWT_SIGNING_KEY")]
    [InlineData("Jwt:SigningKey", "configured-locally", "CONTROLPLUS_MASTER_KEY")]
    public void DevelopmentWithOneMissingSecret_NamesTheMissingEnvironmentVariable(
        string configuredKey,
        string configuredValue,
        string expectedMissingVariable)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [configuredKey] = configuredValue
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupSecurityConfiguration.EnsureRequiredSecrets(configuration, "Development"));

        Assert.Contains(expectedMissingVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestingWithoutSecrets_BypassesOuterStartupRequirement()
    {
        var configuration = new ConfigurationBuilder().Build();

        StartupSecurityConfiguration.EnsureRequiredSecrets(configuration, "Testing");
    }

    [Fact]
    public void ProductionWithMappedSecrets_PassesStartupRequirement()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Installation:MasterKey"] = "configured-outside-the-repository",
                ["Jwt:SigningKey"] = "configured-outside-the-repository"
            })
            .Build();

        StartupSecurityConfiguration.EnsureRequiredSecrets(configuration, "Production");
    }
}
