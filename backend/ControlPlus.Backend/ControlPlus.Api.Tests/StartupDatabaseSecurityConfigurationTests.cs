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

    // Copias aisladas del CI y de la auditoría ZAP (27/11/2026): solo con la opción encendida a propósito.
    [Theory]
    [InlineData("controlplus_e2e_ci", 5432, "postgres")]
    [InlineData("controlplus_test_0a1b2c", 55433, "postgres")]
    public void IsolatedTestDatabase_IsAcceptedOnlyWhenExplicitlyAllowed(string database, int port, string username)
    {
        var connection = $"Host=127.0.0.1;Port={port};Database={database};Username={username};Password=isolated-only";

        Assert.Throws<InvalidOperationException>(() => Validate(connection));
        Assert.Null(Record.Exception(() => Validate(connection, allowIsolated: true)));
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Port=5432;Database=controlplus;Username=postgres;Password=x")]
    [InlineData("Host=localhost;Port=5432;Database=controlplus_test_a;Username=postgres;Password=x")]
    [InlineData("Host=192.168.1.20;Port=5432;Database=controlplus_e2e_ci;Username=postgres;Password=x")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=controlplus_test_;Username=postgres;Password=x")]
    [InlineData("Host=127.0.0.1;Port=5432;Database=controlplus_real;Username=postgres;Password=x")]
    public void IsolatedOption_NeverOpensTheStoreDatabaseOrAnotherHost(string connection)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Validate(connection, allowIsolated: true));

        Assert.Contains("privilegios limitados", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsolatedTestDatabase_StillNeedsPasswordAndHidesErrorDetails()
    {
        const string withoutPassword = "Host=127.0.0.1;Port=55433;Database=controlplus_test_a;Username=postgres";
        Assert.Contains("no tiene contraseña",
            Assert.Throws<InvalidOperationException>(() => Validate(withoutPassword, allowIsolated: true)).Message,
            StringComparison.Ordinal);
        Assert.Contains("detalles internos",
            Assert.Throws<InvalidOperationException>(() =>
                Validate(withoutPassword + ";Password=x;Include Error Detail=true", allowIsolated: true)).Message,
            StringComparison.Ordinal);
    }

    private static void Validate(string connection, bool allowIsolated = false) =>
        StartupDatabaseSecurityConfiguration.EnsureLeastPrivilegeConnection(
            Configuration(connection, allowIsolated),
            "Production");

    private static IConfiguration Configuration(string connection, bool allowIsolated = false) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ControlPlusDb"] = connection,
                [StartupDatabaseSecurityConfiguration.AllowIsolatedTestDatabaseKey] = allowIsolated ? "true" : null
            })
            .Build();
}
