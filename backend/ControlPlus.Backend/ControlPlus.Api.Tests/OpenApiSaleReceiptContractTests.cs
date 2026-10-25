using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiSaleReceiptContractTests
{
    [Fact]
    public async Task Cancellation_DeclaresProtectedReasonAndMutationRoutes()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_test_openapi_cancellation;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-cancellation-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-cancellation-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        var reasons = paths.GetProperty("/api/sales/cancellation-reasons").GetProperty("get");
        var initialize = paths.GetProperty("/api/sales/cancellation-reasons/initialize").GetProperty("post");
        var cancel = paths.GetProperty("/api/sales/{saleId}/cancel").GetProperty("post");
        Assert.True(reasons.GetProperty("security").GetArrayLength() > 0);
        Assert.True(initialize.GetProperty("security").GetArrayLength() > 0);
        Assert.True(cancel.GetProperty("security").GetArrayLength() > 0);
        Assert.Contains(cancel.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key"
                         && parameter.GetProperty("required").GetBoolean());
        foreach (var code in new[] { "200", "400", "401", "403", "404", "409" })
            Assert.True(cancel.GetProperty("responses").TryGetProperty(code, out _));
    }

    [Fact]
    public async Task ReceiptAndReprintRequest_DeclareAuthenticationAndResponses()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_test_openapi_receipt;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-receipt-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-receipt-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        var receipt = paths.GetProperty("/api/sales/{saleId}/receipt").GetProperty("get");
        var reprint = paths.GetProperty("/api/sales/{saleId}/reprint-requests").GetProperty("post");
        Assert.True(receipt.GetProperty("security").GetArrayLength() > 0);
        Assert.True(reprint.GetProperty("security").GetArrayLength() > 0);
        Assert.Contains(reprint.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key"
                         && parameter.GetProperty("required").GetBoolean());
        foreach (var code in new[] { "200", "401", "403", "404", "409" })
        {
            Assert.True(receipt.GetProperty("responses").TryGetProperty(code, out _));
            Assert.True(reprint.GetProperty("responses").TryGetProperty(code, out _));
        }
        Assert.True(reprint.GetProperty("responses").TryGetProperty("400", out _));
    }
}
