using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiDashboardContractTests
{
    [Fact]
    public async Task Summary_DeclaresProtectedResponseWithoutClientDateOrInstallation()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_test_openapi_dashboard;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-dashboard-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-dashboard-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/dashboard/summary").GetProperty("get");
        Assert.True(operation.GetProperty("security").GetArrayLength() > 0);
        Assert.False(operation.TryGetProperty("parameters", out var parameters) && parameters.GetArrayLength() > 0);
        foreach (var code in new[] { "200", "401", "403", "404" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(code, out _), code);
        var fields = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("DashboardSummary").GetProperty("properties");
        foreach (var field in new[] { "operatingDate", "cashSales", "creditSales", "totalSales",
                     "openingBalance", "paymentTotal", "manualTotal", "netCashFlow",
                     "comparison", "currentCash", "alerts", "hourlySales", "recentActivity" })
            Assert.True(fields.TryGetProperty(field, out _), field);
    }
}
