using System.Text.Json;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task Document_DescribesBearerSecurityPublicEndpointsAndCatalogResponses()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var paths = root.GetProperty("paths");
        AssertProtected(paths, "/api/auth/me", "get");
        AssertProtected(paths, "/api/auth/refresh", "post");
        AssertResponses(paths, "/api/auth/refresh", "post", "200", "401", "403");
        AssertProtected(paths, "/api/categories", "get");
        AssertProtected(paths, "/api/categories", "post");
        AssertProtected(paths, "/api/products", "get");
        AssertProtected(paths, "/api/products", "post");
        AssertProtected(paths, "/api/users", "get");
        AssertProtected(paths, "/api/roles", "get");
        AssertProtected(paths, "/api/permissions", "get");
        AssertProtected(paths, "/api/audit-records", "get");
        AssertProtected(paths, "/api/cash/state", "get");
        AssertProtected(paths, "/api/cash/register", "post");
        AssertProtected(paths, "/api/cash/shift-mode", "put");
        AssertProtected(paths, "/api/cash/shifts", "post");
        AssertProtected(paths, "/api/cash/movements", "get");
        AssertProtected(paths, "/api/cash/movements/incomes", "post");
        AssertProtected(paths, "/api/cash/movements/expenses", "post");
        AssertProtected(paths, "/api/cash/movements/cash-drops", "post");
        AssertProtected(paths, "/api/cash/reconciliation", "get");
        AssertProtected(paths, "/api/cash/shifts/{shiftId}/close", "post");
        AssertProtected(paths, "/api/cash/operator-credentials/{userId}", "post");
        AssertProtected(paths, "/api/cash/operator-sessions", "post");
        AssertProtected(paths, "/api/cash/operator-sessions/{sessionId}/close", "post");
        AssertProtected(paths, "/api/sales", "post");
        AssertProtected(paths, "/api/sales", "get");
        AssertProtected(paths, "/api/sales/own", "get");
        AssertProtected(paths, "/api/sales/{saleId}", "get");
        AssertProtected(paths, "/api/document-numbering/sales", "get");
        AssertProtected(paths, "/api/document-numbering/sales", "post");
        AssertProtected(paths, "/api/document-numbering/sales", "put");
        AssertProtected(paths, "/api/inventory/stock", "get");
        AssertProtected(paths, "/api/inventory/movements", "get");
        AssertProtected(paths, "/api/inventory/adjustment-reasons", "get");
        AssertProtected(paths, "/api/inventory/adjustments", "post");
        AssertProtected(paths, "/api/customers", "get");
        AssertProtected(paths, "/api/customers", "post");
        AssertProtected(paths, "/api/customers/{customerId}", "get");
        AssertProtected(paths, "/api/customers/{customerId}", "put");
        AssertProtected(paths, "/api/customers/{customerId}/status", "put");
        Assert.False(paths.GetProperty("/api/document-numbering/sales").TryGetProperty("delete", out _));
        Assert.False(paths.GetProperty("/api/customers").TryGetProperty("delete", out _));
        Assert.False(paths.GetProperty("/api/customers/{customerId}").TryGetProperty("delete", out _));
        foreach (var inventoryPath in new[]
                 {
                     "/api/inventory/stock", "/api/inventory/movements",
                     "/api/inventory/adjustment-reasons", "/api/inventory/adjustments"
                 })
            Assert.False(paths.GetProperty(inventoryPath).TryGetProperty("delete", out _));

        AssertAnonymous(paths, "/api/health", "get");
        AssertAnonymous(paths, "/api/auth/login", "post");
        AssertAnonymous(paths, "/api/auth/setup", "post");
        AssertAnonymous(paths, "/api/auth/recover-initial-administrator", "post");
        AssertAnonymous(paths, "/api/auth/password-recovery", "post");
        AssertAnonymous(paths, "/api/auth/password-recovery/confirm", "post");
        AssertResponses(paths, "/api/auth/password-recovery", "post", "202", "400", "429", "503");
        AssertResponses(paths, "/api/auth/password-recovery/confirm", "post", "204", "400", "429");
        Assert.False(paths.TryGetProperty("/api/users/{userId}/roles/{roleId}", out _));

        AssertResponses(paths, "/api/categories", "post", "201", "400", "401", "403", "409");
        AssertResponses(paths, "/api/products", "post", "201", "400", "401", "403", "409");
        AssertResponses(paths, "/api/categories/{categoryId}", "get", "200", "401", "403", "404");
        AssertResponses(paths, "/api/products/{productId}", "get", "200", "401", "403", "404");
        AssertResponses(paths, "/api/cash/register", "post", "201", "400", "401", "403", "404", "409");
        AssertResponses(paths, "/api/cash/shifts", "post", "201", "400", "401", "403", "404", "409");
        AssertResponses(paths, "/api/cash/movements/incomes", "post", "201", "400", "401", "403", "404", "409");
        AssertResponses(paths, "/api/cash/shifts/{shiftId}/close", "post", "200", "400", "401", "403", "404", "409");
        AssertResponses(paths, "/api/cash/operator-sessions", "post", "201", "400", "401", "403", "404", "409");
        AssertResponses(paths, "/api/sales", "post", "201", "400", "401", "403", "404", "409");
        AssertResponses(paths, "/api/sales", "get", "200", "400", "401", "403");
        AssertResponses(paths, "/api/sales/own", "get", "200", "400", "401", "403");
        AssertResponses(paths, "/api/sales/{saleId}", "get", "200", "401", "403", "404");
        AssertResponses(paths, "/api/document-numbering/sales", "get", "200", "400", "401", "403", "404", "409");
        AssertResponses(paths, "/api/document-numbering/sales", "post", "201", "400", "401", "403", "404", "409");
        AssertResponses(paths, "/api/document-numbering/sales", "put", "200", "400", "401", "403", "404", "409");
        AssertResponses(paths, "/api/inventory/stock", "get", "200", "400", "401", "403");
        AssertResponses(paths, "/api/inventory/movements", "get", "200", "400", "401", "403");
        AssertResponses(paths, "/api/inventory/adjustment-reasons", "get", "200", "401", "403");
        AssertResponses(paths, "/api/inventory/adjustments", "post", "201", "400", "401", "403", "404", "409");
        AssertExactResponses(paths, "/api/customers", "get", "200", "400", "401", "403");
        AssertExactResponses(paths, "/api/customers", "post", "201", "400", "401", "403", "409");
        AssertExactResponses(paths, "/api/customers/{customerId}", "get", "200", "401", "403", "404");
        AssertExactResponses(paths, "/api/customers/{customerId}", "put", "200", "400", "401", "403", "404", "409");
        AssertExactResponses(paths, "/api/customers/{customerId}/status", "put", "200", "400", "401", "403", "404", "409");
        AssertResponseContentType(paths, "/api/inventory/adjustments", "post", "201", "application/json");
        AssertResponseContentType(paths, "/api/inventory/adjustments", "post", "400", "application/problem+json");
        AssertResponseContentType(paths, "/api/inventory/adjustments", "post", "403", "application/problem+json");
        AssertResponseContentType(paths, "/api/inventory/adjustments", "post", "409", "application/problem+json");
        AssertResponseContentType(paths, "/api/sales", "post", "201", "application/json");
        AssertResponseContentType(paths, "/api/sales", "post", "409", "application/problem+json");
        AssertResponseContentType(paths, "/api/document-numbering/sales", "get", "200", "application/json");
        AssertResponseContentType(paths, "/api/document-numbering/sales", "get", "409", "application/problem+json");
        AssertResponseContentType(paths, "/api/document-numbering/sales", "post", "201", "application/json");
        AssertResponseContentType(paths, "/api/document-numbering/sales", "post", "409", "application/problem+json");
        AssertResponseContentType(paths, "/api/document-numbering/sales", "put", "200", "application/json");
        AssertResponseContentType(paths, "/api/document-numbering/sales", "put", "409", "application/problem+json");

        var saleParameters = paths.GetProperty("/api/sales").GetProperty("post").GetProperty("parameters");
        var idempotency = Assert.Single(saleParameters.EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", idempotency.GetProperty("in").GetString());
        Assert.True(idempotency.GetProperty("required").GetBoolean());

        var adjustmentParameters = paths.GetProperty("/api/inventory/adjustments").GetProperty("post").GetProperty("parameters");
        var adjustmentIdempotency = Assert.Single(adjustmentParameters.EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", adjustmentIdempotency.GetProperty("in").GetString());
        Assert.True(adjustmentIdempotency.GetProperty("required").GetBoolean());

        var schemas = root.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "CashShiftDto", "CashReconciliationDto" })
        {
            var properties = schemas.GetProperty(name).GetProperty("properties");
            Assert.True(properties.TryGetProperty("cashDifference", out _));
            Assert.True(properties.TryGetProperty("totalDifference", out _));
        }
        var stateProperties = schemas.GetProperty("CashStateDto").GetProperty("properties");
        Assert.True(stateProperties.TryGetProperty("isConfigured", out _));
        Assert.True(stateProperties.TryGetProperty("hasOpenShift", out _));
        Assert.True(stateProperties.TryGetProperty("ownOperatorSession", out _));
        var authorizationProperties = schemas.GetProperty("CashCloseAuthorizationRequest").GetProperty("properties");
        Assert.False(authorizationProperties.TryGetProperty("authorizationId", out _));

        var healthResponse = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.DeleteAsync("/api/document-numbering/sales")).StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting(
                "ConnectionStrings:ControlPlusDb",
                "Host=127.0.0.1;Port=1;Database=controlplus_openapi_test;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-openapi-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-openapi-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });

    private static void AssertProtected(JsonElement paths, string path, string method)
    {
        var operation = paths.GetProperty(path).GetProperty(method);
        var security = Assert.Single(operation.GetProperty("security").EnumerateArray());
        Assert.True(security.TryGetProperty("Bearer", out var scopes));
        Assert.Empty(scopes.EnumerateArray());
    }

    private static void AssertAnonymous(JsonElement paths, string path, string method) =>
        Assert.False(paths.GetProperty(path).GetProperty(method).TryGetProperty("security", out _));

    private static void AssertResponses(JsonElement paths, string path, string method, params string[] expectedCodes)
    {
        var responses = paths.GetProperty(path).GetProperty(method).GetProperty("responses");
        foreach (var code in expectedCodes)
        {
            Assert.True(responses.TryGetProperty(code, out _), $"Missing OpenAPI response {code} for {method.ToUpperInvariant()} {path}.");
        }
    }

    private static void AssertExactResponses(
        JsonElement paths,
        string path,
        string method,
        params string[] expectedCodes)
    {
        var responses = paths.GetProperty(path).GetProperty(method).GetProperty("responses");
        Assert.Equal(
            expectedCodes.Order(StringComparer.Ordinal),
            responses.EnumerateObject().Select(response => response.Name).Order(StringComparer.Ordinal));
    }

    private static void AssertResponseContentType(
        JsonElement paths,
        string path,
        string method,
        string statusCode,
        string contentType)
    {
        var content = paths.GetProperty(path).GetProperty(method).GetProperty("responses")
            .GetProperty(statusCode).GetProperty("content");
        Assert.True(content.TryGetProperty(contentType, out _),
            $"Missing OpenAPI content type {contentType} for {statusCode} {method.ToUpperInvariant()} {path}.");
    }
}
