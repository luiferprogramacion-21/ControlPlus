using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiSaleChangePreparationContractTests
{
    [Fact]
    public async Task SaleChangePreparation_DeclaresProtectedSearchAndPreparationRoutes()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_test_openapi_change;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-change-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-change-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/sales/change-search", "/api/sales/{saleId}/change-preparation" })
        {
            var operation = paths.GetProperty(path).GetProperty("get");
            Assert.True(operation.GetProperty("security").GetArrayLength() > 0);
            foreach (var code in new[] { "200", "401", "403", "404" })
                Assert.True(operation.GetProperty("responses").TryGetProperty(code, out _));
        }
        Assert.Contains(paths.GetProperty("/api/sales/change-search").GetProperty("get")
            .GetProperty("parameters").EnumerateArray(), x =>
            x.GetProperty("name").GetString() == "visibleNumber" && x.GetProperty("required").GetBoolean());
        var list = paths.GetProperty("/api/sales/{saleId}/changes").GetProperty("get");
        var confirm = paths.GetProperty("/api/sales/{saleId}/changes").GetProperty("post");
        var numbering = paths.GetProperty("/api/document-numbering/sale-changes");
        var policy = paths.GetProperty("/api/sales/change-policy");
        Assert.True(list.GetProperty("security").GetArrayLength() > 0);
        Assert.True(confirm.GetProperty("security").GetArrayLength() > 0);
        Assert.True(numbering.GetProperty("get").GetProperty("security").GetArrayLength() > 0);
        Assert.True(numbering.GetProperty("post").GetProperty("security").GetArrayLength() > 0);
        Assert.True(policy.GetProperty("get").GetProperty("security").GetArrayLength() > 0);
        Assert.True(policy.GetProperty("put").GetProperty("security").GetArrayLength() > 0);
        foreach (var code in new[] { "200", "400", "401", "403", "404", "409" })
            Assert.True(policy.GetProperty("put").GetProperty("responses").TryGetProperty(code, out _));
        Assert.Contains(confirm.GetProperty("parameters").EnumerateArray(), x =>
            x.GetProperty("name").GetString() == "Idempotency-Key" && x.GetProperty("required").GetBoolean());
        foreach (var code in new[] { "201", "400", "401", "403", "404", "409" })
            Assert.True(confirm.GetProperty("responses").TryGetProperty(code, out _));
    }
}
