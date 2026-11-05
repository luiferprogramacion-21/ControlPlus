using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CatalogAutomaticInternalCodeApiTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task CategoryAbbreviation_CreateValidationNormalizationAndConflict()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;

        var missing = await client.PostAsJsonAsync("/api/categories", new { name = "Sin prefijo", description = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("abreviatura", (await missing.Content.ReadAsStringAsync()).ToLowerInvariant());

        foreach (var (name, value) in new[]
        {
            ("Una letra", "A"), ("Once letras", "ABCDEFGHIJK"), ("Con dígito", "AB1"),
            ("Con tilde", "ÁBC"), ("Con espacio", "A BC")
        })
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name, null, value))).StatusCode);

        var createdResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Juguetes", null, "jug"));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<CategoryDto>())!;
        Assert.Equal("JUG", created.Abbreviation);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal("JUG", (await db.Categoria.AsNoTracking().SingleAsync(x => x.Id == created.Id)).Abreviatura);

        var duplicate = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Otros juguetes", null, "JUG"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var problem = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("resource.conflict", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("23505", problem.RootElement.ToString());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CategoryAbbreviation_UpdatePreservesOldProductReferenceAndOmission()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var original = (await client.GetFromJsonAsync<CategoryDto>($"/api/categories/{scenario.CategoryId}"))!;
        Assert.Equal("CAT", original.Abbreviation);
        var product = await ReadProductAsync(await client.PostAsJsonAsync("/api/products", ProductRequest(scenario, null)));
        Assert.Equal("CAT-000001", product.InternalCode);

        var omittedResponse = await client.PutAsJsonAsync($"/api/categories/{original.Id}",
            new { name = original.Name, description = original.Description, version = original.Version });
        Assert.Equal(HttpStatusCode.OK, omittedResponse.StatusCode);
        var omitted = (await omittedResponse.Content.ReadFromJsonAsync<CategoryDto>())!;
        Assert.Equal("CAT", omitted.Abbreviation);

        var changedResponse = await client.PutAsJsonAsync($"/api/categories/{original.Id}",
            new UpdateCategoryRequest(omitted.Name, omitted.Description, omitted.Version, "jug"));
        Assert.Equal(HttpStatusCode.OK, changedResponse.StatusCode);
        var changed = (await changedResponse.Content.ReadFromJsonAsync<CategoryDto>())!;
        Assert.Equal("JUG", changed.Abbreviation);
        var stored = (await client.GetFromJsonAsync<ProductDto>($"/api/products/{product.Id}"))!;
        Assert.Equal(product.InternalCode, stored.InternalCode);
        Assert.Equal("CAT-000001", stored.InternalCode);
        var next = await ReadProductAsync(await client.PostAsJsonAsync("/api/products", ProductRequest(scenario, null)));
        Assert.Equal("JUG-000002", next.InternalCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task HistoricalCategoryWithoutAbbreviation_CanBeSavedWithoutAssigningOne()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE catalogo.categoria SET abreviatura=NULL WHERE id={scenario.CategoryId}");
        }
        var historical = (await client.GetFromJsonAsync<CategoryDto>($"/api/categories/{scenario.CategoryId}"))!;
        Assert.Null(historical.Abbreviation);
        var response = await client.PutAsJsonAsync($"/api/categories/{historical.Id}",
            new { name = historical.Name, description = historical.Description, version = historical.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var omitted = (await response.Content.ReadFromJsonAsync<CategoryDto>())!;
        Assert.Null(omitted.Abbreviation);
        var explicitNull = await client.PutAsJsonAsync($"/api/categories/{historical.Id}",
            new UpdateCategoryRequest(omitted.Name, omitted.Description, omitted.Version, null));
        Assert.Equal(HttpStatusCode.OK, explicitNull.StatusCode);
        Assert.Null((await explicitNull.Content.ReadFromJsonAsync<CategoryDto>())!.Abbreviation);
    }
}
