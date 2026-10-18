using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiPurchasesContractTests
{
    [Fact]
    public async Task PurchaseOrders_ExposeProtectedLifecycleAndReceiptHeader()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_openapi_orders_test;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-openapi-orders-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-openapi-orders-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        Check(paths, "/api/purchase-orders", "get", "post");
        Check(paths, "/api/purchase-orders/{orderId}", "get", "put");
        foreach (var suffix in new[] { "send", "close", "cancel", "receipts" })
            Check(paths, $"/api/purchase-orders/{{orderId}}/{suffix}", "post");
        Check(paths, "/api/purchase-orders/numbering", "get");
        Check(paths, "/api/purchase-orders/numbering/initialize", "post");
        foreach (var kind in new[] { "close", "cancel" })
        {
            Check(paths, $"/api/purchase-orders/reasons/{kind}", "get");
            Check(paths, $"/api/purchase-orders/reasons/{kind}/initialize", "post");
        }
        foreach (var path in paths.EnumerateObject().Where(x => x.Name.StartsWith("/api/purchase-orders", StringComparison.Ordinal)))
            foreach (var operation in path.Value.EnumerateObject())
                Assert.True(Assert.Single(operation.Value.GetProperty("security").EnumerateArray())
                    .TryGetProperty("Bearer", out _));
        var receipt = paths.GetProperty("/api/purchase-orders/{orderId}/receipts").GetProperty("post");
        var header = Assert.Single(receipt.GetProperty("parameters").EnumerateArray(),
            x => x.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.True(header.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task Purchases_PreserveTheEightOriginalProtectedOperations()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_openapi_purchases_test;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-openapi-purchases-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-openapi-purchases-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        Check(paths, "/api/purchases", "get", "post");
        Check(paths, "/api/purchases/{purchaseId}", "get", "put");
        Check(paths, "/api/purchases/{purchaseId}/confirm", "post");
        Check(paths, "/api/document-numbering/purchases", "get", "post", "put");
        foreach (var path in new[] { "/api/purchases", "/api/purchases/{purchaseId}",
                     "/api/purchases/{purchaseId}/confirm", "/api/document-numbering/purchases" })
        foreach (var operation in paths.GetProperty(path).EnumerateObject())
        {
            var value = operation.Value;
            Assert.True(Assert.Single(value.GetProperty("security").EnumerateArray()).TryGetProperty("Bearer", out _));
            var success = operation.Name == "post" && path is "/api/purchases" or "/api/document-numbering/purchases"
                ? "201" : "200";
            Assert.True(value.GetProperty("responses").GetProperty(success).GetProperty("content")
                .TryGetProperty("application/json", out _));
            foreach (var code in new[] { "400", "401", "403", "404", "409" })
                Assert.True(value.GetProperty("responses").GetProperty(code).GetProperty("content")
                    .TryGetProperty("application/problem+json", out _));
        }
        var confirm = paths.GetProperty("/api/purchases/{purchaseId}/confirm").GetProperty("post");
        var header = Assert.Single(confirm.GetProperty("parameters").EnumerateArray(),
            x => x.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "CreatePurchaseRequest", "UpdatePurchaseRequest", "PurchaseLineRequest" })
        {
            var properties = schemas.GetProperty(name).GetProperty("properties");
            foreach (var forbidden in new[] { "orderId", "discount", "discountAmount", "discountPercentage",
                         "stock", "movements", "audit", "status", "prefix", "series", "consecutive" })
                Assert.False(properties.TryGetProperty(forbidden, out _));
        }
    }

    private static void Check(JsonElement paths, string path, params string[] methods) =>
        Assert.Equal(methods.Order(StringComparer.Ordinal), paths.GetProperty(path).EnumerateObject()
            .Select(x => x.Name).Order(StringComparer.Ordinal));
}
