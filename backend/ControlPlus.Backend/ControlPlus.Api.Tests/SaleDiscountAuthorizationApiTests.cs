using ControlPlus.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>
/// A Cajero always needs the credentials of a Supervisor or Administrador (strictly higher role, SALES.DISCOUNT_AUTHORIZE)
/// to apply a discount or sell at the wholesale price; Supervisor and Administrador never do. The reason of a discount is
/// optional for everyone, and the wholesale price needs at least 12 units of the product. Uses only disposable databases.
/// </summary>
public sealed class SaleDiscountAuthorizationApiTests : IAsyncLifetime
{
    private const string AdminName = "discount.auth.admin.test";
    private const string AdminPassword = "Discount-Auth-Admin-Test-2026!";
    private const string SupervisorName = "discount.auth.supervisor.test";
    private const string SupervisorPassword = "Discount-Auth-Supervisor-Test-2026!";
    private const string CashierName = "discount.auth.cashier.test";
    private const string CashierPassword = "Discount-Auth-Cashier-Test-2026!";
    private const string OtherCashierName = "discount.auth.other.cashier.test";
    private const string OtherCashierPassword = "Discount-Auth-Other-Cashier-Test-2026!";

    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtKey = RandomSecret(64);
    private readonly LocalPostgresDatabase _postgres = new LocalPostgresDatabase();
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
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-discount-auth-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-discount-auth-client");
            builder.UseSetting("Jwt:SigningKey", _jwtKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "240");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // ------------------------------------------------------------------------------------------ discounts

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cashier_Discount_WithoutCredentials_IsRejectedEvenWithAReason()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);

        await AssertProblemAsync(await PostSaleAsync(client, "disc-no-credentials",
            DiscountedCash(scenario, "Empaque deteriorado")), HttpStatusCode.BadRequest);
        await AssertNoSaleAsync();
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("Empaque deteriorado")]
    [InlineData(null)]
    public async Task Cashier_Discount_WithSupervisorCredentials_IsAuthorizedAndRecorded_WithOrWithoutReason(string? reason)
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        const string key = "disc-supervisor-authorizes";

        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, key,
            DiscountedCash(scenario, reason) with { Authorization = SupervisorAuthorization }), HttpStatusCode.Created);

        Assert.Equal(950m, sale.Total);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var authorization = await db.AutorizacionOperacion.AsNoTracking().SingleAsync();
        Assert.Equal("DESCUENTO_VENTA", authorization.TipoOperacion);
        Assert.Equal(scenario.Cashier.User.Id, authorization.UsuarioSolicitanteId);
        Assert.Equal(scenario.Supervisor.User.Id, authorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", authorization.MetodoAutenticacion);
        Assert.Equal("EXITOSA", authorization.Resultado);
        Assert.Equal("UTILIZADA", authorization.Estado);
        Assert.Equal("SALES.DISCOUNT_AUTHORIZE", await db.Permiso.Where(x => x.Id == authorization.PermisoId).Select(x => x.Codigo).SingleAsync());
        Assert.Equal(reason, await db.Venta.Where(x => x.Id == sale.Id).Select(x => x.MotivoDescuento).SingleAsync());

        var audit = await ReadAuditTextAsync(db);
        Assert.Contains(scenario.Supervisor.User.Id.ToString(), audit, StringComparison.OrdinalIgnoreCase);
        foreach (var sensitive in new[] { key, SupervisorPassword, CashierPassword, scenario.Cashier.AccessToken })
            Assert.DoesNotContain(sensitive, audit, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cashier_Discount_ReplayWithSameKeyAndAuthorizer_ReturnsTheSameSale()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        var request = DiscountedCash(scenario, null) with { Authorization = SupervisorAuthorization };

        var first = await ReadAsync<SaleDto>(await PostSaleAsync(client, "disc-replay", request), HttpStatusCode.Created);
        var replay = await ReadAsync<SaleDto>(await PostSaleAsync(client, "disc-replay", request), HttpStatusCode.Created);

        Assert.Equal(first.Id, replay.Id);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.Venta.CountAsync());
        Assert.Equal(1, await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "DESCUENTO_VENTA"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cashier_CannotAuthorizeWithAnotherCashier_OrWithThemself()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);

        foreach (var (name, password, key) in new[]
                 {
                     (OtherCashierName, OtherCashierPassword, "disc-other-cashier"),
                     (CashierName, CashierPassword, "disc-self")
                 })
            await AssertProblemAsync(await PostSaleAsync(client, key,
                DiscountedCash(scenario, null) with { Authorization = new SaleCreditAuthorizationRequest(name, password) }),
                HttpStatusCode.Forbidden);
        await AssertNoSaleAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cashier_Discount_WithWrongPassword_KeepsOnlyTheFailureAccounting()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);

        await AssertProblemAsync(await PostSaleAsync(client, "disc-wrong-password",
            DiscountedCash(scenario, null) with { Authorization = new SaleCreditAuthorizationRequest(SupervisorName, "Clave-Incorrecta-2026!") }),
            HttpStatusCode.Forbidden);

        await AssertNoSaleAsync();
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.Usuario.Where(x => x.Id == scenario.Supervisor.User.Id).Select(x => x.IntentosFallidos).SingleAsync());
        Assert.Single(await db.EventoAuditoria.Where(x => x.Accion == "SALEDISCOUNTAUTHORIZATIONREJECTED").ToArrayAsync());
        Assert.DoesNotContain("Clave-Incorrecta-2026!", await ReadAuditTextAsync(db), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cashier_WithoutDiscountOrWholesale_CannotSendAuthorization()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);

        await AssertProblemAsync(await PostSaleAsync(client, "disc-unneeded",
            CashRequest(scenario.ProductId, 1, 1_000m) with { Authorization = SupervisorAuthorization }),
            HttpStatusCode.BadRequest);
        await AssertNoSaleAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CashierCreditSale_WithDiscount_UsesTheSameCredentialsForBothAuthorizations()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        var request = new ConfirmSaleRequest(scenario.CustomerId,
            [new SaleLineRequest(scenario.ProductId, 1, "MINORISTA")], new SaleDiscountRequest("PORCENTAJE", 5), null,
            [], "CREDITO", scenario.Shift.OperatingDate.AddDays(1), SupervisorAuthorization);

        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "disc-credit", request), HttpStatusCode.Created);

        Assert.Equal(950m, sale.Total);
        Assert.NotNull(sale.CreditId);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(["CREAR_CREDITO", "DESCUENTO_VENTA"], await db.AutorizacionOperacion.Select(x => x.TipoOperacion).OrderBy(x => x).ToArrayAsync());
        Assert.All(await db.AutorizacionOperacion.ToArrayAsync(), x => Assert.Equal(scenario.Supervisor.User.Id, x.UsuarioAutorizadorId));
    }

    // ------------------------------------------------------------------------------------------ wholesale

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cashier_Wholesale_NeedsCredentials_AndIsRecordedWithTheWholesalePrice()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);

        await AssertProblemAsync(await PostSaleAsync(client, "wholesale-no-credentials", Wholesale(scenario, 12)), HttpStatusCode.BadRequest);
        await AssertNoSaleAsync();

        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "wholesale-authorized",
            Wholesale(scenario, 12) with { Authorization = SupervisorAuthorization }), HttpStatusCode.Created);

        var line = Assert.Single(sale.Lines);
        Assert.Equal("MAYORISTA", line.PriceType);
        Assert.Equal(800m, line.UnitPrice);
        Assert.Equal(9_600m, sale.Total);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var authorization = await db.AutorizacionOperacion.AsNoTracking().SingleAsync();
        Assert.Equal("PRECIO_MAYORISTA", authorization.TipoOperacion);
        Assert.Equal(scenario.Supervisor.User.Id, authorization.UsuarioAutorizadorId);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("cashier")]
    [InlineData("supervisor")]
    [InlineData("admin")]
    public async Task Wholesale_BelowTwelveUnits_IsRejectedForEveryRole(string role)
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var actor = role switch { "cashier" => scenario.Cashier, "supervisor" => scenario.Supervisor, _ => scenario.Admin };
        _ = await StartSessionAsync(client, scenario, actor);
        var request = Wholesale(scenario, 11);
        if (role == "cashier") request = request with { Authorization = SupervisorAuthorization };

        var response = await PostSaleAsync(client, "wholesale-eleven", request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Contains("12", await response.Content.ReadAsStringAsync());
        await AssertNoSaleAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Wholesale_UsesTheMinimumConfiguredByTheAdministrator()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var settings = await ReadAsync<SalesSettingsDto>(await client.GetAsync("/api/sales/settings"), HttpStatusCode.OK);
        _ = await ReadAsync<SalesSettingsDto>(await client.PutAsJsonAsync("/api/sales/settings",
            new UpdateSalesSettingsRequest(3, settings.Version)), HttpStatusCode.OK);

        _ = await StartSessionAsync(client, scenario, scenario.Admin);
        var belowMinimum = await PostSaleAsync(client, "wholesale-two", Wholesale(scenario, 2));
        await AssertProblemAsync(belowMinimum, HttpStatusCode.BadRequest);
        Assert.Contains("3", await belowMinimum.Content.ReadAsStringAsync());

        var atMinimum = await ReadAsync<SaleDto>(await PostSaleAsync(client, "wholesale-three", Wholesale(scenario, 3)), HttpStatusCode.Created);
        Assert.Equal(2_400m, atMinimum.Total);
        Assert.Equal("MAYORISTA", Assert.Single(atMinimum.Lines).PriceType);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cashier_DiscountAndWholesaleTogether_RecordBothAuthorizationsWithOneAuthorizer()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        var request = new ConfirmSaleRequest(null,
            [new SaleLineRequest(scenario.ProductId, 12, "MAYORISTA"),
             new SaleLineRequest(scenario.SecondProductId, 1, "MINORISTA", new SaleDiscountRequest("PORCENTAJE", 5))],
            null, null, [new SalePaymentRequest("EFECTIVO", 11_500m, 11_500m, null)], Authorization: SupervisorAuthorization);

        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "both-concessions", request), HttpStatusCode.Created);

        Assert.Equal(11_500m, sale.Total);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(["DESCUENTO_VENTA", "PRECIO_MAYORISTA"], await db.AutorizacionOperacion.Select(x => x.TipoOperacion).OrderBy(x => x).ToArrayAsync());
    }

    // ------------------------------------------------------------------------------------------ Supervisor and Administrador

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("supervisor")]
    [InlineData("admin")]
    public async Task SupervisorAndAdministrator_DiscountAndWholesale_NeedNoAuthorization_AndCannotSendOne(string role)
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var actor = role == "supervisor" ? scenario.Supervisor : scenario.Admin;
        _ = await StartSessionAsync(client, scenario, actor);

        await AssertProblemAsync(await PostSaleAsync(client, "free-with-auth",
            DiscountedCash(scenario, null) with { Authorization = new SaleCreditAuthorizationRequest(AdminName, AdminPassword) }),
            HttpStatusCode.BadRequest);
        var discounted = await ReadAsync<SaleDto>(await PostSaleAsync(client, "free-discount", DiscountedCash(scenario, null)), HttpStatusCode.Created);
        Assert.Equal(950m, discounted.Total);

        _ = await StartSessionAsync(client, scenario, actor);
        var wholesale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "free-wholesale", Wholesale(scenario, 12)), HttpStatusCode.Created);
        Assert.Equal(9_600m, wholesale.Total);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Empty(await db.AutorizacionOperacion.ToArrayAsync());
    }

    // ------------------------------------------------------------------------------------------ verification at apply time

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("DISCOUNT")]
    [InlineData("WHOLESALE")]
    public async Task Verify_AcceptsASupervisor_AndCreatesNothing(string kind)
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Cashier.AccessToken);

        var result = await ReadAsync<ConcessionAuthorizationDto>(await client.PostAsJsonAsync(
            "/api/sales/concession-authorizations/verify",
            new VerifyConcessionAuthorizationRequest(SupervisorName, SupervisorPassword, kind)), HttpStatusCode.OK);

        Assert.Equal("Supervisor ficticio de descuentos", result.AuthorizedBy);
        await AssertNoSaleAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Verify_RejectsOtherCashiersWrongPasswordsAndUnneededRoles()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Cashier.AccessToken);

        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales/concession-authorizations/verify",
            new VerifyConcessionAuthorizationRequest(OtherCashierName, OtherCashierPassword, "DISCOUNT")), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales/concession-authorizations/verify",
            new VerifyConcessionAuthorizationRequest(SupervisorName, "Clave-Incorrecta-2026!", "DISCOUNT")), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales/concession-authorizations/verify",
            new VerifyConcessionAuthorizationRequest(SupervisorName, SupervisorPassword, "OTRO")), HttpStatusCode.BadRequest);

        SetBearer(client, scenario.Supervisor.AccessToken);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales/concession-authorizations/verify",
            new VerifyConcessionAuthorizationRequest(AdminName, AdminPassword, "DISCOUNT")), HttpStatusCode.BadRequest);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.Usuario.Where(x => x.Id == scenario.Supervisor.User.Id).Select(x => x.IntentosFallidos).SingleAsync());
        Assert.Equal(0, await db.AutorizacionOperacion.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cashier_Discount_WithScannedSupervisorCredential_IsAuthorizedAndRecordedWithTheCredential()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        var token = await IssueCredentialAsync(client, scenario, scenario.Supervisor.User.Id);
        SetBearer(client, scenario.Cashier.AccessToken);

        var verified = await ReadAsync<ConcessionAuthorizationDto>(await client.PostAsJsonAsync(
            "/api/sales/concession-authorizations/verify",
            new VerifyConcessionAuthorizationRequest(null, null, "DISCOUNT", token)), HttpStatusCode.OK);
        Assert.Equal("Supervisor ficticio de descuentos", verified.AuthorizedBy);

        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "disc-scanned-credential",
            DiscountedCash(scenario, null) with { Authorization = new SaleCreditAuthorizationRequest(null, null, token) }),
            HttpStatusCode.Created);

        Assert.Equal(950m, sale.Total);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var authorization = await db.AutorizacionOperacion.AsNoTracking().SingleAsync(x => x.TipoOperacion == "DESCUENTO_VENTA");
        Assert.Equal(scenario.Supervisor.User.Id, authorization.UsuarioAutorizadorId);
        Assert.Equal("CODIGO_BARRAS_PIN", authorization.MetodoAutenticacion);
        var credentialId = await db.CredencialUsuario.Where(x => x.UsuarioId == scenario.Supervisor.User.Id).Select(x => x.Id).SingleAsync();
        Assert.Equal(credentialId, authorization.CredencialAutorizadorId);
        var audit = await ReadAuditTextAsync(db);
        Assert.DoesNotContain(token, audit, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cashier_Discount_WithUnknownOrUnauthorizedScannedCredential_IsRejected()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        var otherCashierToken = await IssueCredentialAsync(client, scenario, scenario.OtherCashierId);
        SetBearer(client, scenario.Cashier.AccessToken);

        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales/concession-authorizations/verify",
            new VerifyConcessionAuthorizationRequest(null, null, "DISCOUNT", "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")),
            HttpStatusCode.Forbidden);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales/concession-authorizations/verify",
            new VerifyConcessionAuthorizationRequest(null, null, "DISCOUNT", otherCashierToken)), HttpStatusCode.Forbidden);
        // The scanned credential replaces the user name and password; sending both is incomplete.
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales/concession-authorizations/verify",
            new VerifyConcessionAuthorizationRequest(SupervisorName, SupervisorPassword, "DISCOUNT", otherCashierToken)),
            HttpStatusCode.BadRequest);
        await AssertProblemAsync(await PostSaleAsync(client, "disc-bad-credential",
            DiscountedCash(scenario, null) with { Authorization = new SaleCreditAuthorizationRequest(null, null, "no-existe") }),
            HttpStatusCode.Forbidden);
        await AssertNoSaleAsync();
    }

    // ------------------------------------------------------------------------------------------ helpers

    private static SaleCreditAuthorizationRequest SupervisorAuthorization => new(SupervisorName, SupervisorPassword);

    private static ConfirmSaleRequest DiscountedCash(Scenario scenario, string? reason) => new(
        null, [new SaleLineRequest(scenario.ProductId, 1, "MINORISTA")], new SaleDiscountRequest("PORCENTAJE", 5), reason,
        [new SalePaymentRequest("EFECTIVO", 950m, 950m, null)]);

    private static ConfirmSaleRequest Wholesale(Scenario scenario, int quantity) => new(
        null, [new SaleLineRequest(scenario.ProductId, quantity, "MAYORISTA")], null, null,
        [new SalePaymentRequest("EFECTIVO", 800m * quantity, 800m * quantity, null)]);

    private static ConfirmSaleRequest CashRequest(Guid productId, int quantity, decimal total) => new(
        null, [new SaleLineRequest(productId, quantity, "MINORISTA")], null, null,
        [new SalePaymentRequest("EFECTIVO", total, total, null)]);

    private async Task AssertNoSaleAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(0, await db.Venta.CountAsync());
        Assert.Equal(0, await db.PagoVenta.CountAsync());
        Assert.Equal(0, await db.AutorizacionOperacion.CountAsync());
        Assert.All(await db.Producto.Select(x => x.StockActual).ToArrayAsync(), stock => Assert.Equal(100, stock));
    }

    private HttpClient CreateClient() =>
        _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<Scenario> BootstrapAsync(HttpClient client)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
               { Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                   "Comercio ficticio de descuentos", "DISCOUNT-AUTH-TEST", "DISCOUNT-AUTH-INSTALLATION",
                   "DISC1", "DISCOUNT-AUTH-TERMINAL", "Terminal ficticia de descuentos",
                   AdminName, "Administrador ficticio de descuentos", AdminPassword)) })
        {
            request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        }
        var admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin.AccessToken);
        var supervisorRole = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRole = await GetRoleIdAsync(client, RoleCodes.Cashier);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorName, "Supervisor ficticio de descuentos", SupervisorPassword, [supervisorRole])), HttpStatusCode.Created);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierName, "Cajero ficticio de descuentos", CashierPassword, [cashierRole])), HttpStatusCode.Created);
        var otherCashier = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(OtherCashierName, "Otro cajero ficticio", OtherCashierPassword, [cashierRole])), HttpStatusCode.Created);

        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Categoría de descuentos aislada", null, "DCA")), HttpStatusCode.Created);
        var unit = (await ReadAsync<IReadOnlyCollection<MeasurementUnitDto>>(await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK))
            .Single(x => x.Code == "UNIDAD").Id;
        var product = await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, unit, null, "DISCOUNT-AUTH-ITEM", null, null, true,
                "Producto aislado de descuentos", null, 1_000m, 800m, null, 0)), HttpStatusCode.Created);
        var second = await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, unit, null, "DISCOUNT-AUTH-ITEM-2", null, null, true,
                "Segundo producto aislado", null, 2_000m, null, null, 0)), HttpStatusCode.Created);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            foreach (var (id, cost) in new[] { (product.Id, 500m), (second.Id, 1_000m) })
            {
                var row = await db.Producto.SingleAsync(x => x.Id == id);
                row.StockActual = 100;
                row.CostoPromedio = cost;
            }
            await db.SaveChangesAsync();
        }
        var customerId = await CreateCustomerAsync(admin.User.Id);
        await ReadAsync<CashRegisterDto>(await client.PostAsJsonAsync("/api/cash/register",
            new ConfigureCashRegisterRequest("DISCOUNT-CASH", "Caja ficticia de descuentos")), HttpStatusCode.Created);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        await ReadAsync<CashStateDto>(await client.PutAsJsonAsync("/api/cash/shift-mode",
            new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        var shift = await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Turno de descuentos aislado")), HttpStatusCode.Created);
        var supervisor = await LoginAsync(client, SupervisorName, SupervisorPassword);
        var cashier = await LoginAsync(client, CashierName, CashierPassword);
        return new Scenario(admin, supervisor, cashier, product.Id, second.Id, customerId, shift, otherCashier.Id);
    }

    private static async Task<string> IssueCredentialAsync(HttpClient client, Scenario scenario, Guid userId)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        var issued = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{userId}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        return issued.CredentialToken;
    }

    private async Task<Guid> CreateCustomerAsync(Guid creatorId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var customer = new Cliente
        {
            Id = Guid.CreateVersion7(), TipoDocumento = "CC", NumeroDocumento = $"DISC-{Guid.NewGuid():N}"[..30],
            NombreCompleto = "Cliente ficticio de descuentos", Telefono = "3000000000", Direccion = "Dirección ficticia",
            Activo = true, UsuarioCreacionId = creatorId, FechaCreacion = DateTime.UtcNow, Version = 1
        };
        db.Cliente.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<OperatorSessionDto> StartSessionAsync(HttpClient client, Scenario scenario, AuthenticationResult actor)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        var credential = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{actor.User.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        SetBearer(client, actor.AccessToken);
        return await ReadAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(credential.CredentialToken)), HttpStatusCode.Created);
    }

    private static async Task<HttpResponseMessage> PostSaleAsync(HttpClient client, string key, ConfirmSaleRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/sales") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string name, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(name, password)), HttpStatusCode.OK);
    }

    private static async Task<Guid> GetRoleIdAsync(HttpClient client, string code)
    {
        var roles = await ReadAsync<PagedResult<RoleDto>>(await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        return roles.Items.Single(x => x.Code == code).Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} ({expected}) but received {(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
        var value = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        return value;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("code").GetString()));
    }

    private static async Task<string> ReadAuditTextAsync(OfficialControlPlusDbContext db)
    {
        var text = new StringBuilder();
        foreach (var audit in await db.EventoAuditoria.AsNoTracking().ToArrayAsync())
        {
            foreach (var data in new[] { audit.DatosAnteriores, audit.DatosNuevos })
            {
                if (data is null) continue;
                using var json = JsonDocument.Parse(data);
                text.Append(json.RootElement.GetRawText());
            }
            text.Append(audit.Motivo);
        }
        return text.ToString();
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string RandomSecret(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private sealed record Scenario(
        AuthenticationResult Admin, AuthenticationResult Supervisor, AuthenticationResult Cashier,
        Guid ProductId, Guid SecondProductId, Guid CustomerId, CashShiftDto Shift, Guid OtherCashierId);
}
