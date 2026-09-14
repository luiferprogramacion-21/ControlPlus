using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ControlPlus.Infrastructure.Persistence.Official;

namespace ControlPlus.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ControlPlusDb")
            ?? throw new InvalidOperationException("The ControlPlusDb connection string is required.");

        services.AddDbContext<OfficialControlPlusDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
