using ControlPlus.Api.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class StartupDatabaseSecurityConfigurationTests
{
    private const string ValidConnection =
        "Host=127.0.0.1;Port=5432;Database=controlplus;Username=controlplus_app;Password=local-test-only";

    [Fact]
    public void NormalStartupWithLeastPrivilegeLocalConnection_Passes()
    {
        var exception = Record.Exception(() => Validate(ValidConnection));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("controlplus_test_admin")]
    [InlineData("controlplus_backup")]
    public void NormalStartupWithAnotherDatabaseRole_IsRejected(string username)
    {
        var connection = ValidConnection.Replace("Username=controlplus_app", $"Username={username}", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(connection));

        Assert.Contains("privilegios limitados", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("local-test-only", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Host=localhost", "Host=127.0.0.1")]
    [InlineData("Port=5433", "Port=5432")]
    [InlineData("Database=postgres", "Database=controlplus")]
    public void NormalStartupOutsideTheApprovedLocalDatabase_IsRejected(
        string replacement,
        string original)
    {
        var connection = ValidConnection.Replace(original, replacement, StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(connection));

        Assert.Contains("base local controlplus", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalStartupWithoutPassword_IsRejectedWithoutEchoingTheConnection()
    {
        const string connection =
            "Host=127.0.0.1;Port=5432;Database=controlplus;Username=controlplus_app";

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(connection));

        Assert.Contains("no tiene contraseña", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(connection, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NormalStartupWithPostgreSqlErrorDetails_IsRejected()
    {
        var connection = ValidConnection + ";Include Error Detail=true";

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(connection));

        Assert.Contains("detalles internos", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestingEnvironment_BypassesTheInstalledDatabaseRestriction()
    {
        var configuration = Configuration("Host=127.0.0.1;Port=5432;Database=controlplus_test_a;Username=test;Password=test");

        var exception = Record.Exception(() =>
            StartupDatabaseSecurityConfiguration.EnsureLeastPrivilegeConnection(configuration, "Testing"));

        Assert.Null(exception);
    }

    [Fact]
    public void InvalidConnection_DoesNotEchoItsSecret()
    {
        const string connection = "this is not a connection;Password=must-not-appear";

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(connection));

        Assert.DoesNotContain("must-not-appear", exception.ToString(), StringComparison.Ordinal);
    }

    private static void Validate(string connection) =>
        StartupDatabaseSecurityConfiguration.EnsureLeastPrivilegeConnection(
            Configuration(connection),
            "Production");

    private static IConfiguration Configuration(string connection) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ControlPlusDb"] = connection
            })
            .Build();
}
