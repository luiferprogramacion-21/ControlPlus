using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiReportsContractTests
{
    [Fact]
    public async Task DailySales_DeclaresProtectedDateRangeAndPublicResponse()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_test_openapi_reports;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-reports-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-reports-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/reports/sales/daily").GetProperty("get");
        Assert.True(operation.GetProperty("security").GetArrayLength() > 0);
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Contains(parameters, x => x.GetProperty("name").GetString() == "from");
        Assert.Contains(parameters, x => x.GetProperty("name").GetString() == "to");
        Assert.All(parameters.Where(x => x.GetProperty("name").GetString() is "from" or "to"),
            x => Assert.True(x.GetProperty("required").GetBoolean()));
        foreach (var code in new[] { "200", "400", "401", "403", "404" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(code, out _), code);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var report = schemas.GetProperty("FinancialSalesReport").GetProperty("properties");
        foreach (var field in new[] { "from", "to", "days", "cash", "credit", "total" })
            Assert.True(report.TryGetProperty(field, out _), field);
        foreach (var field in new[] { "cost", "margin", "customer", "payment" })
            Assert.False(report.TryGetProperty(field, out _), field);

        var cashFlow = document.RootElement.GetProperty("paths")
            .GetProperty("/api/reports/cash-flow/daily").GetProperty("get");
        Assert.True(cashFlow.GetProperty("security").GetArrayLength() > 0);
        Assert.All(cashFlow.GetProperty("parameters").EnumerateArray()
                .Where(x => x.GetProperty("name").GetString() is "from" or "to"),
            x => Assert.True(x.GetProperty("required").GetBoolean()));
        foreach (var code in new[] { "200", "400", "401", "403", "404" })
            Assert.True(cashFlow.GetProperty("responses").TryGetProperty(code, out _), code);
        var cashFlowProperties = schemas.GetProperty("DailyCashFlowReport").GetProperty("properties");
        foreach (var field in new[] { "from", "to", "days", "openingBalance", "paymentTotal", "manualTotal" })
            Assert.True(cashFlowProperties.TryGetProperty(field, out _), field);
    }
}
