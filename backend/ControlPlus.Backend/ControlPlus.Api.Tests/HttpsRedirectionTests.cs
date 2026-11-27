using System.Collections.Concurrent;
using System.Net;
using ControlPlus.Api.Backups;
using ControlPlus.Api.Printing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class HttpsRedirectionTests
{
    private const string RedirectionCategory = "Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware";

    [Theory]
    [InlineData(null)]
    [InlineData("7443")]
    public async Task LocalProduction_HttpWorksWithoutRedirectionOrPortWarning(string? httpsPort)
    {
        using var logs = new CaptureLogProvider();
        using var factory = CreateFactory("Production", logs, httpsPort: httpsPort);
        using var client = CreateClient(factory, "http://localhost:5000");

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(logs.RedirectionLevels);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Testing", null)]
    public async Task EnabledOrUnspecified_RedirectsHttpToConfiguredHttpsPort(string environment, bool? enabled)
    {
        using var logs = new CaptureLogProvider();
        using var factory = CreateFactory(environment, logs, enabled, "7443");
        using var client = CreateClient(factory, "http://localhost:5000");

        using var response = await client.GetAsync("/api/health?check=local");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal(new Uri("https://localhost:7443/api/health?check=local"), response.Headers.Location);
        Assert.DoesNotContain(logs.RedirectionLevels, level => level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Enabled_HttpsRequestIsServedWithoutAnotherRedirect()
    {
        using var logs = new CaptureLogProvider();
        using var factory = CreateFactory("Production", logs, enabled: true, httpsPort: "7443");
        using var client = CreateClient(factory, "https://localhost:7443");

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain(logs.RedirectionLevels, level => level >= LogLevel.Warning);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string baseAddress) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(baseAddress)
        });

    private static WebApplicationFactory<Program> CreateFactory(
        string environment, CaptureLogProvider logs, bool? enabled = null, string? httpsPort = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=5432;Database=controlplus;Username=controlplus_app;Password=https-test-only");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-https-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-https-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
            if (enabled.HasValue) builder.UseSetting("HttpsRedirection:Enabled", enabled.Value.ToString());
            if (httpsPort is not null) builder.UseSetting("https_port", httpsPort);
            builder.ConfigureLogging(logging => logging
                .AddFilter(RedirectionCategory, LogLevel.Trace)
                .AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                // La comprobación de salud HTTP no consulta datos; los workers tampoco deben hacerlo en esta prueba.
                foreach (var worker in services.Where(descriptor =>
                             descriptor.ServiceType == typeof(IHostedService) &&
                             (descriptor.ImplementationType == typeof(PrintQueueWorker) ||
                              descriptor.ImplementationType == typeof(BackupWorker))).ToArray())
                    services.Remove(worker);
            });
        });

    private sealed class CaptureLogProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogLevel> RedirectionLevels { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, RedirectionLevels);
        public void Dispose() { }

        private sealed class CaptureLogger(string category, ConcurrentQueue<LogLevel> levels) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => category == RedirectionCategory;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) levels.Enqueue(logLevel);
            }
        }
    }
}
