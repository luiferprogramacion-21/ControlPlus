using System.Threading.RateLimiting;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Migrations;
using ControlPlus.Api.OpenApi;
using ControlPlus.Api.Security;
using ControlPlus.Api.Printing;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Cash.Services;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Catalog.Services;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Sales.Services;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Reports;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Numbering.Services;
using ControlPlus.Application.Inventory.Contracts;
using ControlPlus.Application.Inventory.Services;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Customers.Services;
using ControlPlus.Application.Suppliers.Contracts;
using ControlPlus.Application.Suppliers.Services;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Credits.Services;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Layaways.Services;
using ControlPlus.Application.Purchases.Contracts;
using ControlPlus.Application.Purchases.Services;
using ControlPlus.Application.PurchaseOrders.Contracts;
using ControlPlus.Application.PurchaseOrders.Services;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Security;
using ControlPlus.Infrastructure.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

var executableArguments = System.Reflection.Assembly.GetEntryAssembly() == typeof(Program).Assembly
    ? args
    : [];
var controlledCommand = ControlledExecutionCommand.Parse(
    executableArguments,
    Environment.GetEnvironmentVariable(ControlledExecutionCommand.EnabledEnvironmentVariable));
if (!controlledCommand.IsValid)
{
    Console.Error.WriteLine(controlledCommand.Message);
    Environment.ExitCode = ControlledMigrationRunner.RejectedExitCode;
    return;
}

if (controlledCommand.Mode == ControlledExecutionMode.Preflight)
{
    var preflightBuilder = WebApplication.CreateBuilder(args);
    var preflightResult = await ControlledPreflightRunner.RunAsync(
        preflightBuilder.Configuration,
        controlledCommand.TargetMigration!);

    var preflightOutput = preflightResult.ExitCode == 0 ? Console.Out : Console.Error;
    preflightOutput.WriteLine(preflightResult.Message);
    Environment.ExitCode = preflightResult.ExitCode;
    return;
}

if (controlledCommand.Mode == ControlledExecutionMode.Postflight)
{
    var postflightBuilder = WebApplication.CreateBuilder(args);
    var postflightResult = await ControlledPostflightRunner.RunAsync(
        postflightBuilder.Configuration,
        controlledCommand.TargetMigration!,
        controlledCommand.ExpectedUnrelatedReasonsSha256);

    var postflightOutput = postflightResult.ExitCode == 0 ? Console.Out : Console.Error;
    postflightOutput.WriteLine(postflightResult.Message);
    Environment.ExitCode = postflightResult.ExitCode;
    return;
}

if (controlledCommand.Mode == ControlledExecutionMode.Migration)
{
    var migrationBuilder = WebApplication.CreateBuilder(args);
    var migrationResult = await ControlledMigrationRunner.RunAsync(
        migrationBuilder.Configuration,
        controlledCommand.TargetMigration!);

    var migrationOutput = migrationResult.ExitCode == 0 ? Console.Out : Console.Error;
    migrationOutput.WriteLine(migrationResult.Message);
    Environment.ExitCode = migrationResult.ExitCode;
    return;
}

var builder = WebApplication.CreateBuilder(args);

StartupSecurityConfiguration.EnsureRequiredSecrets(
    builder.Configuration,
    builder.Environment.EnvironmentName);

builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Uno o más campos de la solicitud son inválidos.",
            Type = "https://controlplus.local/problems/validation.failed",
            Instance = context.HttpContext.Request.Path
        };
        details.Extensions["code"] = "validation.failed";

        return new JsonResult(details)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentType = "application/problem+json"
        };
    };
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ControlPlusOpenApiSecurityTransformer>();
    options.AddSchemaTransformer<OptionalProductInternalCodeSchemaTransformer>();
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();
builder.Services.AddExceptionHandler<CashMoneyExceptionHandler>();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddSecurityInfrastructure(builder.Configuration);
builder.Services.AddControlPlusJwtAuthentication(builder.Configuration);

builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IRoleManagementService, RoleManagementService>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<ICashService, CashService>();
builder.Services.AddScoped<IAuthorizerCredentialResolver, AuthorizerCredentialResolver>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<IReceiptPrintingService, ReceiptPrintingService>();
builder.Services.AddSingleton<IPrintSpooler, WindowsGdiPrintSpooler>();
builder.Services.AddHostedService<PrintQueueWorker>();
builder.Services.AddScoped<IFinancialSalesReportService, FinancialSalesReportService>();
builder.Services.AddScoped<ISalesOverviewService, SalesOverviewService>();
builder.Services.AddScoped<IDailyCashFlowService, DailyCashFlowService>();
builder.Services.AddScoped<IDashboardSummaryService, DashboardSummaryService>();
builder.Services.AddScoped<IDocumentNumberingService, DocumentNumberingService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ICustomerHistoryService, CustomerHistoryService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<ICreditsService, CreditsService>();
builder.Services.AddScoped<ICreditSettingsService, CreditSettingsService>();
builder.Services.AddScoped<ILayawayCreateService, LayawayCreateService>();
builder.Services.AddScoped<ILayawayQueryService, LayawayQueryService>();
builder.Services.AddScoped<ILayawaySettingsService, LayawaySettingsService>();
builder.Services.AddScoped<ILayawayPaymentService, LayawayPaymentService>();
builder.Services.AddScoped<ILayawayDeliveryService, LayawayDeliveryService>();
builder.Services.AddScoped<ILayawayCancellationService, LayawayCancellationService>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<ICurrentUserSessionValidator, CurrentUserSessionValidator>();
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();
builder.Services.AddScoped<CurrentActorContextResolver>();
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddSingleton<BootstrapAccessValidator>();

builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("installation", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => FixedWindow(3)));
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => FixedWindow(10)));
});

static FixedWindowRateLimiterOptions FixedWindow(int permitLimit) => new()
{
    PermitLimit = permitLimit,
    Window = TimeSpan.FromMinutes(1),
    QueueLimit = 0,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
};

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
