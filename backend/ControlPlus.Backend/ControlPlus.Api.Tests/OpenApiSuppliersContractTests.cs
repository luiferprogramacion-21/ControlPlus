using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiSuppliersContractTests
{
    [Fact]
    public async Task Suppliers_ExposeExactlyFiveProtectedOperationsAndProblemContracts()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb", "Host=127.0.0.1;Port=1;Database=controlplus_openapi_suppliers_test;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-openapi-suppliers-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-openapi-suppliers-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        Check(paths, "/api/suppliers", "get", "post");
        Check(paths, "/api/suppliers/{supplierId}", "get", "put");
        Check(paths, "/api/suppliers/{supplierId}/status", "put");
        foreach (var (path, method, responses) in new[]
        {
            ("/api/suppliers", "get", new[] { "200", "400", "401", "403" }),
            ("/api/suppliers", "post", new[] { "201", "400", "401", "403", "409" }),
            ("/api/suppliers/{supplierId}", "get", new[] { "200", "401", "403", "404" }),
            ("/api/suppliers/{supplierId}", "put", new[] { "200", "400", "401", "403", "404", "409" }),
            ("/api/suppliers/{supplierId}/status", "put", new[] { "200", "400", "401", "403", "404", "409" })
        })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            Assert.True(Assert.Single(operation.GetProperty("security").EnumerateArray()).TryGetProperty("Bearer", out _));
            foreach (var code in responses)
                Assert.True(operation.GetProperty("responses").GetProperty(code).GetProperty("content")
                    .TryGetProperty(code is "200" or "201" ? "application/json" : "application/problem+json", out _));
        }
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "CreateSupplierRequest", "UpdateSupplierRequest", "SetSupplierStatusRequest" })
        {
            var properties = schemas.GetProperty(name).GetProperty("properties");
            Assert.False(properties.TryGetProperty("id", out _));
            Assert.False(properties.TryGetProperty("version", out _));
            Assert.False(properties.TryGetProperty("createdAt", out _));
        }
    }

    private static void Check(JsonElement paths, string path, params string[] verbs) =>
        Assert.Equal(verbs.Order(StringComparer.Ordinal), paths.GetProperty(path).EnumerateObject()
            .Select(x => x.Name).Order(StringComparer.Ordinal));
}
