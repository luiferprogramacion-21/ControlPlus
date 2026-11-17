// Prepara la base descartable de las pruebas de navegador: aplica todas las migraciones a una base vacía.
// Uso (solo en el CI o en una máquina de pruebas): CONTROLPLUS_E2E_CONNECTION=... dotnet run Prepare-E2EDatabase.cs
// Se niega a tocar cualquier base cuyo nombre no empiece por "controlplus_e2e_" o que ya tenga migraciones.
#:property PublishAot=false
#:project ../../ControlPlus.Infrastructure/ControlPlus.Infrastructure.csproj

using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("CONTROLPLUS_E2E_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Falta CONTROLPLUS_E2E_CONNECTION.");
    return 2;
}

var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;
if (!database.StartsWith("controlplus_e2e_", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Solo se preparan bases controlplus_e2e_*.");
    return 2;
}

await using var context = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
    .UseNpgsql(connectionString).Options);
if ((await context.Database.GetAppliedMigrationsAsync()).Any())
{
    Console.Error.WriteLine("La base ya tiene migraciones; las pruebas de navegador usan una base vacía.");
    return 2;
}

await context.Database.MigrateAsync();
var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
Console.WriteLine($"E2E_DATABASE_READY migrations={applied.Length} last={applied[^1]}");
return 0;
