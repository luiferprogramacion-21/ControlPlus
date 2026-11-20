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
using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Application.Purchases.Ports;
using ControlPlus.Application.PurchaseOrders.Ports;
using ControlPlus.Application.Reports;
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
        services.Configure<ControlPlus.Infrastructure.Email.EmailOptions>(
            configuration.GetSection(ControlPlus.Infrastructure.Email.EmailOptions.SectionName));
        services.AddSingleton<IEmailSender, ControlPlus.Infrastructure.Email.SmtpEmailSender>();
        services.AddSingleton<IPasswordRecoveryCodes, PasswordRecoveryCodes>();

        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddScoped<IClock, SystemClock>();
        services.AddScoped<ILoginAttemptCoordinator, PostgreSqlLoginAttemptCoordinator>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IPermissionRepository, EfPermissionRepository>();
        services.AddScoped<IAuditRepository, EfAuditRepository>();
        services.AddScoped<IAuditUserDirectory, EfAuditUserDirectory>();
        services.AddScoped<ICatalogRepository, EfCatalogRepository>();
        services.AddScoped<ICashRepository, EfCashRepository>();
        services.AddScoped<ICashTransactionManager, EfCashTransactionManager>();
        services.AddScoped<ISalesRepository, EfSalesRepository>();
        services.AddScoped<ISaleDraftRepository, EfSaleDraftRepository>();
        services.AddScoped<ControlPlus.Application.Credits.Ports.ICustomerCreditExposureReader, EfCustomerCreditExposureReader>();
        services.AddScoped<IFinancialSalesReportRepository, EfFinancialSalesReportRepository>();
        services.AddScoped<ISalesOverviewRepository, EfSalesOverviewRepository>();
        services.AddScoped<IDailyCashFlowRepository, EfDailyCashFlowRepository>();
        services.AddScoped<IDashboardInstallationRepository, EfDashboardInstallationRepository>();
        services.AddScoped<IDashboardOverviewRepository, EfDashboardOverviewRepository>();
        services.AddScoped<ISalesTransactionManager, EfSalesTransactionManager>();
        services.AddScoped<IDocumentNumberingRepository, EfDocumentNumberingRepository>();
        services.AddScoped<IDocumentNumberingTransactionManager, EfDocumentNumberingTransactionManager>();
        services.AddScoped<IInventoryRepository, EfInventoryRepository>();
        services.AddScoped<ICustomerRepository, EfCustomerRepository>();
        services.AddScoped<ICustomerHistoryRepository, EfCustomerHistoryRepository>();
        services.AddScoped<ISupplierRepository, EfSupplierRepository>();
        services.AddScoped<ICreditsRepository, EfCreditsRepository>();
        services.AddScoped<ILayawayCreateRepository, EfLayawayCreateRepository>();
        services.AddScoped<ILayawayQueryRepository, EfLayawayQueryRepository>();
        services.AddScoped<ILayawayPaymentRepository, EfLayawayPaymentRepository>();
        services.AddScoped<ILayawayDeliveryRepository, EfLayawayDeliveryRepository>();
        services.AddScoped<ILayawayCancellationRepository, EfLayawayCancellationRepository>();
        services.AddScoped<ILayawayPaymentCorrectionRepository, EfLayawayPaymentCorrectionRepository>();
        services.AddScoped<IPurchaseRepository, EfPurchaseRepository>();
        services.AddScoped<IPurchaseOrderRepository, EfPurchaseOrderRepository>();
        services.AddSingleton<ICredentialTokenGenerator, Code128CredentialTokenGenerator>();
        services.AddSingleton<OperatorCredentialProtector>();
        services.AddScoped<IOperatorCredentialStore, ProtectedOperatorCredentialStore>();
        services.AddScoped<SecurityCatalogSeeder>();
        services.AddScoped<InstallationBootstrapper>();

        return services;
    }
}
