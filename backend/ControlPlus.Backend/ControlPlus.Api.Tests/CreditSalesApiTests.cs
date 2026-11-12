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
using ControlPlus.Application.Credits.Contracts;
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
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class CreditSalesApiTests : IAsyncLifetime
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreditSale_WithBusinessVat_ConfirmsAndStoresWholePesoBreakdown()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var vat = await ReadAsync<VatSettingDto>(await client.PutAsJsonAsync("/api/settings/vat",
            new UpdateVatSettingRequest(true, 19m, 0)), HttpStatusCode.OK);
        Assert.Equal(1, vat.Version);

        await StartSessionAsync(client, scenario, scenario.Cashier);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "credit-vat-whole-peso",
            CreditRequest(scenario, scenario.CustomerId, scenario.Shift.OperatingDate.AddDays(1))),
            HttpStatusCode.Created);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var detail = await db.DetalleVenta.AsNoTracking().SingleAsync(x => x.VentaId == sale.Id);
        var header = await db.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id);
        Assert.Equal(19m, detail.PorcentajeIva);
        Assert.Equal(840m, detail.BaseGravable);
        Assert.Equal(160m, detail.ValorIva);
        Assert.True(header.IvaDiscriminadoCompleto);
        Assert.Equal(840m, header.BaseGravableTotal);
        Assert.Equal(160m, header.ImpuestoIncluidoTotal);
        Assert.Equal(1000m, header.Total);
    }

    private const string AdminName = "credit.sales.admin.test";
    private const string AdminPassword = "Credit-Sales-Admin-Test-2026!";
    private const string SupervisorName = "credit.sales.supervisor.test";
    private const string SupervisorPassword = "Credit-Sales-Supervisor-Test-2026!";
    private const string CashierName = "credit.sales.cashier.test";
    private const string CashierPassword = "Credit-Sales-Cashier-Test-2026!";
    private const string OtherCashierName = "credit.sales.other.cashier.test";
    private const string OtherCashierPassword = "Credit-Sales-Other-Cashier-Test-2026!";

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
            builder.UseSetting("Jwt:Issuer", "controlplus-credit-sales-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-credit-sales-client");
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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreditSale_IsAtomic_AndHasNoInitialPaymentOrCashMovement()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var session = await StartSessionAsync(client, scenario, scenario.Cashier);
        SetBearer(client, scenario.Cashier.AccessToken);
        const string key = "credit-sale-atomic-key";
        var request = CreditRequest(scenario, scenario.CustomerId, scenario.Shift.OperatingDate.AddDays(1));
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, key, request), HttpStatusCode.Created);

        Assert.Equal(1_000m, sale.Total);
        Assert.Equal(0m, sale.Change);
        Assert.Empty(sale.Payments);
        Assert.NotNull(sale.CreditId);
        Assert.StartsWith("VTA-CREDIT1-", sale.VisibleNumber, StringComparison.Ordinal);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persistedSale = await db.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id);
        var credit = await db.Credito.AsNoTracking().SingleAsync(x => x.Id == sale.CreditId);
        var authorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == credit.AutorizacionOperacionId);
        Assert.Equal("CREDITO", persistedSale.TipoVenta);
        Assert.Equal(scenario.Cashier.User.Id, authorization.UsuarioSolicitanteId);
        Assert.Equal(scenario.Supervisor.User.Id, authorization.UsuarioAutorizadorId);
        Assert.Equal("UTILIZADA", authorization.Estado);
        Assert.Equal(sale.Total, credit.MontoOriginal);
        Assert.Equal(sale.Total, credit.MontoAjustado);
        Assert.Equal(sale.Total, credit.SaldoPendiente);
        Assert.Equal("PENDIENTE", credit.Estado);
        Assert.Equal(scenario.Shift.OperatingDate, credit.FechaOtorgamiento);
        Assert.Equal(scenario.Shift.OperatingDate.AddDays(1), credit.FechaVencimiento);
        Assert.Null(credit.FechaPago);
        Assert.Equal(1, credit.Version);
        Assert.Equal(99, await db.Producto.Where(x => x.Id == scenario.ProductId)
            .Select(x => x.StockActual).SingleAsync());
        Assert.Single(await db.MovimientoInventario.Where(x => x.VentaId == sale.Id).ToArrayAsync());
        Assert.Empty(await db.PagoVenta.Where(x => x.VentaId == sale.Id).ToArrayAsync());
        Assert.Empty(await db.PagoCredito.Where(x => x.CreditoId == credit.Id).ToArrayAsync());
        Assert.Empty(await db.MovimientoCaja.Where(x => x.CategoriaMovimiento == "VENTA" ||
            x.CategoriaMovimiento == "ABONO_CREDITO").ToArrayAsync());
        Assert.Single(await db.OperacionIdempotente.Where(x => x.EntidadId == sale.Id).ToArrayAsync());
        Assert.Single(await db.EventoAuditoria.Where(x => x.EntidadId == sale.Id && x.Accion == "SALECONFIRMED").ToArrayAsync());
        Assert.Single(await db.EventoAuditoria.Where(x => x.EntidadId == credit.Id && x.Accion == "CREDITCREATED").ToArrayAsync());
        Assert.Equal("CERRADA", await db.SesionOperador.Where(x => x.Id == session.Id)
            .Select(x => x.Estado).SingleAsync());

        var audit = await ReadAuditTextAsync(db);
        foreach (var sensitive in new[] { key, SupervisorPassword, CashierPassword,
                     scenario.Cashier.AccessToken, "Dirección privada de crédito", "correo-privado@prueba.invalid" })
            Assert.DoesNotContain(sensitive, audit, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreditSale_UsesTheProposedDate_AndChangingTheDefaultDoesNotChangeExistingCredit()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Cashier.AccessToken);
        var rules = await ReadAsync<CreditRulesDto>(
            await client.GetAsync("/api/sales/credit-rules"), HttpStatusCode.OK);
        Assert.Equal(30, rules.DefaultDays);
        var proposedDueDate = scenario.Shift.OperatingDate.AddDays(rules.DefaultDays!.Value);

        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client,
            "credit-proposed-default-date",
            CreditRequest(scenario, scenario.CustomerId, proposedDueDate)), HttpStatusCode.Created);

        SetBearer(client, scenario.Admin.AccessToken);
        var settings = await ReadAsync<CreditSettingsDto>(
            await client.GetAsync("/api/credits/settings"), HttpStatusCode.OK);
        _ = await ReadAsync<CreditSettingsDto>(await client.PutAsJsonAsync(
            "/api/credits/settings", new UpdateCreditSettingsRequest(45, settings.Version)),
            HttpStatusCode.OK);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(proposedDueDate, await db.Credito.AsNoTracking()
            .Where(x => x.Id == sale.CreditId)
            .Select(x => x.FechaVencimiento).SingleAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CashWithoutSaleType_RemainsDefault_AndExplicitCashReplaysSameIdempotencyKey()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        SetBearer(client, scenario.Cashier.AccessToken);
        var implicitRequest = CashRequest(scenario.ProductId);
        const string key = "credit-regression-implicit-cash";
        var first = await ReadAsync<SaleDto>(await PostSaleAsync(client, key, implicitRequest), HttpStatusCode.Created);
        var replay = await ReadAsync<SaleDto>(await PostSaleAsync(client, key,
            implicitRequest with { SaleType = "CONTADO" }), HttpStatusCode.Created);
        Assert.Equal(first.Id, replay.Id);
        Assert.Null(first.CreditId);
        Assert.Single(first.Payments);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal("CONTADO", await db.Venta.Where(x => x.Id == first.Id).Select(x => x.TipoVenta).SingleAsync());
        Assert.Equal(0, await db.Credito.CountAsync());
        Assert.Equal(1, await db.Venta.CountAsync());
        Assert.Equal(1, await db.PagoVenta.CountAsync());
        Assert.Equal(1, await db.MovimientoCaja.CountAsync(x => x.CategoriaMovimiento == "VENTA"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreditSale_RejectsCustomerAndDueDateMatrix_WithoutWrites()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var inactive = await CreateCustomerAsync(scenario.Admin.User.Id, false, "inactive", true, false);
        var unidentified = await CreateCustomerAsync(scenario.Admin.User.Id, true, "unidentified", false, false);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        SetBearer(client, scenario.Cashier.AccessToken);
        var date = scenario.Shift.OperatingDate;
        var before = await CaptureStateAsync(scenario.ProductId);
        var cases = new (string Key, ConfirmSaleRequest Request, HttpStatusCode Status)[]
        {
            ("customer-missing", CreditRequest(scenario, null, date.AddDays(1)), HttpStatusCode.BadRequest),
            ("customer-unknown", CreditRequest(scenario, Guid.NewGuid(), date.AddDays(1)), HttpStatusCode.NotFound),
            ("customer-inactive", CreditRequest(scenario, inactive, date.AddDays(1)), HttpStatusCode.NotFound),
            ("customer-unidentified", CreditRequest(scenario, unidentified, date.AddDays(1)), HttpStatusCode.BadRequest),
            ("due-yesterday", CreditRequest(scenario, scenario.CustomerId, date.AddDays(-1)), HttpStatusCode.BadRequest),
            ("due-today", CreditRequest(scenario, scenario.CustomerId, date), HttpStatusCode.BadRequest),
            ("due-91", CreditRequest(scenario, scenario.CustomerId, date.AddDays(91)), HttpStatusCode.BadRequest)
        };
        foreach (var item in cases)
        {
            await AssertProblemAsync(await PostSaleAsync(client, item.Key, item.Request), item.Status);
            Assert.Equal(before, await CaptureStateAsync(scenario.ProductId));
        }

        var tomorrow = await ReadAsync<SaleDto>(await PostSaleAsync(client, "due-tomorrow",
            CreditRequest(scenario, scenario.CustomerId, date.AddDays(1))), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        SetBearer(client, scenario.Cashier.AccessToken);
        var ninety = await ReadAsync<SaleDto>(await PostSaleAsync(client, "due-90",
            CreditRequest(scenario, scenario.CustomerId, date.AddDays(90))), HttpStatusCode.Created);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(date.AddDays(1), await db.Credito.Where(x => x.Id == tomorrow.CreditId)
            .Select(x => x.FechaVencimiento).SingleAsync());
        Assert.Equal(date.AddDays(90), await db.Credito.Where(x => x.Id == ninety.CreditId)
            .Select(x => x.FechaVencimiento).SingleAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreditSale_RequiresBothActorPermissions_AndHonorsIndividualGrantAndRevoke()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var salesPermission = await GetPermissionIdAsync(client, scenario.Admin.AccessToken, PermissionCodes.SalesCreate);
        var requestPermission = await GetPermissionIdAsync(client, scenario.Admin.AccessToken, PermissionCodes.CreditRequest);
        var request = CreditRequest(scenario, scenario.CustomerId, scenario.Shift.OperatingDate.AddDays(1));
        foreach (var permission in new[] { salesPermission, requestPermission })
        {
            SetBearer(client, scenario.Admin.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                $"/api/users/{scenario.Cashier.User.Id}/permissions/{permission}",
                new SetUserPermissionRequest(false))).StatusCode);
            var cashier = await LoginAsync(client, CashierName, CashierPassword);
            SetBearer(client, cashier.AccessToken);
            await AssertProblemAsync(await PostSaleAsync(client, $"revoked-{permission}", request), HttpStatusCode.Forbidden);
            SetBearer(client, scenario.Admin.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                $"/api/users/{scenario.Cashier.User.Id}/permissions/{permission}",
                new SetUserPermissionRequest(true))).StatusCode);
        }

        var enabledCashier = await LoginAsync(client, CashierName, CashierPassword);
        var enabledScenario = scenario with { Cashier = enabledCashier };
        _ = await StartSessionAsync(client, enabledScenario, enabledCashier);
        SetBearer(client, enabledCashier.AccessToken);
        Assert.Equal(HttpStatusCode.Created,
            (await PostSaleAsync(client, "individual-permissions-restored", request)).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreditSale_RejectsSelfCashierInactiveAndRevokedAuthorizers()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Admin);
        SetBearer(client, scenario.Admin.AccessToken);
        var date = scenario.Shift.OperatingDate.AddDays(1);
        var before = await CaptureStateAsync(scenario.ProductId);

        await AssertProblemAsync(await PostSaleAsync(client, "self-authorization",
            CreditRequest(scenario, scenario.CustomerId, date, AdminName, AdminPassword)), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await PostSaleAsync(client, "cashier-authorization",
            CreditRequest(scenario, scenario.CustomerId, date, OtherCashierName, OtherCashierPassword)), HttpStatusCode.Forbidden);

        var permission = await GetPermissionIdAsync(client, scenario.Admin.AccessToken, PermissionCodes.CreditAuthorize);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Supervisor.User.Id}/permissions/{permission}",
            new SetUserPermissionRequest(false))).StatusCode);
        await AssertProblemAsync(await PostSaleAsync(client, "revoked-authorizer",
            CreditRequest(scenario, scenario.CustomerId, date)), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Supervisor.User.Id}/permissions/{permission}",
            new SetUserPermissionRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(
            $"/api/users/{scenario.Supervisor.User.Id}/deactivate", null)).StatusCode);
        await AssertProblemAsync(await PostSaleAsync(client, "inactive-authorizer",
            CreditRequest(scenario, scenario.CustomerId, date)), HttpStatusCode.Forbidden);
        var after = await CaptureStateAsync(scenario.ProductId);
        Assert.Equal(before.Sales, after.Sales);
        Assert.Equal(before.Credits, after.Credits);
        Assert.Equal(before.Details, after.Details);
        Assert.Equal(before.InventoryMovements, after.InventoryMovements);
        Assert.Equal(before.Authorizations, after.Authorizations);
        Assert.Equal(before.IdempotencyOperations, after.IdempotencyOperations);
        Assert.Equal(before.Stock, after.Stock);
        Assert.Equal(before.SaleSequence, after.SaleSequence);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CreditSale_SupervisorAndAdministratorWithoutSecondAuthorizer_PersistOwnAuthorizationAndReplay()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var dueDate = scenario.Shift.OperatingDate.AddDays(1);
        var request = CreditRequest(scenario, scenario.CustomerId, dueDate) with { Authorization = null };

        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostSaleAsync(client, "cashier-credit-without-authorizer", request)).StatusCode);

        foreach (var (actor, mode) in new[]
                 {
                     (scenario.Supervisor, "SUPERVISOR_SELF"),
                     (scenario.Admin, "ADMINISTRATOR_SELF")
                 })
        {
            _ = await StartSessionAsync(client, scenario, actor);
            SetBearer(client, actor.AccessToken);
            var key = "credit-self-" + mode;
            var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, key, request), HttpStatusCode.Created);
            var beforeReplay = await CaptureStateAsync(scenario.ProductId);
            var replay = await ReadAsync<SaleDto>(await PostSaleAsync(client, key, request), HttpStatusCode.Created);
            Assert.Equal(sale.Id, replay.Id);
            Assert.Equal(beforeReplay, await CaptureStateAsync(scenario.ProductId));

            await using var scope = _factory!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var credit = await db.Credito.AsNoTracking().SingleAsync(x => x.Id == sale.CreditId);
            var authorization = await db.AutorizacionOperacion.AsNoTracking()
                .SingleAsync(x => x.Id == credit.AutorizacionOperacionId);
            Assert.Equal(actor.User.Id, authorization.UsuarioSolicitanteId);
            Assert.Equal(actor.User.Id, authorization.UsuarioAutorizadorId);
            Assert.Equal("PASSWORD", authorization.MetodoAutenticacion);
            Assert.Null(authorization.CredencialAutorizadorId);
            Assert.Contains("sin segundo autorizador", authorization.DescripcionOperacion);
            var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x =>
                x.Accion == "CREDITCREATED" && x.EntidadId == credit.Id);
            Assert.Contains(mode, audit.DatosNuevos);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FailedCreditAuthorization_SharesLoginCounter_LocksOnce_AndKeepsActorUntouched()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        for (var i = 0; i < 2; i++)
        {
            client.DefaultRequestHeaders.Authorization = null;
            await AssertProblemAsync(await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(SupervisorName, "Wrong-Credit-Authorization-2026!")), HttpStatusCode.Unauthorized);
        }
        SetBearer(client, scenario.Cashier.AccessToken);
        var request = CreditRequest(scenario, scenario.CustomerId,
            scenario.Shift.OperatingDate.AddDays(1), SupervisorName, "Wrong-Credit-Authorization-2026!");
        for (var i = 0; i < 3; i++)
            await AssertProblemAsync(await PostSaleAsync(client, $"wrong-credit-password-{i}", request), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await PostSaleAsync(client, "blocked-credit-authorizer",
            CreditRequest(scenario, scenario.CustomerId, scenario.Shift.OperatingDate.AddDays(1))),
            HttpStatusCode.Forbidden);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var authorizer = await db.Usuario.AsNoTracking().SingleAsync(x => x.Id == scenario.Supervisor.User.Id);
        var actor = await db.Usuario.AsNoTracking().SingleAsync(x => x.Id == scenario.Cashier.User.Id);
        Assert.Equal(5, authorizer.IntentosFallidos);
        Assert.NotNull(authorizer.BloqueoHasta);
        Assert.Equal(0, actor.IntentosFallidos);
        Assert.Null(actor.BloqueoHasta);
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.Accion == "USERLOCKED" &&
            x.EntidadId == scenario.Supervisor.User.Id));
        Assert.Equal(3, await db.EventoAuditoria.CountAsync(x => x.Accion == "CREDITAUTHORIZATIONREJECTED"));
        Assert.Equal(0, await db.Venta.CountAsync());
        Assert.Equal(0, await db.Credito.CountAsync());
        Assert.Equal(0, await db.MovimientoInventario.CountAsync());
        Assert.Equal(0, await db.OperacionIdempotente.CountAsync());
        var audit = await ReadAuditTextAsync(db);
        Assert.DoesNotContain("Wrong-Credit-Authorization-2026!", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(scenario.Cashier.AccessToken, audit, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreditSale_IdempotencyReplaysAndRejectsDifferentTypeContentAndActor()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        const string key = "credit-idempotency-replay";
        SetBearer(client, scenario.Cashier.AccessToken);
        var request = CreditRequest(scenario, scenario.CustomerId, scenario.Shift.OperatingDate.AddDays(1));
        var first = await ReadAsync<SaleDto>(await PostSaleAsync(client, key, request), HttpStatusCode.Created);
        var before = await CaptureStateAsync(scenario.ProductId);
        var replay = await ReadAsync<SaleDto>(await PostSaleAsync(client, key, request), HttpStatusCode.Created);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.CreditId, replay.CreditId);
        await AssertProblemAsync(await PostSaleAsync(client, key,
            request with { DueDate = request.DueDate!.Value.AddDays(1) }), HttpStatusCode.Conflict);
        await AssertProblemAsync(await PostSaleAsync(client, key, CashRequest(scenario.ProductId)), HttpStatusCode.Conflict);
        SetBearer(client, scenario.OtherCashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, key, request), HttpStatusCode.Conflict);
        Assert.Equal(before, await CaptureStateAsync(scenario.ProductId));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentSameCreditRequest_CreatesOneSaleCreditAndAuthorization()
    {
        using var setup = CreateClient();
        var scenario = await BootstrapAsync(setup);
        _ = await StartSessionAsync(setup, scenario, scenario.Cashier);
        using var firstClient = CreateClient(scenario.Cashier.AccessToken);
        using var secondClient = CreateClient(scenario.Cashier.AccessToken);
        var request = CreditRequest(scenario, scenario.CustomerId, scenario.Shift.OperatingDate.AddDays(1));

        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(
                         "SELECT id FROM configuracion.instalacion WHERE activo LIMIT 1 FOR UPDATE", blocker, transaction))
            Assert.NotNull(await command.ExecuteScalarAsync());

        var firstTask = PostSaleAsync(firstClient, "concurrent-credit-key", request);
        var secondTask = PostSaleAsync(secondClient, "concurrent-credit-key", request);
        // The common login coordinator serializes this authorizer before the installation lock.
        await WaitForInstallationWaitersAsync(1);
        await transaction.CommitAsync();
        var responses = await Task.WhenAll(firstTask, secondTask);
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        var sales = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<SaleDto>()));
        var sale = Assert.Single(sales.Select(x => x!.Id).Distinct());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.Venta.CountAsync(x => x.Id == sale));
        Assert.Equal(1, await db.Credito.CountAsync(x => x.VentaId == sale));
        Assert.Equal(1, await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "CREAR_CREDITO"));
        Assert.Equal(1, await db.MovimientoInventario.CountAsync(x => x.VentaId == sale));
        Assert.Equal(1, await db.OperacionIdempotente.CountAsync(x => x.EntidadId == sale));
        Assert.Equal(99, await db.Producto.Where(x => x.Id == scenario.ProductId)
            .Select(x => x.StockActual).SingleAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeferredCreditSaleCommitFailure_RollsBackAllEffectsIncludingAuthorizerAndSession()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var session = await StartSessionAsync(client, scenario, scenario.Cashier);
        await InstallDeferredCreditFailureAsync();
        var before = await CaptureStateAsync(scenario.ProductId, session.Id, scenario.Supervisor.User.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var response = await PostSaleAsync(client, "credit-commit-failure",
            CreditRequest(scenario, scenario.CustomerId, scenario.Shift.OperatingDate.AddDays(1)));
        await AssertProblemAsync(response, HttpStatusCode.Conflict);
        Assert.True(await DeferredCreditFailureObservedAsync());
        Assert.Equal(before, await CaptureStateAsync(scenario.ProductId, session.Id, scenario.Supervisor.User.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreditSale_StrictJsonRejectsUnknownRootLineAndAuthorizationFields()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Cashier);
        SetBearer(client, scenario.Cashier.AccessToken);
        var request = CreditRequest(scenario, scenario.CustomerId, scenario.Shift.OperatingDate.AddDays(1));
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var altered in new[]
        {
            json.Replace("\"saleType\":\"CREDITO\"", "\"saleType\":\"CREDITO\",\"internalState\":1", StringComparison.Ordinal),
            json.Replace("\"quantity\":1", "\"quantity\":1,\"internalStock\":1", StringComparison.Ordinal),
            json.Replace("\"password\":\"" + SupervisorPassword + "\"",
                "\"password\":\"" + SupervisorPassword + "\",\"internalUserId\":\"" + Guid.NewGuid() + "\"", StringComparison.Ordinal)
        })
        {
            Assert.NotEqual(json, altered);
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/sales")
            {
                Content = new StringContent(altered, Encoding.UTF8, "application/json")
            };
            message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            await AssertProblemAsync(await client.SendAsync(message), HttpStatusCode.BadRequest);
        }
        Assert.Equal(0, await CountCreditsAsync());
    }

    private HttpClient CreateClient(string? token = null)
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (token is not null) SetBearer(client, token);
        return client;
    }

    private async Task<Scenario> BootstrapAsync(HttpClient client)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
               { Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                   "Comercio ficticio de crédito", "CREDIT-SALES-TEST", "CREDIT-SALES-INSTALLATION",
                   "CREDIT1", "CREDIT-SALES-TERMINAL", "Terminal ficticia de crédito",
                   AdminName, "Administrador ficticio de crédito", AdminPassword)) })
        {
            request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        }
        var admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin.AccessToken);
        var supervisorRole = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRole = await GetRoleIdAsync(client, RoleCodes.Cashier);
        var supervisorUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorName, "Supervisor ficticio de crédito", SupervisorPassword,
                [supervisorRole])), HttpStatusCode.Created);
        var cashierUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierName, "Cajero ficticio de crédito", CashierPassword,
                [cashierRole])), HttpStatusCode.Created);
        _ = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(OtherCashierName, "Otro cajero ficticio", OtherCashierPassword,
                [cashierRole])), HttpStatusCode.Created);

        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Categoría de crédito aislada", null, "CRA")), HttpStatusCode.Created);
        var units = await ReadAsync<IReadOnlyCollection<MeasurementUnitDto>>(
            await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        var product = await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, units.Single(x => x.Code == "UNIDAD").Id,
                null, "CREDIT-SALE-ITEM", null, null, true, "Producto aislado de crédito",
                null, 1_000m, null, null, 0)), HttpStatusCode.Created);
        await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/sales", new CreateSalesDocumentNumberingRequest("VTA", 1)),
            HttpStatusCode.Created);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var row = await db.Producto.SingleAsync(x => x.Id == product.Id);
            row.StockActual = 100;
            row.CostoPromedio = 500m;
            await db.SaveChangesAsync();
        }
        var customerId = await CreateCustomerAsync(admin.User.Id, true, "active", true, true);
        await ReadAsync<CashRegisterDto>(await client.PostAsJsonAsync("/api/cash/register",
            new ConfigureCashRegisterRequest("CREDIT-CASH", "Caja ficticia de crédito")), HttpStatusCode.Created);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        await ReadAsync<CashStateDto>(await client.PutAsJsonAsync("/api/cash/shift-mode",
            new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        var shift = await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Turno de crédito aislado")), HttpStatusCode.Created);
        var supervisor = await LoginAsync(client, SupervisorName, SupervisorPassword);
        var cashier = await LoginAsync(client, CashierName, CashierPassword);
        var otherCashier = await LoginAsync(client, OtherCashierName, OtherCashierPassword);
        return new Scenario(admin, supervisor, cashier, otherCashier, supervisorUser, cashierUser,
            product.Id, customerId, shift);
    }

    private async Task<Guid> CreateCustomerAsync(Guid creatorId, bool active, string suffix,
        bool hasDocument, bool hasPhone)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var customer = new Cliente
        {
            Id = Guid.CreateVersion7(),
            TipoDocumento = hasDocument ? "CC" : null,
            NumeroDocumento = hasDocument ? $"CREDIT-{suffix}-{Guid.NewGuid():N}"[..40] : null,
            NombreCompleto = $"Cliente ficticio {suffix}",
            Telefono = hasPhone ? "3000000000" : null,
            Direccion = "Dirección privada de crédito",
            Correo = "correo-privado@prueba.invalid",
            Observaciones = "Observación privada de crédito",
            Activo = active,
            UsuarioCreacionId = creatorId,
            FechaCreacion = DateTime.UtcNow,
            Version = 1
        };
        db.Cliente.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static ConfirmSaleRequest CreditRequest(Scenario scenario, Guid? customerId, DateOnly dueDate,
        string authorizer = SupervisorName, string password = SupervisorPassword) => new(
        customerId, [new SaleLineRequest(scenario.ProductId, 1, "MINORISTA")], null, null,
        [], "CREDITO", dueDate, new SaleCreditAuthorizationRequest(authorizer, password));

    private static ConfirmSaleRequest CashRequest(Guid productId) => new(
        null, [new SaleLineRequest(productId, 1, "MINORISTA")], null, null,
        [new SalePaymentRequest("EFECTIVO", 1_000m, 1_000m, null)]);

    private async Task<OperatorSessionDto> StartSessionAsync(
        HttpClient client, Scenario scenario, AuthenticationResult actor)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        var credential = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{actor.User.Id}", new IssueOperatorCredentialRequest(null)),
            HttpStatusCode.Created);
        SetBearer(client, actor.AccessToken);
        return await ReadAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(credential.CredentialToken)),
            HttpStatusCode.Created);
    }

    private async Task<CreditSaleState> CaptureStateAsync(Guid productId, Guid? sessionId = null,
        Guid? authorizerId = null)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var session = sessionId is null ? null : await db.SesionOperador.AsNoTracking()
            .SingleAsync(x => x.Id == sessionId);
        var authorizer = authorizerId is null ? null : await db.Usuario.AsNoTracking()
            .SingleAsync(x => x.Id == authorizerId);
        return new CreditSaleState(
            await db.Venta.CountAsync(), await db.Credito.CountAsync(), await db.DetalleVenta.CountAsync(),
            await db.PagoVenta.CountAsync(), await db.MovimientoCaja.CountAsync(),
            await db.MovimientoInventario.CountAsync(), await db.AutorizacionOperacion.CountAsync(),
            await db.OperacionIdempotente.CountAsync(), await db.EventoAuditoria.CountAsync(),
            await db.Producto.Where(x => x.Id == productId).Select(x => x.StockActual).SingleAsync(),
            await db.ConsecutivoDocumento.Where(x => x.TipoDocumento == "VENTA")
                .Select(x => x.UltimoNumero).SingleAsync(),
            session?.Estado, session?.FechaUltimoUso, session?.FechaFin, session?.MotivoCierre,
            authorizer?.IntentosFallidos, authorizer?.BloqueoHasta, authorizer?.UltimoAcceso);
    }

    private async Task<int> CountCreditsAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>()
            .Credito.CountAsync();
    }

    private async Task InstallDeferredCreditFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE ventas.test_credit_commit_observed;
            CREATE FUNCTION ventas.test_fail_credit_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF EXISTS (SELECT 1 FROM ventas.credito WHERE venta_id = NEW.id)
                 AND EXISTS (SELECT 1 FROM ventas.detalle_venta WHERE venta_id = NEW.id)
                 AND EXISTS (SELECT 1 FROM compras.movimiento_inventario WHERE venta_id = NEW.id)
                 AND EXISTS (SELECT 1 FROM auditoria.operacion_idempotente WHERE entidad_id = NEW.id)
                 AND EXISTS (SELECT 1 FROM auditoria.evento_auditoria
                             WHERE entidad_id = NEW.id AND accion = 'SALECONFIRMED')
                 AND EXISTS (SELECT 1 FROM auditoria.evento_auditoria e
                             JOIN ventas.credito c ON c.id = e.entidad_id
                             WHERE c.venta_id = NEW.id AND e.accion = 'CREDITCREATED')
                 AND EXISTS (SELECT 1 FROM caja.sesion_operador
                             WHERE id = NEW.sesion_operador_id AND estado = 'CERRADA')
                 AND NOT EXISTS (SELECT 1 FROM ventas.pago_venta WHERE venta_id = NEW.id)
              THEN
                PERFORM nextval('ventas.test_credit_commit_observed');
              END IF;
              RAISE EXCEPTION 'Forced isolated credit sale commit failure' USING ERRCODE = '23514';
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_credit_commit
              AFTER INSERT ON ventas.venta DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION ventas.test_fail_credit_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredCreditFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT is_called FROM ventas.test_credit_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task WaitForInstallationWaitersAsync(int expectedCount)
    {
        await using var observer = new NpgsqlConnection(_postgres.GetConnectionString());
        await observer.OpenAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                  AND query LIKE '%FROM configuracion.instalacion AS i%'
                  AND query LIKE '%FOR UPDATE%'
                """, observer);
            if ((long)(await command.ExecuteScalarAsync())! >= expectedCount) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"No se observaron {expectedCount} solicitudes esperando instalación.");
    }

    private static async Task<HttpResponseMessage> PostSaleAsync(
        HttpClient client, string key, ConfirmSaleRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/sales")
        {
            Content = JsonContent.Create(request)
        };
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
        var roles = await ReadAsync<PagedResult<RoleDto>>(
            await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        return roles.Items.Single(x => x.Code == code).Id;
    }

    private static async Task<Guid> GetPermissionIdAsync(HttpClient client, string adminToken, string code)
    {
        SetBearer(client, adminToken);
        var permissions = await ReadAsync<PagedResult<PermissionDto>>(
            await client.GetAsync("/api/permissions?page=1&pageSize=100"), HttpStatusCode.OK);
        return permissions.Items.Single(x => x.Code == code).Id;
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
        var audits = await db.EventoAuditoria.AsNoTracking().ToArrayAsync();
        var text = new StringBuilder();
        foreach (var audit in audits)
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
        AuthenticationResult Admin,
        AuthenticationResult Supervisor,
        AuthenticationResult Cashier,
        AuthenticationResult OtherCashier,
        UserDto SupervisorUser,
        UserDto CashierUser,
        Guid ProductId,
        Guid CustomerId,
        CashShiftDto Shift);

    private sealed record CreditSaleState(
        int Sales, int Credits, int Details, int SalePayments, int CashMovements,
        int InventoryMovements, int Authorizations, int IdempotencyOperations, int Audits,
        int Stock, long SaleSequence, string? SessionState, DateTime? SessionLastUsed,
        DateTime? SessionEnded, string? SessionCloseReason, int? AuthorizerFailedAttempts,
        DateTime? AuthorizerLockedUntil, DateTime? AuthorizerLastAccess);
}
