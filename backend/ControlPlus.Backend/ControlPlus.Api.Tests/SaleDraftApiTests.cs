using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using ControlPlus.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Venta recuperable (decisión del 18 de noviembre de 2026): borrador del carrito en ventas.borrador_venta.</summary>
public sealed class SaleDraftApiTests : IAsyncLifetime
{
    private const string AdminName = "drafts.admin.test";
    private const string SupervisorName = "drafts.supervisor.test";
    private readonly LocalPostgresDatabase _postgres = new();
    private readonly string _masterKey = Secret(48);
    private readonly string _adminPassword = "Admin-" + Secret(12);
    private readonly string _supervisorPassword = "Supervisor-" + Secret(12);
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using (var migrations = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
            await migrations.Database.MigrateAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-drafts-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-drafts-client");
            builder.UseSetting("Jwt:SigningKey", Secret(64));
            builder.UseSetting("Jwt:AccessTokenMinutes", "60");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Draft_IsSavedReplacedReadPerUserAndDiscarded()
    {
        using var client = Client();
        var setup = await BootstrapAsync(client, openShift: true);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/sales/drafts/current")).StatusCode);

        var first = await SaveAsync(client, new SaveSaleDraftRequest("CONTADO", setup.CustomerId,
        [
            new SaleDraftLineRequest(setup.ProductA, 2, "MINORISTA", new SaleDiscountRequest("PORCENTAJE", 5)),
            new SaleDraftLineRequest(setup.ProductB, 1, "minorista")
        ], DiscountReason: "Cliente frecuente"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var current = await CurrentAsync(client);
        Assert.Equal(("CONTADO", setup.CustomerId), (current.SaleType, current.CustomerId));
        Assert.Equal([setup.ProductA, setup.ProductB], current.Lines.Select(x => x.ProductId));
        Assert.Equal(new SaleDiscountRequest("PORCENTAJE", 5), current.Lines.First().Discount);
        Assert.Null(current.GeneralDiscount);
        Assert.Equal("Cliente frecuente", current.DiscountReason);

        // Reemplazo completo: otra línea, descuento a toda la venta y crédito.
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(client, new SaveSaleDraftRequest("CREDITO", setup.CustomerId,
            [new SaleDraftLineRequest(setup.ProductB, 3, "MINORISTA")], new SaleDiscountRequest("VALOR_FIJO", 200), "Acuerdo"))).StatusCode);
        current = await CurrentAsync(client);
        Assert.Equal("CREDITO", current.SaleType);
        Assert.Equal((setup.ProductB, 3), (Assert.Single(current.Lines).ProductId, current.Lines.Single().Quantity));
        Assert.Equal(new SaleDiscountRequest("VALOR_FIJO", 200), current.GeneralDiscount);

        // Rechazos sin tocar lo guardado.
        foreach (var invalid in new[]
        {
            new SaveSaleDraftRequest("CONTADO", null, []),
            new SaveSaleDraftRequest("CONTADO", null, [new SaleDraftLineRequest(Guid.NewGuid(), 1, "MINORISTA")]),
            new SaveSaleDraftRequest("CONTADO", null, [new SaleDraftLineRequest(setup.ProductA, 0, "MINORISTA")]),
            new SaveSaleDraftRequest("APARTADO", null, [new SaleDraftLineRequest(setup.ProductA, 1, "MINORISTA")]),
            new SaveSaleDraftRequest("CONTADO", null, [new SaleDraftLineRequest(setup.ProductA, 1, "MINORISTA", new SaleDiscountRequest("PORCENTAJE", 5))],
                new SaleDiscountRequest("VALOR_FIJO", 100)),
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await SaveAsync(client, invalid)).StatusCode);
        Assert.Equal("CREDITO", (await CurrentAsync(client)).SaleType);

        // Cada usuario ve solo el suyo.
        SetBearer(client, setup.Supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/sales/drafts/current")).StatusCode);
        SetBearer(client, setup.Admin.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/sales/drafts/current")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/sales/drafts/current")).StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var rows = await db.BorradorVenta.AsNoTracking().Include(x => x.DetalleBorradorVenta).ToArrayAsync();
        var row = Assert.Single(rows);
        Assert.Equal("DESCARTADO", row.Estado);
        Assert.NotNull(row.FechaCierre);
        Assert.Single(row.DetalleBorradorVenta);
        Assert.Equal(0, await db.EventoAuditoria.CountAsync(x => x.Entidad == "BorradorVenta"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConfirmingTheSale_ClosesTheDraftAsConvertedInTheSameTransaction()
    {
        using var client = Client();
        var setup = await BootstrapAsync(client, openShift: true);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(client, new SaveSaleDraftRequest("CONTADO", null,
            [new SaleDraftLineRequest(setup.ProductA, 2, "MINORISTA")]))).StatusCode);

        using var sale = new HttpRequestMessage(HttpMethod.Post, "/api/sales")
        {
            Content = JsonContent.Create(new ConfirmSaleRequest(null, [new SaleLineRequest(setup.ProductA, 2, "MINORISTA")],
                null, null, [new SalePaymentRequest("EFECTIVO", 2_000m, 5_000m, null)], "CONTADO"))
        };
        sale.Headers.Add("Idempotency-Key", "draft-sale");
        var confirmed = await client.SendAsync(sale);
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        var saleId = (await confirmed.Content.ReadFromJsonAsync<SaleDto>())!.Id;

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/sales/drafts/current")).StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var draft = await scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>()
            .BorradorVenta.AsNoTracking().SingleAsync();
        Assert.Equal(("CONVERTIDO", (Guid?)saleId), (draft.Estado, draft.VentaConfirmadaId));
        Assert.NotNull(draft.FechaCierre);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task WithoutAnOpenShift_ThereIsNothingToRecover_AndNothingIsSaved()
    {
        using var client = Client();
        var setup = await BootstrapAsync(client, openShift: false);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/sales/drafts/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SaveAsync(client, new SaveSaleDraftRequest("CONTADO", null,
            [new SaleDraftLineRequest(setup.ProductA, 1, "MINORISTA")]))).StatusCode);
        SetBearer(client, string.Empty);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sales/drafts/current")).StatusCode);
    }

    private sealed record Setup(AuthenticationResult Admin, AuthenticationResult Supervisor, Guid ProductA, Guid ProductB, Guid CustomerId);

    private HttpClient Client() => _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<Setup> BootstrapAsync(HttpClient client, bool openShift)
    {
        using (var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest("Comercio ficticio de borradores", "DRAFT-TEST",
                "DRAFT-INSTALLATION", "DRF1", "DRAFT-TERMINAL", "Terminal ficticia", AdminName, "Administrador ficticio", _adminPassword))
        })
        {
            setup.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setup)).StatusCode);
        }
        var admin = await LoginAsync(client, AdminName, _adminPassword);
        SetBearer(client, admin.AccessToken);
        var roles = (await client.GetFromJsonAsync<PagedResult<RoleDto>>("/api/roles?page=1&pageSize=20"))!;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users", new CreateUserRequest(SupervisorName,
            "Supervisor ficticio", _supervisorPassword, [roles.Items.Single(x => x.Code == RoleCodes.Supervisor).Id]))).StatusCode);
        var category = (await (await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Borradores", null, "BOR")))
            .Content.ReadFromJsonAsync<CategoryDto>())!;
        var unit = (await client.GetFromJsonAsync<IReadOnlyCollection<MeasurementUnitDto>>("/api/measurement-units"))!.Single(x => x.Code == "UNIDAD");
        async Task<Guid> ProductAsync(string name, string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products")
            {
                Content = JsonContent.Create(new CreateProductRequest(category.Id, unit.Id, null, null, null, null, true, name, null,
                    1_000m, null, null, 0, 20, 500m))
            };
            request.Headers.Add("Idempotency-Key", key);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ProductDto>())!.Id;
        }
        var productA = await ProductAsync("Producto ficticio A", "draft-product-a");
        var productB = await ProductAsync("Producto ficticio B", "draft-product-b");
        var customer = (await (await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest("CC", "DRAFT-1",
            "Cliente ficticio", "3005550100", "Dirección ficticia", null, null))).Content.ReadFromJsonAsync<CustomerDto>())!;
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/cash/register",
            new ConfigureCashRegisterRequest("DRAFT-CASH", "Caja ficticia"))).StatusCode);
        if (openShift)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/cash/shifts",
                new OpenCashShiftRequest(0m, "Turno de borradores"))).StatusCode);
        var supervisor = await LoginAsync(client, SupervisorName, _supervisorPassword);
        SetBearer(client, admin.AccessToken);
        return new Setup(admin, supervisor, productA, productB, customer.Id);
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, SaveSaleDraftRequest request) =>
        client.PutAsJsonAsync("/api/sales/drafts/current", request);

    private static async Task<SaleDraftDto> CurrentAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/sales/drafts/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SaleDraftDto>())!;
    }

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string userName, string password)
    {
        var saved = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = saved;
        return (await response.Content.ReadFromJsonAsync<AuthenticationResult>())!;
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);

    private static string Secret(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes));
}
