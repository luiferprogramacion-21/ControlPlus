using Npgsql;

namespace ControlPlus.Api.Security;

public static class StartupDatabaseSecurityConfiguration
{
    private const string ExpectedHost = "127.0.0.1";
    private const int ExpectedPort = 5432;
    private const string ExpectedDatabase = "controlplus";
    private const string ExpectedUsername = "controlplus_app";

    /// <summary>
    /// Excepción explícita para copias aisladas (decisión del usuario del 27/11/2026): la auditoría con OWASP ZAP levanta
    /// la API en Production, como en la tienda, contra una base desechable. Solo se acepta si esta opción se
    /// enciende a propósito y la base se llama controlplus_e2e_* o controlplus_test_* en 127.0.0.1. La tienda nunca la
    /// tiene, así que sigue exigiendo controlplus con controlplus_app.
    /// </summary>
    public const string AllowIsolatedTestDatabaseKey = "Security:AllowIsolatedTestDatabase";
    private static readonly string[] IsolatedDatabasePrefixes = ["controlplus_e2e_", "controlplus_test_"];

    public static void EnsureLeastPrivilegeConnection(
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = configuration.GetConnectionString("ControlPlusDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Falta la conexión privada de la base de datos de ControlPlus.");
        }

        NpgsqlConnectionStringBuilder connection;
        try
        {
            connection = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "La conexión privada de la base de datos de ControlPlus no tiene un formato válido.",
                exception);
        }

        var isolated = configuration.GetValue(AllowIsolatedTestDatabaseKey, false) &&
            string.Equals(connection.Host, ExpectedHost, StringComparison.Ordinal) &&
            IsolatedDatabasePrefixes.Any(prefix =>
                (connection.Database ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal) &&
                (connection.Database ?? string.Empty).Length > prefix.Length);
        if (!isolated && (!string.Equals(connection.Host, ExpectedHost, StringComparison.Ordinal) ||
            connection.Port != ExpectedPort ||
            !string.Equals(connection.Database, ExpectedDatabase, StringComparison.Ordinal) ||
            !string.Equals(connection.Username, ExpectedUsername, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "La API normal solo puede conectarse a la base local controlplus con el rol de aplicación de privilegios limitados.");
        }

        if (string.IsNullOrWhiteSpace(connection.Password))
        {
            throw new InvalidOperationException(
                "La conexión privada de la base de datos de ControlPlus no tiene contraseña.");
        }

        if (connection.IncludeErrorDetail)
        {
            throw new InvalidOperationException(
                "La conexión normal no puede exponer detalles internos de PostgreSQL.");
        }
    }
}
