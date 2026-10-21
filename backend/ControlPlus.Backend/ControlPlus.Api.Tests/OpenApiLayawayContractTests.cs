using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiLayawayContractTests
{
    [Fact]
    public async Task Layaways_ExposeApprovedRoutesAndServerOwnedFields()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_test_openapi_layaway;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-openapi-layaway-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-openapi-layaway-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        AssertMethods(paths, "/api/layaways", "get", "post");
        AssertMethods(paths, "/api/layaways/{layawayId}", "get");
        AssertMethods(paths, "/api/layaways/{layawayId}/payments", "post");
        AssertMethods(paths, "/api/layaways/{layawayId}/delivery", "post");
        AssertMethods(paths, "/api/layaways/{layawayId}/cancellation", "post");
        AssertMethods(paths, "/api/document-numbering/layaways", "get", "post");
        AssertMethods(paths, "/api/document-numbering/layaway-payments", "get", "post");

        var post = paths.GetProperty("/api/layaways").GetProperty("post");
        var idempotency = Assert.Single(post.GetProperty("parameters").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", idempotency.GetProperty("in").GetString());
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        Assert.True(post.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("409", out _));
        var paymentPost = paths.GetProperty("/api/layaways/{layawayId}/payments").GetProperty("post");
        Assert.Contains(paymentPost.GetProperty("parameters").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "Idempotency-Key" &&
            item.GetProperty("required").GetBoolean());
        Assert.True(paymentPost.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(paymentPost.GetProperty("responses").TryGetProperty("409", out _));
        var deliveryPost = paths.GetProperty("/api/layaways/{layawayId}/delivery").GetProperty("post");
        Assert.Contains(deliveryPost.GetProperty("parameters").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "Idempotency-Key" &&
            item.GetProperty("in").GetString() == "header" &&
            item.GetProperty("required").GetBoolean());
        foreach (var status in new[] { "200", "400", "401", "403", "404", "409" })
            Assert.True(deliveryPost.GetProperty("responses").TryGetProperty(status, out _), status);
        var cancellationPost = paths.GetProperty("/api/layaways/{layawayId}/cancellation").GetProperty("post");
        Assert.Contains(cancellationPost.GetProperty("parameters").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "Idempotency-Key" &&
            item.GetProperty("in").GetString() == "header" && item.GetProperty("required").GetBoolean());
        foreach (var status in new[] { "200", "400", "401", "403", "404", "409" })
            Assert.True(cancellationPost.GetProperty("responses").TryGetProperty(status, out _), status);

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var create = schemas.GetProperty("CreateLayawayRequest").GetProperty("properties");
        var applyPayment = schemas.GetProperty("ApplyLayawayPaymentRequest").GetProperty("properties");
        Assert.True(applyPayment.TryGetProperty("expectedVersion", out _));
        Assert.True(applyPayment.TryGetProperty("payments", out _));
        Assert.False(applyPayment.TryGetProperty("authorization", out _));
        var deliver = schemas.GetProperty("DeliverLayawayRequest").GetProperty("properties");
        Assert.Equal(new[] { "expectedVersion" }, deliver.EnumerateObject().Select(x => x.Name));
        var delivery = schemas.GetProperty("LayawayDeliveryDto").GetProperty("properties");
        foreach (var field in new[] { "layawayId", "number", "state", "version", "deliveredAtUtc",
                     "deliveredByUserId", "correlationId", "products" })
            Assert.True(delivery.TryGetProperty(field, out _), field);
        Assert.True(schemas.GetProperty("LayawayDetailDto").GetProperty("properties")
            .TryGetProperty("delivery", out _));
        Assert.True(schemas.GetProperty("LayawayDetailDto").GetProperty("properties")
            .TryGetProperty("cancellation", out _));
        var cancel = schemas.GetProperty("CancelLayawayRequest").GetProperty("properties");
        foreach (var field in new[] { "expectedVersion", "refundAmount", "refunds", "reasonId",
                     "observation", "authorization" })
            Assert.True(cancel.TryGetProperty(field, out _), field);
        Assert.False(schemas.GetProperty("LayawayCancellationDto").GetProperty("properties")
            .TryGetProperty("password", out _));
        var deliveredProduct = schemas.GetProperty("LayawayDeliveredProductDto").GetProperty("properties");
        Assert.Equal(new[] { "productId", "productCode", "productName", "unit", "quantity" },
            deliveredProduct.EnumerateObject().Select(x => x.Name));
        foreach (var field in new[] { "customerId", "lines", "payments", "dueDate", "authorization" })
            Assert.True(create.TryGetProperty(field, out _), field);
        foreach (var serverOwned in new[] { "total", "initialMinimumPercentage", "state",
                     "discount", "generalDiscount", "installationId", "shiftId" })
            Assert.False(create.TryGetProperty(serverOwned, out _), serverOwned);
        var numbering = schemas.GetProperty("CreateLayawayDocumentNumberingRequest")
            .GetProperty("properties");
        Assert.True(numbering.TryGetProperty("prefix", out _));
        Assert.True(numbering.TryGetProperty("firstNumber", out _));
        Assert.False(numbering.TryGetProperty("documentType", out _));
        Assert.False(numbering.TryGetProperty("installationId", out _));
    }

    private static void AssertMethods(JsonElement paths, string path, params string[] expected)
    {
        var route = paths.GetProperty(path);
        Assert.Equal(expected.Order(StringComparer.Ordinal),
            route.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        foreach (var verb in expected)
        {
            var security = Assert.Single(route.GetProperty(verb).GetProperty("security").EnumerateArray());
            Assert.True(security.TryGetProperty("Bearer", out _));
        }
    }
}
