using ControlPlus.Application.Security.Ports;
using ControlPlus.Infrastructure.Persistence.Repositories;
using ControlPlus.Application.Catalog.Ports;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Numbering.Ports;
using ControlPlus.Application.Inventory.Ports;
using ControlPlus.Application.Customers.Ports;
using ControlPlus.Application.Suppliers.Ports;
using ControlPlus.Application.Credits.Ports;
using ControlPlus.Application.Purchases.Ports;
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
        services.AddScoped<ILoginAttemptCoordinator, PostgreSqlLoginAttemptCoordinator>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IPermissionRepository, EfPermissionRepository>();
        services.AddScoped<IAuditRepository, EfAuditRepository>();
        services.AddScoped<ICatalogRepository, EfCatalogRepository>();
        services.AddScoped<ICashRepository, EfCashRepository>();
        services.AddScoped<ICashTransactionManager, EfCashTransactionManager>();
        services.AddScoped<ISalesRepository, EfSalesRepository>();
        services.AddScoped<ISalesTransactionManager, EfSalesTransactionManager>();
        services.AddScoped<IDocumentNumberingRepository, EfDocumentNumberingRepository>();
        services.AddScoped<IDocumentNumberingTransactionManager, EfDocumentNumberingTransactionManager>();
        services.AddScoped<IInventoryRepository, EfInventoryRepository>();
        services.AddScoped<ICustomerRepository, EfCustomerRepository>();
        services.AddScoped<ISupplierRepository, EfSupplierRepository>();
        services.AddScoped<ICreditsRepository, EfCreditsRepository>();
        services.AddScoped<IPurchaseRepository, EfPurchaseRepository>();
        services.AddSingleton<ICredentialTokenGenerator, Code128CredentialTokenGenerator>();
        services.AddScoped<SecurityCatalogSeeder>();
        services.AddScoped<InstallationBootstrapper>();

        return services;
    }
}
