using System.Threading.RateLimiting;
using ControlPlus.Api.Authorization;
using ControlPlus.Api.Migrations;
using ControlPlus.Api.OpenApi;
using ControlPlus.Api.Security;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Cash.Services;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Catalog.Services;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Security;
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

if (controlledCommand.Mode == ControlledExecutionMode.HealthCheck)
{
    using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await healthClient.GetAsync("http://127.0.0.1:8080/api/health");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        Environment.ExitCode = 1;
    }
    catch (TaskCanceledException)
    {
        Environment.ExitCode = 1;
    }

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

        var response = new BadRequestObjectResult(details);
        response.ContentTypes.Add("application/problem+json");
        return response;
    };
});
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<ControlPlusOpenApiSecurityTransformer>());
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
