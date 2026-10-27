using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class OpenApiCustomerHistoryContractTests
{
    [Fact]
    public async Task History_ExposesProtectedGetPaginationPublicProjectionAndProblemContracts()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            // Deliberately unreachable: OpenAPI must not connect to a database.
            builder.UseSetting("ConnectionStrings:ControlPlusDb", "Host=127.0.0.1;Port=1;Database=controlplus_test_openapi_history;Username=test;Password=test");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "controlplus-openapi-history-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-openapi-history-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var route = root.GetProperty("paths").GetProperty("/api/customers/{customerId}/history");
        Assert.Equal("get", Assert.Single(route.EnumerateObject()).Name);
        var get = route.GetProperty("get");
        Assert.False(get.TryGetProperty("requestBody", out _));
        var security = Assert.Single(get.GetProperty("security").EnumerateArray());
        Assert.Empty(security.GetProperty("Bearer").EnumerateArray());
        var parameters = get.GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Equal(new[] { "customerId", "page", "pageSize" }, parameters.Select(x => x.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        var customer = parameters.Single(x => x.GetProperty("name").GetString() == "customerId");
        Assert.Equal("path", customer.GetProperty("in").GetString());
        Assert.True(customer.GetProperty("required").GetBoolean());
        Assert.Equal("uuid", customer.GetProperty("schema").GetProperty("format").GetString());
        foreach (var (name, defaultValue) in new[] { ("page", 1), ("pageSize", 20) })
        {
            var parameter = parameters.Single(x => x.GetProperty("name").GetString() == name);
            Assert.Equal("query", parameter.GetProperty("in").GetString());
            Assert.Equal(defaultValue, parameter.GetProperty("schema").GetProperty("default").GetInt32());
        }
        var responses = get.GetProperty("responses");
        Assert.Equal(new[] { "200", "400", "401", "403", "404" }, responses.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        foreach (var code in new[] { "400", "401", "403", "404" })
            Assert.True(responses.GetProperty(code).GetProperty("content").TryGetProperty("application/problem+json", out _));
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var pageSchema = Resolve(schemas, responses.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        AssertProperties(pageSchema.GetProperty("properties"), "items", "page", "pageSize", "totalCount", "totalPages");
        var item = Resolve(schemas, pageSchema.GetProperty("properties").GetProperty("items").GetProperty("items"));
        var properties = item.GetProperty("properties");
        AssertProperties(properties, "id", "type", "parentId", "number", "occurredAt", "status", "amount", "outstandingAmount");
        Assert.Equal("date-time", properties.GetProperty("occurredAt").GetProperty("format").GetString());
        var amountType = properties.GetProperty("amount").GetProperty("type");
        Assert.Contains("number", amountType.ValueKind == JsonValueKind.Array
            ? amountType.EnumerateArray().Select(x => x.GetString()) : [amountType.GetString()]);
    }

    private static JsonElement Resolve(JsonElement schemas, JsonElement schema) =>
        schema.TryGetProperty("$ref", out var reference) ? schemas.GetProperty(reference.GetString()!.Split('/')[^1]) : schema;
    private static void AssertProperties(JsonElement properties, params string[] names) =>
        Assert.Equal(names.Order(StringComparer.Ordinal), properties.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
}
