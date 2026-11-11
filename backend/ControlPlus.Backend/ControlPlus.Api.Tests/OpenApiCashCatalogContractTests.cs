using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiCashCatalogContractTests
{
    [Fact]
    public async Task ClosureDifferenceReasons_DeclaresBearerResponsesAndPublicFields()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/cash/closure-difference-reasons").GetProperty("get");
        Assert.True(operation.GetProperty("security").GetArrayLength() > 0);
        Assert.Equal(new[] { "200", "401", "403" }, operation.GetProperty("responses").EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        var properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("CashClosureDifferenceReasonDto").GetProperty("properties");
        Assert.Equal(new[] { "code", "id", "isActive", "name" }, properties.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
    }

    [Fact]
    public async Task ProductInternalCode_IsOptionalOnlyForCreationAndProductRoutesRemainProtected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var create = schemas.GetProperty("CreateProductRequest");
        Assert.True(create.GetProperty("properties").TryGetProperty("internalCode", out _));
        Assert.DoesNotContain("internalCode", Required(create));
        Assert.True(create.GetProperty("properties").TryGetProperty("initialQuantity", out _));
        Assert.True(create.GetProperty("properties").TryGetProperty("initialUnitCost", out _));
        Assert.DoesNotContain("initialQuantity", Required(create));
        Assert.DoesNotContain("initialUnitCost", Required(create));
        Assert.Contains("internalCode", Required(schemas.GetProperty("UpdateProductRequest")));
        Assert.Contains("internalCode", Required(schemas.GetProperty("ProductDto")));
        var paths = document.RootElement.GetProperty("paths");
        var post = paths.GetProperty("/api/products").GetProperty("post");
        Assert.True(post.GetProperty("security").GetArrayLength() > 0);
        Assert.Contains(post.GetProperty("parameters").EnumerateArray(), x =>
            x.GetProperty("name").GetString() == "Idempotency-Key" &&
            (!x.TryGetProperty("required", out var required) || !required.GetBoolean()));
        Assert.True(paths.GetProperty("/api/products/{productId}").GetProperty("put").GetProperty("security").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ProductPhysicalDelete_DeclaresVersionSecurityAndOutcomes()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/products/{productId}").GetProperty("delete");
        Assert.True(operation.GetProperty("security").GetArrayLength() > 0);
        Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), x =>
            x.GetProperty("name").GetString() == "expectedVersion" &&
            x.GetProperty("in").GetString() == "query");
        foreach (var status in new[] { "204", "400", "403", "404", "409" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
    }

    private static string?[] Required(JsonElement schema) => schema.TryGetProperty("required", out var required)
        ? required.EnumerateArray().Select(x => x.GetString()).ToArray() : [];

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb", "Host=127.0.0.1;Port=1;Database=controlplus_test_cash_catalog_openapi;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-cash-catalog-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-cash-catalog-tests");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
}
