using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ControlPlus.Infrastructure.Persistence;

public sealed class ControlPlusDbContextFactory : IDesignTimeDbContextFactory<ControlPlusDbContext>
{
    public ControlPlusDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ControlPlusDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__ControlPlusDb before running EF Core design-time commands.");
        }

        var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ControlPlusDbContext(options);
    }
}
