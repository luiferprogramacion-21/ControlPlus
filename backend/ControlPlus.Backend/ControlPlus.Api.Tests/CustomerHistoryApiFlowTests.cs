using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using ControlPlus.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Real API operations and official constraints, exclusively in a fixture-owned controlplus_test_* database.</summary>
public sealed class CustomerHistoryApiFlowTests : IAsyncLifetime
{
    private const string AdminName = "history.admin.test";
    private const string SupervisorName = "history.supervisor.test";
    private const string CashierName = "history.cashier.test";
    private readonly string _adminPassword = "History-Admin-" + RandomSecret();
    private readonly string _supervisorPassword = "History-Supervisor-" + RandomSecret();
    private readonly string _cashierPassword = "History-Cashier-" + RandomSecret();
    private readonly LocalPostgresDatabase _postgres = new();
    private readonly FrozenClock _clock = new();
    private WebApplicationFactory<Program> _factory = null!;
    private Dictionary<string, Guid> _permissionIds = [];

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Assert.StartsWith("controlplus_test_", new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        await using (var migrations = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
            await migrations.Database.MigrateAsync();
        var masterKey = RandomSecret();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-history-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-history-client");
            builder.UseSetting("Jwt:SigningKey", RandomSecret());
            builder.UseSetting("Jwt:AccessTokenMinutes", "240");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
            });
        });
        _masterKey = masterKey;
    }

    private string _masterKey = null!;

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact, Trait("Category", "Integration")]
    public async Task History_SalesScope_HonorsOwnAllGrantsAndRevocations()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var group = await CreateOperationsAsync(client, scenario, scenario.CustomerId);
        var token = await SetScopeAsync(client, scenario, own: true, all: false, credits: false, layaways: false);
        SetBearer(client, token.AccessToken);
        AssertIds(await HistoryAsync(client, scenario.CustomerId), group.OwnSale.Id);

        token = await SetScopeAsync(client, scenario, own: true, all: true, credits: false, layaways: false);
        SetBearer(client, token.AccessToken);
        AssertIds(await HistoryAsync(client, scenario.CustomerId), group.OwnSale.Id, group.OtherSale.Id, group.CreditSale.Id);

        // ALL_READ is sufficient even when OWN_READ has an explicit individual revocation.
        token = await SetScopeAsync(client, scenario, own: false, all: true, credits: false, layaways: false);
        SetBearer(client, token.AccessToken);
        AssertIds(await HistoryAsync(client, scenario.CustomerId), group.OwnSale.Id, group.OtherSale.Id, group.CreditSale.Id);

        token = await SetScopeAsync(client, scenario, own: false, all: false, credits: false, layaways: false);
        SetBearer(client, token.AccessToken);
        AssertIds(await HistoryAsync(client, scenario.CustomerId));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task History_ModulePermissions_FilterCreditsPaymentsAndLayawaysIndependently()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var group = await CreateOperationsAsync(client, scenario, scenario.CustomerId);
        foreach (var credits in new[] { false, true })
        foreach (var layaways in new[] { false, true })
        {
            var token = await SetScopeAsync(client, scenario, own: false, all: false, credits, layaways);
            SetBearer(client, token.AccessToken);
            var page = await HistoryAsync(client, scenario.CustomerId);
            var expected = new List<Guid>();
            if (credits) expected.AddRange([group.Credit.Id, .. group.CreditPayments.Payments.Select(x => x.Id)]);
            if (layaways) expected.AddRange([group.Layaway.Id, .. group.LayawayPaymentIds]);
            AssertIds(page, expected.ToArray());
            Assert.Equal(credits ? 1 : 0, page.Items.Count(x => x.Type == "CREDIT"));
            Assert.Equal(credits ? 2 : 0, page.Items.Count(x => x.Type == "CREDIT_PAYMENT"));
            Assert.Equal(layaways ? 1 : 0, page.Items.Count(x => x.Type == "LAYAWAY"));
            Assert.Equal(layaways ? 3 : 0, page.Items.Count(x => x.Type == "LAYAWAY_PAYMENT"));
            Assert.DoesNotContain(page.Items, x => x.Type == "SALE");
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task History_IsolatesInstallationAndCustomerForEveryType()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var group = await CreateOperationsAsync(client, scenario, scenario.CustomerId);
        var otherCustomer = await CreateCustomerAsync(client, scenario.Admin.AccessToken, "Otro cliente");
        var otherGroup = await CreateOperationsAsync(client, scenario, otherCustomer.Id);
        SetBearer(client, scenario.Admin.AccessToken);
        AssertIds(await HistoryAsync(client, scenario.CustomerId), group.AllIds);
        AssertIds(await HistoryAsync(client, otherCustomer.Id), otherGroup.AllIds);

        // Historical operations retain their installation and relationships. Switch only the active installation.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var installation = await db.Instalacion.SingleAsync(x => x.Activo);
            installation.Activo = false;
            db.Instalacion.Add(new Instalacion
            {
                Id = Guid.CreateVersion7(), EstablecimientoId = installation.EstablecimientoId,
                Codigo = "HISTORY-OTHER", Serie = "HISTORY2", Activo = true,
                FechaInstalacion = _clock.UtcNow.UtcDateTime, VersionAplicacion = "test",
                VersionEsquema = "V3", Version = 1
            });
            await db.SaveChangesAsync();
        }
        AssertIds(await HistoryAsync(client, scenario.CustomerId));
        AssertIds(await HistoryAsync(client, otherCustomer.Id));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.Equal(6, await db.Venta.CountAsync());
            Assert.Equal(2, await db.Credito.CountAsync());
            Assert.Equal(4, await db.PagoCredito.CountAsync());
            Assert.Equal(2, await db.Apartado.CountAsync());
            Assert.Equal(6, await db.PagoApartado.CountAsync());
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task History_Pagination_CountsOnlyVisibleOperations()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var group = await CreateOperationsAsync(client, scenario, scenario.CustomerId);
        var otherCustomer = await CreateCustomerAsync(client, scenario.Admin.AccessToken, "No contar");
        _ = await CreateOperationsAsync(client, scenario, otherCustomer.Id);
        var token = await SetScopeAsync(client, scenario, own: true, all: false, credits: false, layaways: true);
        SetBearer(client, token.AccessToken);
        var visible = new[] { group.OwnSale.Id, group.Layaway.Id }.Concat(group.LayawayPaymentIds).ToHashSet();
        var received = new List<Guid>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await HistoryAsync(client, scenario.CustomerId, page, 2);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(3, result.TotalPages);
            Assert.Equal(page, result.Page);
            Assert.Equal(2, result.PageSize);
            Assert.Equal(page < 3 ? 2 : page == 3 ? 1 : 0, result.Items.Count);
            received.AddRange(result.Items.Select(x => x.Id));
        }
        Assert.Equal(5, received.Distinct().Count());
        Assert.True(visible.SetEquals(received));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task History_SameTimestamp_UsesStableTypeAndIdOrdering()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var group = await CreateOperationsAsync(client, scenario, scenario.CustomerId);
        SetBearer(client, scenario.Admin.AccessToken);
        var all = await HistoryAsync(client, scenario.CustomerId);
        Assert.Equal(10, all.TotalCount);
        Assert.All(all.Items, row => Assert.Equal(_clock.UtcNow, row.OccurredAt));
        // IDs from the write operations supply the expected order independently of the history response.
        var expected = new (string Type, Guid Id)[]
        {
            ("SALE", group.OwnSale.Id), ("SALE", group.OtherSale.Id), ("SALE", group.CreditSale.Id),
            ("CREDIT", group.Credit.Id), ("LAYAWAY", group.Layaway.Id)
        }.Concat(group.CreditPayments.Payments.Select(x => ("CREDIT_PAYMENT", x.Id)))
            .Concat(group.LayawayPaymentIds.Select(x => ("LAYAWAY_PAYMENT", x)))
            .OrderBy(x => x.Item1, StringComparer.Ordinal).ThenByDescending(x => x.Item2)
            .Select(x => x.Item2).ToArray();
        Assert.Equal(expected, all.Items.Select(x => x.Id));
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var actual = new List<Guid>();
            for (var page = 1; page <= 5; page++)
                actual.AddRange((await HistoryAsync(client, scenario.CustomerId, page, 2)).Items.Select(x => x.Id));
            Assert.Equal(expected, actual);
            Assert.Equal(10, actual.Distinct().Count());
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task History_PublicProjection_IsReadOnlyAndOmitsCosts()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var group = await CreateOperationsAsync(client, scenario, scenario.CustomerId);
        SetBearer(client, scenario.Admin.AccessToken);
        var before = await DatabaseSnapshotAsync();
        using var response = await client.GetAsync(HistoryUrl(scenario.CustomerId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertProperties(json.RootElement, "items", "page", "pageSize", "totalCount", "totalPages");
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(10, items.Length);
        foreach (var item in items)
        {
            AssertProperties(item, "id", "type", "parentId", "number", "occurredAt", "status", "amount", "outstandingAmount");
            Assert.Equal(TimeSpan.Zero, item.GetProperty("occurredAt").GetDateTimeOffset().Offset);
        }
        var page = await HistoryAsync(client, scenario.CustomerId);
        foreach (var sale in new[] { group.OwnSale, group.OtherSale, group.CreditSale })
        {
            var row = page.Items.Single(x => x.Id == sale.Id);
            Assert.Equal("SALE", row.Type);
            Assert.Equal(sale.VisibleNumber, row.Number);
            Assert.Equal(sale.Total, row.Amount);
            Assert.Equal("CONFIRMADA", row.Status);
            Assert.Null(row.ParentId);
            Assert.Null(row.OutstandingAmount);
        }
        var credit = page.Items.Single(x => x.Type == "CREDIT");
        Assert.Equal(group.CreditSale.Id, credit.ParentId);
        Assert.Equal(group.CreditSale.VisibleNumber, credit.Number);
        Assert.Equal(group.Credit.AdjustedAmount, credit.Amount);
        Assert.Equal(700m, credit.OutstandingAmount);
        Assert.Equal(group.Credit.State, credit.Status);
        foreach (var payment in group.CreditPayments.Payments)
        {
            var row = page.Items.Single(x => x.Id == payment.Id);
            Assert.Equal("CREDIT_PAYMENT", row.Type);
            Assert.Equal(group.Credit.Id, row.ParentId);
            Assert.Equal(payment.Number, row.Number);
            Assert.Equal(payment.AppliedAmount, row.Amount);
            Assert.Equal(payment.State, row.Status);
            Assert.Null(row.OutstandingAmount);
        }
        var layaway = page.Items.Single(x => x.Type == "LAYAWAY");
        Assert.Equal(group.Layaway.VisibleNumber, layaway.Number);
        Assert.Equal(1000m, layaway.Amount);
        Assert.Equal(500m, layaway.OutstandingAmount);
        Assert.Equal("ACTIVO", layaway.Status);
        Assert.Null(layaway.ParentId);
        var payments = page.Items.Where(x => x.Type == "LAYAWAY_PAYMENT").ToArray();
        Assert.Equal(new[] { 100m, 100m, 300m }, payments.Select(x => x.Amount).Order());
        Assert.All(payments, x =>
        {
            Assert.Equal(group.Layaway.Id, x.ParentId);
            Assert.Equal("APLICADO", x.Status);
            Assert.StartsWith("PAP-HISTORY1-", x.Number);
            Assert.Null(x.OutstandingAmount);
        });
        Assert.Equal(before, await DatabaseSnapshotAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task History_CatalogPermission_IsPersistedAssignableAndRevocationWins()
    {
        // Verify the published migration, before bootstrap has an opportunity to seed anything.
        await using (var db = new OfficialControlPlusDbContext(new DbContextOptionsBuilder<OfficialControlPlusDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
        {
            var permission = await db.Permiso.AsNoTracking().SingleAsync(x => x.Codigo == PermissionCodes.CustomersHistoryRead);
            Assert.True(permission.Activo);
            Assert.Equal("CLIENTES", permission.Modulo);
        }
        Assert.Single(DefaultPermissionCatalog.Permissions, x => x.Code == PermissionCodes.CustomersHistoryRead);
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var permissionId = _permissionIds[PermissionCodes.CustomersHistoryRead];
        await SetPermissionAsync(client, scenario, PermissionCodes.CustomersHistoryRead, false);
        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(HistoryUrl(scenario.CustomerId))).StatusCode);
        var revoked = await LoginAsync(client, CashierName, _cashierPassword);
        SetBearer(client, revoked.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(HistoryUrl(scenario.CustomerId))).StatusCode);
        await SetPermissionAsync(client, scenario, PermissionCodes.CustomersHistoryRead, true);
        var granted = await LoginAsync(client, CashierName, _cashierPassword);
        SetBearer(client, granted.AccessToken);
        AssertIds(await HistoryAsync(client, scenario.CustomerId));
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var assignment = await context.UsuarioPermiso.AsNoTracking().SingleAsync(x =>
            x.UsuarioId == scenario.Cashier.User.Id && x.PermisoId == permissionId);
        Assert.Equal("CONCEDER", assignment.Efecto);
    }

    private async Task<Scenario> BootstrapAsync(HttpClient client)
    {
        using var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest("Historial Test", "HISTORY-TEST",
                "HISTORY-INSTALL", "HISTORY1", "HISTORY-TERM", "Terminal de historial",
                AdminName, "Administrador historial", _adminPassword))
        };
        setup.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setup)).StatusCode);
        var admin = await LoginAsync(client, AdminName, _adminPassword);
        SetBearer(client, admin.AccessToken);
        var roles = await ReadAsync<PagedResult<RoleDto>>(await client.GetAsync("/api/roles?pageSize=100"), HttpStatusCode.OK);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            SupervisorName, "Supervisor historial", _supervisorPassword, [roles.Items.Single(x => x.Code == RoleCodes.Supervisor).Id])), HttpStatusCode.Created);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            CashierName, "Cajero historial", _cashierPassword, [roles.Items.Single(x => x.Code == RoleCodes.Cashier).Id])), HttpStatusCode.Created);
        var permissions = await ReadAsync<PagedResult<PermissionDto>>(await client.GetAsync("/api/permissions?pageSize=100"), HttpStatusCode.OK);
        _permissionIds = permissions.Items.ToDictionary(x => x.Code, x => x.Id);
        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Historial", null, "HIS")), HttpStatusCode.Created);
        var units = await ReadAsync<MeasurementUnitDto[]>(await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        var product = await ReadAsync<ProductDto>(await PostAsync(client, "/api/products", new CreateProductRequest(
            category.Id, units.Single(x => x.Code == "UNIDAD").Id, null, "HISTORY-ITEM", null, null, true,
            "Producto historial", null, 1000m, null, null, 0, InitialQuantity: 100, InitialUnitCost: 137m)), HttpStatusCode.Created);
        var customer = await CreateCustomerAsync(client, admin.AccessToken, "Cliente historial");
        _ = await ReadAsync<CashRegisterDto>(await client.PostAsJsonAsync("/api/cash/register", new ConfigureCashRegisterRequest("HISTORY-CASH", "Caja historial")), HttpStatusCode.Created);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        _ = await ReadAsync<CashStateDto>(await client.PutAsJsonAsync("/api/cash/shift-mode", new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        var shift = await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts", new OpenCashShiftRequest(0m, "Historial")), HttpStatusCode.Created);
        var cashier = await LoginAsync(client, CashierName, _cashierPassword);
        var supervisor = await LoginAsync(client, SupervisorName, _supervisorPassword);
        return new Scenario(admin, cashier, supervisor, product.Id, customer.Id, shift);
    }

    private async Task<Operations> CreateOperationsAsync(HttpClient client, Scenario scenario, Guid customerId)
    {
        var own = await SaleAsync(client, scenario, scenario.Cashier, customerId, credit: false);
        var other = await SaleAsync(client, scenario, scenario.Admin, customerId, credit: false);
        var creditSale = await SaleAsync(client, scenario, scenario.Admin, customerId, credit: true);
        SetBearer(client, scenario.Admin.AccessToken);
        var credit = await ReadAsync<CreditDto>(await client.GetAsync($"/api/credits/{creditSale.CreditId}"), HttpStatusCode.OK);
        await StartSessionAsync(client, scenario, scenario.Cashier);
        var creditPayments = await ReadAsync<CreditPaymentOperationDto>(await PostAsync(client,
            $"/api/credits/{credit.Id}/payments", new ApplyCreditPaymentRequest(credit.Version,
                [new("EFECTIVO", 100m, 200m, null), new("NEQUI", 200m, null, "history-credit-reference")])), HttpStatusCode.Created);
        await StartSessionAsync(client, scenario, scenario.Cashier);
        var layaway = await ReadAsync<CreatedLayawayDto>(await PostAsync(client, "/api/layaways", new CreateLayawayRequest(
            customerId, [new(scenario.ProductId, 1, "MINORISTA")],
            [new("EFECTIVO", 100m, 100m, null), new("TRANSFERENCIA", 100m, null, "history-initial-reference")],
            scenario.Shift.OperatingDate.AddDays(10), new(SupervisorName, _supervisorPassword))), HttpStatusCode.Created);
        await StartSessionAsync(client, scenario, scenario.Cashier);
        _ = await ReadAsync<LayawayPaymentOperationDto>(await PostAsync(client, $"/api/layaways/{layaway.Id}/payments",
            new ApplyLayawayPaymentRequest(layaway.Version, [new("EFECTIVO", 300m, 400m, null)])), HttpStatusCode.OK);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var paymentIds = await db.PagoApartado.AsNoTracking().Where(x => x.ApartadoId == layaway.Id).Select(x => x.Id).ToArrayAsync();
        return new Operations(own, other, creditSale, credit, creditPayments, layaway, paymentIds);
    }

    private async Task<SaleDto> SaleAsync(HttpClient client, Scenario scenario, AuthenticationResult actor, Guid customerId, bool credit)
    {
        await StartSessionAsync(client, scenario, actor);
        return await ReadAsync<SaleDto>(await PostAsync(client, "/api/sales", new ConfirmSaleRequest(
            customerId, [new(scenario.ProductId, 1, "MINORISTA")], null, null,
            credit ? [] : [new("EFECTIVO", 1000m, 1000m, null)], credit ? "CREDITO" : "CONTADO",
            credit ? scenario.Shift.OperatingDate.AddDays(10) : null,
            // El Administrador autoriza su propio crédito sin segundo autorizador: un Supervisor no es
            // estrictamente superior a él, así que sus credenciales serían rechazadas (regla del 5 de noviembre).
            credit && actor.User.Id != scenario.Admin.User.Id ? new(SupervisorName, _supervisorPassword) : null)),
            HttpStatusCode.Created);
    }

    private async Task StartSessionAsync(HttpClient client, Scenario scenario, AuthenticationResult actor)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        var credential = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{actor.User.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        SetBearer(client, actor.AccessToken);
        _ = await ReadAsync<OperatorSessionDto>(await client.PostAsJsonAsync("/api/cash/operator-sessions",
            new StartOperatorSessionRequest(credential.CredentialToken)), HttpStatusCode.Created);
    }

    private async Task<AuthenticationResult> SetScopeAsync(HttpClient client, Scenario scenario, bool own, bool all, bool credits, bool layaways)
    {
        foreach (var (code, granted) in new[] { (PermissionCodes.SalesOwnRead, own), (PermissionCodes.SalesAllRead, all),
                     (PermissionCodes.CreditManage, credits), (PermissionCodes.LayawayManage, layaways) })
            await SetPermissionAsync(client, scenario, code, granted);
        return await LoginAsync(client, CashierName, _cashierPassword);
    }

    private async Task SetPermissionAsync(HttpClient client, Scenario scenario, string code, bool granted)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Cashier.User.Id}/permissions/{_permissionIds[code]}", new SetUserPermissionRequest(granted))).StatusCode);
    }

    private async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string token, string name)
    {
        SetBearer(client, token);
        return await ReadAsync<CustomerDto>(await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest(
            "CC", Guid.NewGuid().ToString("N"), name, "3000000000", "Dirección privada", "privado@example.invalid", "Observación privada")), HttpStatusCode.Created);
    }

    private async Task<string> DatabaseSnapshotAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        // Include all business, security and audit data; excludes only migration metadata.
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand("SELECT quote_ident(schemaname) || '.' || quote_ident(tablename) FROM pg_tables WHERE schemaname IN ('catalogo', 'configuracion', 'caja', 'ventas', 'compras', 'seguridad', 'auditoria') ORDER BY 1", connection))
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        var snapshot = new List<string>();
        foreach (var table in tables)
        {
            await using var read = new NpgsqlCommand($"SELECT COALESCE(jsonb_agg(row_json ORDER BY row_json::text)::text, '[]') FROM (SELECT to_jsonb(t) AS row_json FROM {table} t) rows", connection);
            snapshot.Add(table + ":" + (string)(await read.ExecuteScalarAsync())!);
        }
        return string.Join('\n', snapshot);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string user, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user, password)), HttpStatusCode.OK);
    }
    private static async Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string path, T request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(message);
    }
    private static string HistoryUrl(Guid customerId, int page = 1, int pageSize = 100) => $"/api/customers/{customerId}/history?page={page}&pageSize={pageSize}";
    private static Task<PagedResult<CustomerHistoryItem>> HistoryAsync(HttpClient client, Guid customerId, int page = 1, int pageSize = 100) =>
        ReadAsync<PagedResult<CustomerHistoryItem>>(client.GetAsync(HistoryUrl(customerId, page, pageSize)), HttpStatusCode.OK);
    private static async Task<T> ReadAsync<T>(Task<HttpResponseMessage> response, HttpStatusCode expected) => await ReadAsync<T>(await response, expected);
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            var problem = "Sin ProblemDetails JSON.";
            try
            {
                using var json = JsonDocument.Parse(body);
                // Report only public error fields; never dump a successful auth/credential payload.
                problem = string.Join("; ", new[] { "code", "title", "detail" }
                    .Where(name => json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    .Select(name => name + "=" + json.RootElement.GetProperty(name).GetString()));
            }
            catch (JsonException) { }
            Assert.Fail($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: " +
                $"expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. {problem}");
        }
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static void SetBearer(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static void AssertIds(PagedResult<CustomerHistoryItem> page, params Guid[] expected)
    {
        Assert.Equal(expected.Length, page.TotalCount);
        Assert.Equal(expected.Order(), page.Items.Select(x => x.Id).Order());
    }
    private static void AssertProperties(JsonElement element, params string[] names) =>
        Assert.Equal(names.Order(StringComparer.Ordinal), element.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
    private static string RandomSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
    private sealed record Scenario(AuthenticationResult Admin, AuthenticationResult Cashier, AuthenticationResult Supervisor,
        Guid ProductId, Guid CustomerId, CashShiftDto Shift);
    private sealed record Operations(SaleDto OwnSale, SaleDto OtherSale, SaleDto CreditSale, CreditDto Credit,
        CreditPaymentOperationDto CreditPayments, CreatedLayawayDto Layaway, Guid[] LayawayPaymentIds)
    {
        public Guid[] AllIds => [OwnSale.Id, OtherSale.Id, CreditSale.Id, Credit.Id, Layaway.Id,
            .. CreditPayments.Payments.Select(x => x.Id), .. LayawayPaymentIds];
    }
}
