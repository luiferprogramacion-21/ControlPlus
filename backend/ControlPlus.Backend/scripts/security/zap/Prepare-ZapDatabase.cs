// Prepara la base descartable de la auditoría con OWASP ZAP: aplica todas las migraciones a una base vacía.
// Uso (solo lo llama Invoke-ZapAudit.ps1): CONTROLPLUS_ZAP_CONNECTION=... dotnet run Prepare-ZapDatabase.cs
// Se niega a tocar una base cuyo nombre no empiece por "controlplus_test_", una base en el puerto 5432 (el PostgreSQL de
// la tienda) o una base que ya tenga migraciones. La base vive en un PostgreSQL temporal creado por el script.
#:property PublishAot=false
#:project ../../../ControlPlus.Infrastructure/ControlPlus.Infrastructure.csproj

using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("CONTROLPLUS_ZAP_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Falta CONTROLPLUS_ZAP_CONNECTION.");
    return 2;
}

var builder = new NpgsqlConnectionStringBuilder(connectionString);
if (!(builder.Database ?? string.Empty).StartsWith("controlplus_test_", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Solo se preparan bases controlplus_test_*.");
    return 2;
}
if (builder.Port == 5432)
{
    Console.Error.WriteLine("La auditoría usa un PostgreSQL temporal; el puerto 5432 es el de la tienda.");
    return 2;
}

await using var context = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
    .UseNpgsql(connectionString).Options);
if ((await context.Database.GetAppliedMigrationsAsync()).Any())
{
    Console.Error.WriteLine("La base ya tiene migraciones; la auditoría usa una base vacía.");
    return 2;
}

await context.Database.MigrateAsync();
var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
Console.WriteLine($"ZAP_DATABASE_READY migrations={applied.Length} last={applied[^1]}");
return 0;
