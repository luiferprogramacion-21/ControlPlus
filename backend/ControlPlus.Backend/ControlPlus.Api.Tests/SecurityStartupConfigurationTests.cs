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
    [InlineData("Installation:MasterKey", "JWT_SIGNING_KEY")]
    [InlineData("Jwt:SigningKey", "CONTROLPLUS_MASTER_KEY")]
    public void DevelopmentWithOneMissingSecret_NamesTheMissingEnvironmentVariable(
        string configuredKey,
        string expectedMissingVariable)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [configuredKey] = configuredKey == "Installation:MasterKey"
                    ? new string('m', BootstrapOptions.MinimumMasterKeyByteLength)
                    : "configured-locally"
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
    public void DevelopmentWithShortMasterKey_FailsWithTheMinimumLength()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Installation:MasterKey"] = "too-short",
                ["Jwt:SigningKey"] = new string('j', 64)
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupSecurityConfiguration.EnsureRequiredSecrets(configuration, "Development"));

        Assert.Contains("CONTROLPLUS_MASTER_KEY", exception.Message, StringComparison.Ordinal);
        Assert.Contains(BootstrapOptions.MinimumMasterKeyByteLength.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DevelopmentWithSecureMasterKeyAndSigningKey_Passes()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Installation:MasterKey"] = new string('m', BootstrapOptions.MinimumMasterKeyByteLength),
                ["Jwt:SigningKey"] = new string('j', 64)
            })
            .Build();

        StartupSecurityConfiguration.EnsureRequiredSecrets(configuration, "Development");
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
