using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiPurchaseCancellationContractTests
{
    private const string CancellationPath = "/api/purchases/{purchaseId}/cancel";
    private const string ReasonsPath = "/api/purchases/cancellation-reasons";
    private const string InitializePath = "/api/purchases/cancellation-reasons/initialize";

    [Fact]
    public async Task CancellationAndReasonRoutes_ExposeOnlyApprovedVerbsAndBearerSecurity()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        AssertOperations(paths, CancellationPath, "post");
        AssertOperations(paths, ReasonsPath, "get");
        AssertOperations(paths, InitializePath, "post");

        foreach (var path in paths.EnumerateObject().Where(x => x.Name.StartsWith("/api/purchases", StringComparison.Ordinal)))
            Assert.False(path.Value.TryGetProperty("delete", out _), $"Unexpected DELETE on {path.Name}.");
    }

    [Fact]
    public async Task CancellationAndReasonRoutes_DeclareJsonSuccessAndProblemResponses()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        foreach (var (path, method) in new[]
                 {
                     (CancellationPath, "post"), (ReasonsPath, "get"), (InitializePath, "post")
                 })
        {
            AssertResponse(paths, path, method, "200", "application/json");
            foreach (var code in new[] { "400", "401", "403", "404", "409" })
                AssertResponse(paths, path, method, code, "application/problem+json");
        }
    }

    [Fact]
    public async Task CancellationRequest_DeclaresRequiredIdempotencyHeaderAndOptionalCredentials()
    {
        using var document = await GetDocumentAsync();
        var operation = document.RootElement.GetProperty("paths").GetProperty(CancellationPath).GetProperty("post");
        var header = Assert.Single(operation.GetProperty("parameters").EnumerateArray(), item =>
            item.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
        Assert.True(operation.GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("CancelPurchaseRequest");
        var properties = request.GetProperty("properties");
        Assert.Equal(new[] { "authorization", "expectedVersion", "observation", "reasonId" },
            properties.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        if (request.TryGetProperty("required", out var required))
            Assert.DoesNotContain(required.EnumerateArray(), item => item.GetString() == "authorization");

        var authorization = schemas.GetProperty("CancelPurchaseAuthorizationRequest").GetProperty("properties");
        Assert.Equal(new[] { "credentialToken", "password", "userName" },
            authorization.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
    }

    private static async Task<JsonDocument> GetDocumentAsync()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_openapi_purchase_cancellation_test;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-openapi-purchase-cancellation-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-openapi-purchase-cancellation-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static void AssertOperations(JsonElement paths, string path, params string[] methods)
    {
        var route = paths.GetProperty(path);
        Assert.Equal(methods.Order(StringComparer.Ordinal), route.EnumerateObject()
            .Select(x => x.Name).Order(StringComparer.Ordinal));
        foreach (var method in methods)
        {
            var security = Assert.Single(route.GetProperty(method).GetProperty("security").EnumerateArray());
            Assert.True(security.TryGetProperty("Bearer", out var scopes));
            Assert.Empty(scopes.EnumerateArray());
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
