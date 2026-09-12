using ControlPlus.Application.Security.Ports;
using ControlPlus.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlus.Infrastructure.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddSecurityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddScoped<IClock, SystemClock>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IPermissionRepository, EfPermissionRepository>();
        services.AddScoped<IAuditRepository, EfAuditRepository>();
        services.AddScoped<SecurityCatalogSeeder>();
        services.AddScoped<InstallationBootstrapper>();

        return services;
    }
}
