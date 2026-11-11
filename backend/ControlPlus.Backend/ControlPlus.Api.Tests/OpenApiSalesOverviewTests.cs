using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiSalesOverviewTests
{
    [Fact]
    public async Task OverviewAndPdf_ExposeProtectedDateRangeAndResponseTypes()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_test_openapi_overview;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-overview-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-overview-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/reports/sales/overview", "/api/reports/sales/overview.pdf" })
        {
            var operation = paths.GetProperty(path).GetProperty("get");
            Assert.True(operation.GetProperty("security").GetArrayLength() > 0);
            var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
            Assert.All(parameters.Where(x => x.GetProperty("name").GetString() is "from" or "to"),
                x => Assert.True(x.GetProperty("required").GetBoolean()));
            foreach (var code in new[] { "200", "400", "401", "403", "404" })
                Assert.True(operation.GetProperty("responses").TryGetProperty(code, out _), $"{path}: {code}");
        }
        var overview = paths.GetProperty("/api/reports/sales/overview").GetProperty("get");
        Assert.True(overview.GetProperty("responses").GetProperty("200").GetProperty("content")
            .TryGetProperty("application/json", out _));
        var pdf = paths.GetProperty("/api/reports/sales/overview.pdf").GetProperty("get");
        Assert.True(pdf.GetProperty("responses").GetProperty("200").GetProperty("content")
            .TryGetProperty("application/pdf", out _));
    }
}
