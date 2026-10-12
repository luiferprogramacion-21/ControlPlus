using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiCreditsContractTests
{
    [Fact]
    public async Task CreditsAndAboNumbering_ExposeOnlyApprovedVerbsAndProtectedProblemContracts()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_openapi_credits_test;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-openapi-credits-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-openapi-credits-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        AssertOperations(paths, "/api/credits", "get");
        AssertOperations(paths, "/api/credits/{creditId}", "get");
        AssertOperations(paths, "/api/credits/{creditId}/payments", "get", "post");
        AssertOperations(paths, "/api/document-numbering/credit-payments", "get", "post", "put");
        AssertResponse(paths, "/api/credits", "get", "200", "application/json");
        AssertResponse(paths, "/api/credits", "get", "400", "application/problem+json");
        AssertResponse(paths, "/api/credits/{creditId}", "get", "404", "application/problem+json");
        AssertResponse(paths, "/api/credits/{creditId}/payments", "post", "201", "application/json");
        AssertResponse(paths, "/api/credits/{creditId}/payments", "post", "409", "application/problem+json");
        AssertResponse(paths, "/api/document-numbering/credit-payments", "post", "201", "application/json");
        AssertResponse(paths, "/api/document-numbering/credit-payments", "put", "409", "application/problem+json");

        var paymentPost = paths.GetProperty("/api/credits/{creditId}/payments").GetProperty("post");
        var idempotency = Assert.Single(paymentPost.GetProperty("parameters").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", idempotency.GetProperty("in").GetString());
        Assert.True(idempotency.GetProperty("required").GetBoolean());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var requestProperties = schemas.GetProperty("ApplyCreditPaymentRequest").GetProperty("properties");
        Assert.True(requestProperties.TryGetProperty("expectedVersion", out _));
        Assert.True(requestProperties.TryGetProperty("payments", out _));
        Assert.False(requestProperties.TryGetProperty("creditId", out _));
        var paymentProperties = schemas.GetProperty("CreditPaymentMethodRequest").GetProperty("properties");
        Assert.True(paymentProperties.TryGetProperty("reference", out _));
        Assert.False(paymentProperties.TryGetProperty("movementId", out _));
        var numberingProperties = schemas.GetProperty("CreateSalesDocumentNumberingRequest").GetProperty("properties");
        Assert.False(numberingProperties.TryGetProperty("documentType", out _));
        Assert.False(numberingProperties.TryGetProperty("series", out _));
        Assert.False(numberingProperties.TryGetProperty("installationId", out _));
    }

    private static void AssertOperations(JsonElement paths, string path, params string[] expected)
    {
        var route = paths.GetProperty(path);
        Assert.Equal(expected.Order(StringComparer.Ordinal), route.EnumerateObject()
            .Select(x => x.Name).Order(StringComparer.Ordinal));
        foreach (var method in expected)
        {
            var operation = route.GetProperty(method);
            var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
            Assert.True(security.TryGetProperty("Bearer", out var scopes));
            Assert.Empty(scopes.EnumerateArray());
            foreach (var code in new[] { "401", "403" })
                AssertResponse(paths, path, method, code, "application/problem+json");
        }
    }

    private static void AssertResponse(JsonElement paths, string path, string method, string code, string contentType)
    {
        var content = paths.GetProperty(path).GetProperty(method).GetProperty("responses")
            .GetProperty(code).GetProperty("content");
        Assert.True(content.TryGetProperty(contentType, out _),
            $"Missing {contentType} for {code} {method.ToUpperInvariant()} {path}.");
    }
}
