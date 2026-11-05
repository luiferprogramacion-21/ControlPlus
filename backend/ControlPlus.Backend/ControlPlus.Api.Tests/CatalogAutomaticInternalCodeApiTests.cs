using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Tests;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CatalogAutomaticInternalCodeApiTests : IAsyncLifetime
{
    private const string AdminName = "catalog.internal-code.admin.test";
    private const string AdminPassword = "Catalog-Internal-Code-Test-2026!";
    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtKey = RandomSecret(64);
    private readonly LocalPostgresDatabase _postgres = new();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var context = new ControlPlusDbContext(
                         new DbContextOptionsBuilder<ControlPlusDbContext>()
                             .UseNpgsql(_postgres.GetConnectionString()).Options))
            await context.Database.MigrateAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-catalog-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-catalog-tests");
            builder.UseSetting("Jwt:SigningKey", _jwtKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "30");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateWithoutInternalCode_GeneratesUniqueCodesForConcurrentProducts()
    {
        using var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var setup = new SetupFirstAdministratorRequest(
            "Catálogo de prueba", "CATALOG-TEST", "CATALOG-INTERNAL-CODE", "CATALOG1",
            "CATALOG-TERM", "Terminal de prueba", AdminName, "Administrador de prueba", AdminPassword);
        using var setupRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(setup)
        };
        setupRequest.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setupRequest)).StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(AdminName, AdminPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authentication = await login.Content.ReadFromJsonAsync<AuthenticationResult>();
        Assert.NotNull(authentication);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authentication.AccessToken);

        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Categoría temporal", null, "CAT"));
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var category = await categoryResponse.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(category);
        var units = await client.GetFromJsonAsync<MeasurementUnitDto[]>("/api/measurement-units");
        Assert.NotNull(units);
        var unit = Assert.Single(units, x => x.Code == "UNIDAD");

        var responses = await Task.WhenAll(Enumerable.Range(1, 12).Select(async index =>
        {
            var response = await client.PostAsJsonAsync("/api/products", new
            {
                categoryId = category.Id,
                measurementUnitId = unit.Id,
                supplierId = (Guid?)null,
                barcode = (string?)null,
                barcodeFormat = (string?)null,
                generateBarcode = false,
                name = $"Producto concurrente {index}",
                description = (string?)null,
                retailPrice = 1000,
                wholesalePrice = (decimal?)null,
                taxPercentage = (decimal?)null,
                minimumStock = 0
            });
            var product = await response.Content.ReadFromJsonAsync<ProductDto>();
            return (response.StatusCode, product);
        }));

        Assert.All(responses, result => Assert.Equal(HttpStatusCode.Created, result.StatusCode));
        var products = responses.Select(result => Assert.IsType<ProductDto>(result.product)).ToArray();
        var codes = products.Select(product => product.InternalCode).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches(new Regex("^CAT-[0-9]{6,}$", RegexOptions.CultureInvariant), code));

        var generatedBarcodeResponse = await client.PostAsJsonAsync("/api/products", new
        {
            categoryId = category.Id,
            measurementUnitId = unit.Id,
            supplierId = (Guid?)null,
            barcode = (string?)null,
            barcodeFormat = (string?)null,
            generateBarcode = true,
            name = "Producto con barras generadas",
            description = (string?)null,
            retailPrice = 1000,
            wholesalePrice = (decimal?)null,
            taxPercentage = (decimal?)null,
            minimumStock = 0
        });
        Assert.Equal(HttpStatusCode.Created, generatedBarcodeResponse.StatusCode);
        var generatedBarcodeProduct = await generatedBarcodeResponse.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(generatedBarcodeProduct);
        Assert.Equal("CODE128", generatedBarcodeProduct.BarcodeFormat);
        Assert.Equal(ProductCode.GenerateOwnedBarcode(generatedBarcodeProduct.InternalCode), generatedBarcodeProduct.Barcode);
    }

    private static string RandomSecret(int length)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var value = new StringBuilder(length);
        foreach (var item in bytes) value.Append(alphabet[item % alphabet.Length]);
        return value.ToString();
    }
}
