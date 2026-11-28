using System.Threading.RateLimiting;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Migrations;
using ControlPlus.Api.OpenApi;
using ControlPlus.Api.Security;
using ControlPlus.Api.Printing;
using ControlPlus.Api.Backups;
using ControlPlus.Application.Backups;
using ControlPlus.Infrastructure.Backups;
using ControlPlus.Infrastructure.Persistence.Repositories;
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
using ControlPlus.Application.Establishments.Contracts;
using ControlPlus.Application.Establishments.Services;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Layaways.Services;
using ControlPlus.Application.Purchases.Contracts;
using ControlPlus.Application.Purchases.Services;
using ControlPlus.Application.PurchaseOrders.Contracts;
using ControlPlus.Application.PurchaseOrders.Services;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Infrastructure.Security;
using ControlPlus.Infrastructure.Printing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting.WindowsServices;
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
    await Console.Error.WriteLineAsync(controlledCommand.Message);
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
    await preflightOutput.WriteLineAsync(preflightResult.Message);
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
    await postflightOutput.WriteLineAsync(postflightResult.Message);
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
    await migrationOutput.WriteLineAsync(migrationResult.Message);
    Environment.ExitCode = migrationResult.ExitCode;
    return;
}

// Como servicio de Windows el directorio actual es System32: el contenido (wwwroot, appsettings) se toma de la
// carpeta del ejecutable. Fuera del servicio el comportamiento no cambia.
var runningAsService = WindowsServiceHelpers.IsWindowsService();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = runningAsService ? AppContext.BaseDirectory : null
});
if (runningAsService)
{
    builder.Services.AddWindowsService(options => options.ServiceName = "ControlPlus");
    // Los User Secrets solo existen para el usuario que desarrolla; el servicio lee los mismos valores de un archivo
    // protegido (solo SYSTEM y Administradores) que escribe scripts/service/Install-ControlPlusService.ps1.
    builder.Configuration.AddJsonFile(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ControlPlus", "api-secrets.json"),
        optional: false, reloadOnChange: false);
}

StartupSecurityConfiguration.EnsureRequiredSecrets(
    builder.Configuration,
    builder.Environment.EnvironmentName);
StartupDatabaseSecurityConfiguration.EnsureLeastPrivilegeConnection(
    builder.Configuration,
    builder.Environment.EnvironmentName);

// Auditoría ZAP del 27/11/2026 (ZAP-ACT-001): ningún texto del cuerpo JSON puede traer el carácter nulo.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
    options.JsonSerializerOptions.Converters.Add(new RejectNullCharacterStringConverter()));

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
builder.Services.AddExceptionHandler<TestingExceptionDetailHandler>();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddSecurityInfrastructure(builder.Configuration);
builder.Services.AddControlPlusJwtAuthentication(builder.Configuration);

builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IPasswordRecoveryService, PasswordRecoveryService>();
// Contraseñas de 4 números: el servicio instalado activa la regla en appsettings.Production.json.
builder.Services.AddSingleton<IPasswordPolicy>(new PasswordPolicy(string.Equals(
    builder.Configuration["Security:CredentialFormat"], PasswordPolicy.PinFormat, StringComparison.OrdinalIgnoreCase)));
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IRoleManagementService, RoleManagementService>();
builder.Services.AddScoped<IAuditQueryService, AuditQueryService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<ICashService, CashService>();
builder.Services.AddScoped<IAuthorizerCredentialResolver, AuthorizerCredentialResolver>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<ISaleDraftService, SaleDraftService>();
builder.Services.AddScoped<IReceiptPrintingService, ReceiptPrintingService>();
builder.Services.AddScoped<IProductLabelPrintingService, ProductLabelPrintingService>();
builder.Services.AddScoped<ICashDrawerService, CashDrawerService>();
builder.Services.AddSingleton<IPrintSpooler, WindowsGdiPrintSpooler>();
builder.Services.AddSingleton<ICashDrawerPulseSender, WindowsRawCashDrawerPulseSender>();
builder.Services.AddHostedService<PrintQueueWorker>();
// Respaldos de Fase 4: apagados salvo Backup:Enabled=true y ConnectionStrings:ControlPlusBackup configurados.
builder.Services.Configure<BackupOptions>(builder.Configuration.GetSection(BackupOptions.SectionName));
builder.Services.AddSingleton<BackupRequestQueue>();
builder.Services.AddSingleton<IBackupRequestQueue>(provider => provider.GetRequiredService<BackupRequestQueue>());
builder.Services.AddSingleton<IBackupRuntime, BackupRuntime>();
builder.Services.AddSingleton<IBackupEngine, PgDumpBackupEngine>();
builder.Services.AddScoped<IBackupRepository, EfBackupRepository>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddHostedService<BackupWorker>();
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
builder.Services.AddScoped<ICustomerCreditStages, CustomerCreditStages>();
builder.Services.AddScoped<ISalesSettingsService, SalesSettingsService>();
builder.Services.AddScoped<IEstablishmentService, EstablishmentService>();
builder.Services.AddScoped<ILayawayCreateService, LayawayCreateService>();
builder.Services.AddScoped<ILayawayQueryService, LayawayQueryService>();
builder.Services.AddScoped<ILayawaySettingsService, LayawaySettingsService>();
builder.Services.AddScoped<ILayawayPaymentService, LayawayPaymentService>();
builder.Services.AddScoped<ILayawayDeliveryService, LayawayDeliveryService>();
builder.Services.AddScoped<ILayawayCancellationService, LayawayCancellationService>();
// Correcciones operativas (decisión del 19 de noviembre de 2026): anular un abono y corregir productos de un apartado.
builder.Services.AddScoped<LayawayPaymentCancellationService>();
builder.Services.AddScoped<ILayawayPaymentCancellationService>(provider => provider.GetRequiredService<LayawayPaymentCancellationService>());
builder.Services.AddScoped<ILayawayProductCorrectionService>(provider => provider.GetRequiredService<LayawayPaymentCancellationService>());
builder.Services.AddScoped<IPurchaseService, PurchaseService>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<ICurrentUserSessionValidator, CurrentUserSessionValidator>();
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentActorContextResolver>();
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddSingleton<BootstrapAccessValidator>();

builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();

// Ingresos por minuto desde un mismo equipo: 10 en la tienda. Las pruebas de navegador del CI inician sesión muchas veces
// seguidas desde la misma dirección y lo suben con RateLimiting:LoginPerMinute (26/11/2026); solo se aceptan 1 a 1000.
var loginPerMinute = Math.Clamp(builder.Configuration.GetValue<int?>("RateLimiting:LoginPerMinute") ?? 10, 1, 1000);
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
            _ => FixedWindow(loginPerMinute)));
    // Pedir y usar códigos de recuperación de contraseña: pocos por minuto, para no enviar correos en masa.
    options.AddPolicy("password-recovery", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => FixedWindow(6)));
});

static FixedWindowRateLimiterOptions FixedWindow(int permitLimit) => new()
{
    PermitLimit = permitLimit,
    Window = TimeSpan.FromMinutes(1),
    QueueLimit = 0,
    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
};

var app = builder.Build();

// Defensa base para la API y la pantalla instalada: evita inferencia de tipos, incrustación en marcos y acceso
// accidental a capacidades del equipo que ControlPlus no utiliza. No se aplica CSP aquí porque debe validarse
// por separado con la pantalla Angular y los recorridos de impresión antes de restringir sus recursos.
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
    context.Response.Headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    // Auditoría ZAP del 27/11/2026 (ZAP-PAS-001): solo scripts propios; estilos en línea porque Angular inyecta los de
    // cada pantalla y el ticket se imprime en un marco con estilos propios; imágenes data:/blob: por los códigos de
    // barras y el carné. Sin marcos de otros sitios ni formularios hacia afuera.
    context.Response.Headers.TryAdd("Content-Security-Policy", SecurityHeaders.ContentSecurityPolicy);
    await next();
});

app.UseExceptionHandler();

// Auditoría ZAP del 27/11/2026 (ZAP-ACT-001): el carácter nulo en la ruta o la consulta se rechaza con 400 antes de
// llegar a PostgreSQL, que no lo acepta en un texto y respondía 500.
app.Use(async (context, next) =>
{
    if (NullCharacterRequestGuard.HasNullCharacter(context.Request))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "La solicitud contiene un carácter no permitido.",
            Type = "https://controlplus.local/problems/validation.failed",
            Extensions = { ["code"] = "validation.failed" }
        }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
        return;
    }
    await next();
});

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi().AllowAnonymous();
}

// La instalación local desactiva esta opción; sin configuración explícita se conserva la redirección.
if (app.Configuration.GetValue("HttpsRedirection:Enabled", true))
{
    app.UseHttpsRedirection();
}
// Pantalla compilada (Angular) en wwwroot cuando se instala como servicio; en desarrollo no hay wwwroot y se sigue
// usando ng serve con su proxy.
var hasFrontend = File.Exists(Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html"));
if (hasFrontend)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Contraseña de 4 números (decisión del 19 de noviembre de 2026): una sesión iniciada con una contraseña anterior
// solo puede cambiarla, consultar su usuario y renovarse.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (context.User.HasClaim(JwtClaimTypes.CredentialRenewal, JwtClaimTypes.CredentialRenewalRequiredValue) &&
        path.StartsWithSegments("/api") &&
        !path.StartsWithSegments("/api/auth/password-change") &&
        !path.StartsWithSegments("/api/auth/me") &&
        !path.StartsWithSegments("/api/auth/refresh"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Cambia tu contraseña por una de 4 números para continuar.",
            Extensions = { ["code"] = "password.change_required" }
        }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
        return;
    }
    await next();
});

app.MapControllers();
if (hasFrontend)
{
    // Las rutas de la pantalla (/operacion/...) devuelven index.html; las de /api que no existen siguen dando 404.
    // nonfile: los archivos (main.js, estilos) los sirve UseStaticFiles; solo las rutas de la pantalla caen aquí.
    app.MapFallbackToFile("{**path:nonfile:regex(^(?!api(/|$)).*$)}", "index.html").AllowAnonymous();
}

await app.RunAsync();
