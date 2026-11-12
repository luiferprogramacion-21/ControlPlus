using ControlPlus.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Numbering.Contracts;
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

public sealed partial class LayawayCreateApiFlowTests : IAsyncLifetime
{
    private const string AdminName = "layaway.create.admin";
    private const string AdminPassword = "Layaway-Create-Administrator-2026!";
    private const string SupervisorName = "layaway.create.supervisor";
    private const string SupervisorPassword = "Layaway-Create-Supervisor-2026!";
    private const string CashierName = "layaway.create.cashier";
    private const string CashierPassword = "Layaway-Create-Cashier-2026!";

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
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-layaway-create-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-layaway-create-client");
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

    [Fact, Trait("Category", "Integration")]
    public async Task MixedPayment_RoundsMinimumUp_ReservesOnce_AndReconcilesEachMethodOnce()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var before = await CaptureAsync(scenario, session.Id);
        using var belowMinimum = await PostAsync(client,
            "layaway-below-rounded-minimum", Request(scenario,
                [new CreateLayawayPaymentRequest("EFECTIVO", 200m, 200m, null)]));
        Assert.Equal(HttpStatusCode.BadRequest, belowMinimum.StatusCode);
        Assert.Contains("201 pesos", await belowMinimum.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client,
            "layaway-above-total", Request(scenario,
                [new CreateLayawayPaymentRequest("EFECTIVO", 1002m, 1002m, null)]))).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario, session.Id));
        var request = Request(scenario, [
            new("EFECTIVO", 101m, 200m, null),
            new("NEQUI", 100m, null, "LAY-NEQUI-1")]);

        var created = await ReadAsync<CreatedLayawayDto>(
            await PostAsync(client, "layaway-mixed-first", request), HttpStatusCode.Created);
        SetBearer(client, scenario.Admin.AccessToken);
        var reservedProduct = await ReadAsync<ProductDto>(
            await client.GetAsync($"/api/products/{scenario.ProductId}"), HttpStatusCode.OK);
        using (var blocked = await client.DeleteAsync(
            $"/api/products/{scenario.ProductId}?expectedVersion={reservedProduct.Version}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("solo puede inactivarse", await blocked.Content.ReadAsStringAsync());
        }
        SetBearer(client, scenario.Cashier.AccessToken);

        Assert.Equal("APT-LAYAWAY1-1", created.VisibleNumber);
        Assert.Equal(scenario.Shift.OperatingDate.AddDays(15), created.DueDate);
        Assert.Equal(20m, created.MinimumPercentage);
        Assert.Equal(201m, created.MinimumInitialPayment); // ceil(1001 × 20 %)
        Assert.Equal(201m, created.InitialPayment);
        Assert.Equal(1001m, created.Total);
        Assert.Equal(800m, created.Balance);
        Assert.Equal(99m, created.Change);
        Assert.Equal("ACTIVO", created.State);
        Assert.Equal(session.Id, created.OperatorSessionId);
        Assert.Equal(2, created.Payments.Count);
        Assert.Equal(new[] { "PAP-LAYAWAY1-1", "PAP-LAYAWAY1-2" },
            created.Payments.Select(x => x.VisibleNumber).ToArray());

        var list = await ReadAsync<PagedResult<LayawayQueryDto>>(await client.GetAsync(
            $"/api/layaways?page=1&pageSize=1&state=ACTIVO&customerId={scenario.CustomerId}" +
            $"&dueFrom={created.DueDate:yyyy-MM-dd}&dueTo={created.DueDate:yyyy-MM-dd}"),
            HttpStatusCode.OK);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(created.Id, Assert.Single(list.Items).Id);
        var detailResponse = await client.GetAsync($"/api/layaways/{created.Id}");
        var detailBody = await detailResponse.Content.ReadAsStringAsync();
        var detail = await ReadAsync<LayawayDetailDto>(detailResponse, HttpStatusCode.OK);
        Assert.Equal(created.Id, detail.Layaway.Id);
        Assert.Equal(new[] { "PAP-LAYAWAY1-1", "PAP-LAYAWAY1-2" },
            detail.Payments.Select(x => x.Number).ToArray());
        Assert.Single(detail.Lines);
        Assert.DoesNotContain("cost", detailBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/layaways/{Guid.CreateVersion7()}")).StatusCode);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var product = await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.ProductId);
            Assert.Equal(5, product.StockActual);
            Assert.Equal(1, product.StockReservado);
            var movement = await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.ApartadoId == created.Id);
            Assert.Equal("RESERVA_APARTADO", movement.TipoMovimiento);
            Assert.Equal(0, movement.CantidadStock);
            Assert.Equal(1, movement.CantidadReservada);
            Assert.Equal(5, movement.StockAnterior);
            Assert.Equal(5, movement.StockPosterior);
            var payments = await db.PagoApartado.AsNoTracking().Where(x => x.ApartadoId == created.Id).ToArrayAsync();
            Assert.Equal(2, payments.Length);
            Assert.Single(payments.Select(x => x.MovimientoCajaId).Distinct());
            var cash = await db.MovimientoCaja.AsNoTracking().SingleAsync(x => x.Id == payments[0].MovimientoCajaId);
            Assert.Equal(201m, cash.Valor);
            var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == created.Id);
            var authorization = await db.AutorizacionOperacion.AsNoTracking()
                .SingleAsync(x => x.Id == layaway.AutorizacionOperacionId);
            Assert.Equal(scenario.Cashier.User.Id, authorization.UsuarioSolicitanteId);
            Assert.Equal(scenario.Supervisor.User.Id, authorization.UsuarioAutorizadorId);
            Assert.Equal("UTILIZADA", authorization.Estado);
            Assert.Equal(1, await db.OperacionIdempotente.CountAsync(x => x.EntidadId == created.Id));
            Assert.Equal(1, await db.EventoAuditoria.CountAsync(x =>
                x.EntidadId == created.Id && x.Accion == "LAYAWAYCREATED"));
            Assert.Equal("CERRADA", await db.SesionOperador.Where(x => x.Id == session.Id)
                .Select(x => x.Estado).SingleAsync());
        }

        SetBearer(client, scenario.Admin.AccessToken);
        var reconciliation = await ReadAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(201m, reconciliation.TotalExpected);
        Assert.Equal(101m, reconciliation.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        Assert.Equal(100m, reconciliation.PaymentMethods.Single(x => x.Code == "NEQUI").ExpectedAmount);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConfiguredAndNullDeadlines_AreEnforced_AndFullPaymentRemainsReserved()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var request = Request(scenario, [new("EFECTIVO", 1001m, 1001m, null)]);
        var before = await CaptureAsync(scenario);
        foreach (var dueDate in new[]
                 { scenario.Shift.OperatingDate, scenario.Shift.OperatingDate.AddDays(16) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client,
                $"layaway-configured-deadline-{dueDate}", request with { DueDate = dueDate })).StatusCode);
            Assert.Equal(before, await CaptureAsync(scenario));
        }

        var paid = await ReadAsync<CreatedLayawayDto>(
            await PostAsync(client, "layaway-paid-in-full", request), HttpStatusCode.Created);
        Assert.Equal("PAGADO", paid.State);
        Assert.Equal(0m, paid.Balance);
        Assert.Equal(scenario.Shift.OperatingDate.AddDays(15), paid.DueDate);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.NotNull(await db.Apartado.Where(x => x.Id == paid.Id)
                .Select(x => x.FechaPago).SingleAsync());
            Assert.Equal(5, await db.Producto.Where(x => x.Id == scenario.ProductId)
                .Select(x => x.StockActual).SingleAsync());
            Assert.Equal(1, await db.Producto.Where(x => x.Id == scenario.ProductId)
                .Select(x => x.StockReservado).SingleAsync());
        }

        await SetDeadlineDaysAsync(null);
        _ = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var withoutDeadline = await CaptureAsync(scenario);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostAsync(client, "layaway-null-deadline-rejected", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client,
            "layaway-null-deadline-today", request with
            { DueDate = scenario.Shift.OperatingDate })).StatusCode);
        Assert.Equal(withoutDeadline, await CaptureAsync(scenario));
        var selectedDate = scenario.Shift.OperatingDate.AddDays(25);
        var selected = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-selected-deadline", request with { DueDate = selectedDate }), HttpStatusCode.Created);
        Assert.Equal(selectedDate, selected.DueDate);
        Assert.Equal("PAGADO", selected.State);
        await using var finalScope = _factory!.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Null(await finalDb.Establecimiento.Select(x => x.DiasLimiteApartado).SingleAsync());
        Assert.Equal(5, await finalDb.Producto.Where(x => x.Id == scenario.ProductId)
            .Select(x => x.StockActual).SingleAsync());
        Assert.Equal(2, await finalDb.Producto.Where(x => x.Id == scenario.ProductId)
            .Select(x => x.StockReservado).SingleAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CurrentPermissions_DistinctAuthorizerAndActiveSession_AreRequired()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var request = Request(scenario, [new("EFECTIVO", 201m, 201m, null)]);
        SetBearer(client, scenario.Admin.AccessToken);
        var layawayPermissionId = await GetPermissionIdAsync(client, PermissionCodes.LayawayManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Cashier.User.Id}/permissions/{layawayPermissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        var cashier = await LoginAsync(client, CashierName, CashierPassword);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostAsync(client, "layaway-actor-revoked", request)).StatusCode);

        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Cashier.User.Id}/permissions/{layawayPermissionId}",
            new SetUserPermissionRequest(true))).StatusCode);
        cashier = await LoginAsync(client, CashierName, CashierPassword);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict,
            (await PostAsync(client, "layaway-no-session", request)).StatusCode);
        _ = await StartSessionAsync(client, scenario, cashier);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, "layaway-self-authorized",
            request with { Authorization = new(CashierName, CashierPassword) })).StatusCode);

        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Supervisor.User.Id}/permissions/{layawayPermissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostAsync(client, "layaway-authorizer-revoked", request)).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Supervisor.User.Id}/permissions/{layawayPermissionId}",
            new SetUserPermissionRequest(true))).StatusCode);

        SetBearer(client, cashier.AccessToken);
        const string wrongPassword = "Wrong-Layaway-Authorization-2026!";
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, "layaway-wrong-credential",
            request with { Authorization = new(SupervisorName, wrongPassword) })).StatusCode);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.Equal(1, await db.Usuario.Where(x => x.Id == scenario.Supervisor.User.Id)
                .Select(x => x.IntentosFallidos).SingleAsync());
            Assert.Equal(1, await db.EventoAuditoria.CountAsync(x =>
                x.Accion == "LAYAWAYAUTHORIZATIONREJECTED"));
            Assert.Equal(0, await db.Apartado.CountAsync());
            var auditJson = await db.EventoAuditoria.AsNoTracking()
                .Select(x => x.DatosNuevos)
                .ToArrayAsync();
            var auditText = string.Join('|', auditJson.Select(x => x ?? string.Empty).ToArray());
            Assert.DoesNotContain(wrongPassword, auditText, StringComparison.Ordinal);
        }

        var bySupervisor = await ReadAsync<CreatedLayawayDto>(
            await PostAsync(client, "layaway-supervisor-authorized", request), HttpStatusCode.Created);
        _ = await StartSessionAsync(client, scenario, cashier);
        SetBearer(client, cashier.AccessToken);
        var byAdministrator = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-admin-authorized", request with
            { Authorization = new(AdminName, AdminPassword) }), HttpStatusCode.Created);
        await using var finalScope = _factory!.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var authorizerIds = await finalDb.Apartado.AsNoTracking()
            .Where(x => x.Id == bySupervisor.Id || x.Id == byAdministrator.Id)
            .Select(x => x.AutorizacionOperacion.UsuarioAutorizadorId).ToArrayAsync();
        Assert.Contains(scenario.Supervisor.User.Id, authorizerIds);
        Assert.Contains(scenario.Admin.User.Id, authorizerIds);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task AdministratorWithoutSecondAuthorizer_IsRecordedAsSelfAuthorized_AndReplaysWithoutWrites()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        var request = Request(scenario, [new("EFECTIVO", 201m, 201m, null)]) with { Authorization = null };

        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostAsync(client, "cashier-no-authorizer", request)).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, "admin-with-credentials",
            request with { Authorization = new(SupervisorName, SupervisorPassword) })).StatusCode);
        var created = await ReadAsync<CreatedLayawayDto>(
            await PostAsync(client, "admin-self-authorized", request), HttpStatusCode.Created);
        var beforeReplay = await CaptureAsync(scenario);
        var replay = await ReadAsync<CreatedLayawayDto>(
            await PostAsync(client, "admin-self-authorized", request), HttpStatusCode.Created);
        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(created.VisibleNumber, replay.VisibleNumber);
        Assert.Equal(beforeReplay, await CaptureAsync(scenario));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        var authorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == layaway.AutorizacionOperacionId);
        Assert.Equal(scenario.Admin.User.Id, authorization.UsuarioSolicitanteId);
        Assert.Equal(scenario.Admin.User.Id, authorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", authorization.MetodoAutenticacion);
        Assert.Null(authorization.CredencialAutorizadorId);
        Assert.Equal("UTILIZADA", authorization.Estado);
        Assert.Equal("EXITOSA", authorization.Resultado);
        Assert.NotNull(authorization.FechaUtilizacion);
        Assert.Equal("Autorización de creación de apartado por Administrador sin segundo autorizador.",
            authorization.DescripcionOperacion);
        var audit = Assert.Single(await db.EventoAuditoria.AsNoTracking()
            .Where(x => x.EntidadId == created.Id && x.Accion == "LAYAWAYCREATED").ToArrayAsync());
        Assert.Contains("ADMINISTRATOR_SELF", audit.DatosNuevos);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task AdministratorAuthorizationRow_PassesOfficialDatabaseChecks()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, sharedShift: false);
        SetBearer(client, scenario.Admin.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "admin-authorization-checks", Request(scenario, [new("EFECTIVO", 201m, 201m, null)])
                with { Authorization = null }), HttpStatusCode.Created);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var authorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.ApartadoAutorizacionOperacion != null &&
                x.ApartadoAutorizacionOperacion.Id == created.Id);
        Assert.Equal("PASSWORD", authorization.MetodoAutenticacion);
        Assert.Null(authorization.CredencialAutorizadorId);
        Assert.Equal(authorization.UsuarioSolicitanteId, authorization.UsuarioAutorizadorId);
        Assert.Equal("UTILIZADA", authorization.Estado);
        Assert.Equal("EXITOSA", authorization.Resultado);
        Assert.NotNull(authorization.FechaUtilizacion);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SupervisorWithoutSecondAuthorizer_CreatesAndReplaysWithOwnAuditMark()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        _ = await StartSessionAsync(client, scenario, scenario.Supervisor);
        var request = Request(scenario, [new("EFECTIVO", 201m, 201m, null)]) with { Authorization = null };
        SetBearer(client, scenario.Supervisor.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "supervisor-self-layaway", request), HttpStatusCode.Created);
        var beforeReplay = await CaptureAsync(scenario);
        var replay = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "supervisor-self-layaway", request), HttpStatusCode.Created);
        Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(replay));
        Assert.Equal(beforeReplay, await CaptureAsync(scenario));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var layaway = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        var authorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == layaway.AutorizacionOperacionId);
        Assert.Equal(scenario.Supervisor.User.Id, authorization.UsuarioSolicitanteId);
        Assert.Equal(scenario.Supervisor.User.Id, authorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", authorization.MetodoAutenticacion);
        Assert.Null(authorization.CredencialAutorizadorId);
        Assert.Contains("Supervisor sin segundo autorizador", authorization.DescripcionOperacion);
        var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x =>
            x.Accion == "LAYAWAYCREATED" && x.EntidadId == created.Id);
        Assert.Contains("SUPERVISOR_SELF", audit.DatosNuevos);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task InactiveCustomerOrMissingContact_CannotCreateOrConsumeSession()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var request = Request(scenario, [new("EFECTIVO", 201m, 201m, null)]);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var customer = await db.Cliente.SingleAsync(x => x.Id == scenario.CustomerId);
            customer.Activo = false;
            await db.SaveChangesAsync();
        }
        var before = await CaptureAsync(scenario, session.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostAsync(client, "layaway-inactive-customer", request)).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario, session.Id));

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var customer = await db.Cliente.SingleAsync(x => x.Id == scenario.CustomerId);
            customer.Activo = true;
            customer.TipoDocumento = null;
            customer.NumeroDocumento = null;
            customer.Telefono = null;
            await db.SaveChangesAsync();
        }
        before = await CaptureAsync(scenario, session.Id);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostAsync(client, "layaway-customer-without-contact", request)).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario, session.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task MissingNumbering_RequiresExplicitInitializationBeforeCreation()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, initializeNumbering: false);
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var request = Request(scenario, [new("EFECTIVO", 201m, 201m, null)]);

        Assert.Equal(HttpStatusCode.Conflict,
            (await PostAsync(client, "layaway-numbering-required", request)).StatusCode);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.Equal(0, await db.ConsecutivoDocumento.CountAsync());
            Assert.Equal(0, await db.Apartado.CountAsync());
            Assert.Equal(0, await db.OperacionIdempotente.CountAsync());
            Assert.Equal(0, await db.Producto.Where(x => x.Id == scenario.ProductId)
                .Select(x => x.StockReservado).SingleAsync());
            Assert.Equal("ACTIVA", await db.SesionOperador.Where(x => x.Id == session.Id)
                .Select(x => x.Estado).SingleAsync());
        }

        SetBearer(client, scenario.Admin.AccessToken);
        _ = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/layaways",
            new CreateLayawayDocumentNumberingRequest("APT", 1)), HttpStatusCode.Created);
        _ = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/layaway-payments",
            new CreateLayawayDocumentNumberingRequest("PAP", 1)), HttpStatusCode.Created);
        SetBearer(client, scenario.Cashier.AccessToken);
        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-numbering-required", request), HttpStatusCode.Created);
        Assert.Equal("APT-LAYAWAY1-1", created.VisibleNumber);
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("100000000000000", 1, false)] // A single tax base cannot fit numeric(18,4).
    [InlineData("50000000000000", 2, false)] // Valid unit price; quantity makes the base overflow.
    [InlineData("60000000000000", 1, true)] // Each line fits; the header tax base overflows.
    public async Task TaxAmountOverflow_ReturnsValidationWithoutPartialEffects(
        string priceText, int quantity, bool addSecondProduct)
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var price = decimal.Parse(priceText, System.Globalization.CultureInfo.InvariantCulture);
        var lines = new List<CreateLayawayLineRequest>
            { new(scenario.ProductId, quantity, "MINORISTA") };
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var product = await db.Producto.SingleAsync(x => x.Id == scenario.ProductId);
            product.PrecioMinorista = price;
            product.PorcentajeIva = 0m;
            if (addSecondProduct)
            {
                var second = new Producto
                {
                    Id = Guid.CreateVersion7(), CategoriaId = product.CategoriaId,
                    UnidadMedidaId = product.UnidadMedidaId, CodigoInterno = "LAYAWAY-TAX-SECOND",
                    Nombre = "Segundo producto de regresión de IVA", PrecioMinorista = price,
                    PorcentajeIva = 0m, CostoPromedio = 500m, StockActual = 5, Activo = true,
                    UsuarioCreacionId = scenario.Admin.User.Id, FechaCreacion = DateTime.UtcNow, Version = 1
                };
                db.Producto.Add(second);
                lines.Add(new(second.Id, 1, "MINORISTA"));
            }
            await db.SaveChangesAsync();
        }

        var before = await CaptureAsync(scenario, session.Id);
        await using var verification = _factory!.Services.CreateAsyncScope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var stocksBefore = await verificationDb.Producto.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.StockActual, x.StockReservado, x.Version }).ToArrayAsync();
        var total = price * (quantity + (addSecondProduct ? 1 : 0));
        var payment = total / 5m;
        using var response = await PostAsync(client, "layaway-tax-overflow", Request(scenario,
            [new("EFECTIVO", payment, payment, null)]) with { Lines = lines });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation.failed", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, await CaptureAsync(scenario, session.Id));
        Assert.Equal(stocksBefore, await verificationDb.Producto.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.StockActual, x.StockReservado, x.Version }).ToArrayAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task InvalidPaymentAmounts_ReturnValidationWithoutPartialEffects()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var before = await CaptureAsync(scenario, session.Id);
        CreateLayawayPaymentRequest[][] invalidPayments =
        [
            [new("EFECTIVO", 201m, 1000000000000000000m, null)],
            [new("EFECTIVO", 201m, 201.5m, null)],
            [new("EFECTIVO", 1000000000000000000m, 1000000000000000000m, null)],
            [new("EFECTIVO", 201.5m, 202m, null)],
            // Both applied amounts fit numeric(18,0), but their sum does not.
            [new("EFECTIVO", 500000000000000000m, 500000000000000000m, null),
             new("NEQUI", 500000000000000000m, null, "LAYAWAY-SUM-OVERFLOW")]
        ];
        for (var index = 0; index < invalidPayments.Length; index++)
        {
            using var response = await PostAsync(client, $"layaway-invalid-payment-{index}",
                Request(scenario, invalidPayments[index]));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("validation.failed", problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(before, await CaptureAsync(scenario, session.Id));
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task DisabledMinimum_AcceptsOnePeso_RejectsZeroAndAboveTotal_AndSnapshotsAppliedRule()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await SetMinimumRequirementAsync(false);
        var session = await StartSessionAsync(client, scenario);
        SetBearer(client, scenario.Cashier.AccessToken);
        var before = await CaptureAsync(scenario, session.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client,
            "layaway-disabled-zero", Request(scenario,
                [new CreateLayawayPaymentRequest("EFECTIVO", 0m, 0m, null)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client,
            "layaway-disabled-above-total", Request(scenario,
                [new CreateLayawayPaymentRequest("EFECTIVO", 1002m, 1002m, null)]))).StatusCode);
        Assert.Equal(before, await CaptureAsync(scenario, session.Id));

        var created = await ReadAsync<CreatedLayawayDto>(await PostAsync(client,
            "layaway-disabled-one-peso", Request(scenario,
                [new CreateLayawayPaymentRequest("EFECTIVO", 1m, 1m, null)])),
            HttpStatusCode.Created);

        Assert.Equal(0m, created.MinimumPercentage);
        Assert.Equal(1m, created.MinimumInitialPayment);
        Assert.Equal(1m, created.InitialPayment);
        Assert.Equal(1000m, created.Balance);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persisted = await db.Apartado.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(0m, persisted.PorcentajeMinimoInicial);
        Assert.Equal(1m, persisted.AbonoInicial);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConcurrentReservations_AllowOnlyAvailableStock_AndReplayHasNoSecondEffects()
    {
        using var setupClient = CreateClient();
        var scenario = await BootstrapAsync(setupClient, sharedShift: false, stock: 1);
        using var firstClient = CreateClient(scenario.Admin.AccessToken);
        using var secondClient = CreateClient(scenario.Admin.AccessToken);
        var request = Request(scenario, [new("EFECTIVO", 201m, 201m, null)]) with { Authorization = null };

        var responses = await Task.WhenAll(
            PostAsync(firstClient, "layaway-stock-race-first", request),
            PostAsync(secondClient, "layaway-stock-race-second", request));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var winner = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
        var created = await ReadAsync<CreatedLayawayDto>(winner, HttpStatusCode.Created);
        var winningKey = ReferenceEquals(winner, responses[0])
            ? "layaway-stock-race-first" : "layaway-stock-race-second";
        var beforeReplay = await CaptureAsync(scenario);
        var replay = await ReadAsync<CreatedLayawayDto>(await PostAsync(firstClient,
            winningKey, request), HttpStatusCode.Created);
        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(created.VisibleNumber, replay.VisibleNumber);
        Assert.Equal(beforeReplay, await CaptureAsync(scenario));
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(firstClient,
            winningKey, request with { DueDate = scenario.Shift.OperatingDate.AddDays(1) })).StatusCode);
        Assert.Equal(beforeReplay, await CaptureAsync(scenario));
        Assert.Equal(1, beforeReplay.ReservedStock);
        Assert.Equal(1, beforeReplay.ActualStock);
        Assert.Equal(1, beforeReplay.Layaways);
        Assert.Equal(1, beforeReplay.LayawaySequence);
        Assert.Equal(1, beforeReplay.PaymentSequence);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task DeferredCommitFailure_RollsBackSequenceStockPaymentAuthorizationAuditAndSession()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var session = await StartSessionAsync(client, scenario);
        await InstallDeferredFailureAsync();
        var before = await CaptureAsync(scenario, session.Id);
        SetBearer(client, scenario.Cashier.AccessToken);

        var response = await PostAsync(client, "layaway-forced-commit-failure",
            Request(scenario, [new("EFECTIVO", 201m, 201m, null)]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await DeferredFailureObservedAsync());
        Assert.Equal(before, await CaptureAsync(scenario, session.Id));
    }

    private HttpClient CreateClient(string? token = null)
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        if (token is not null) SetBearer(client, token);
        return client;
    }

    private async Task<Scenario> BootstrapAsync(HttpClient client, bool sharedShift = true,
        int stock = 5, bool initializeNumbering = true)
    {
        using (var message = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
               { Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                   "Comercio ficticio de apartados", "LAYAWAY-CREATE-TEST", "LAYAWAY-CREATE-INSTALLATION",
                   "LAYAWAY1", "LAYAWAY-CREATE-TERMINAL", "Terminal ficticia de apartados",
                   AdminName, "Administrador ficticio de apartados", AdminPassword)) })
        {
            message.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(message)).StatusCode);
        }
        var admin = await LoginAsync(client, AdminName, AdminPassword);
        SetBearer(client, admin.AccessToken);
        var supervisorRoleId = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRoleId = await GetRoleIdAsync(client, RoleCodes.Cashier);
        var supervisorUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorName, "Supervisor ficticio de apartados",
                SupervisorPassword, [supervisorRoleId])), HttpStatusCode.Created);
        var cashierUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierName, "Cajero ficticio de apartados",
                CashierPassword, [cashierRoleId])), HttpStatusCode.Created);
        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Categoría de apartados aislada", null, "APA")), HttpStatusCode.Created);
        var units = await ReadAsync<IReadOnlyCollection<MeasurementUnitDto>>(
            await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        var unitId = units.Single(x => x.Code == "UNIDAD").Id;
        var product = await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, unitId, null, "LAYAWAY-CREATE-ITEM", null,
                null, true, "Producto ficticio de apartado", null, 1001m,
                null, null, 0)), HttpStatusCode.Created);
        if (initializeNumbering)
        {
            _ = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
                "/api/document-numbering/layaways",
                new CreateLayawayDocumentNumberingRequest("APT", 1)), HttpStatusCode.Created);
            _ = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
                "/api/document-numbering/layaway-payments",
                new CreateLayawayDocumentNumberingRequest("PAP", 1)), HttpStatusCode.Created);
        }
        var customerId = await SeedProductCustomerAndSettingsAsync(admin.User.Id, product.Id, stock);
        _ = await ReadAsync<CashRegisterDto>(await client.PostAsJsonAsync("/api/cash/register",
            new ConfigureCashRegisterRequest("LAYAWAY-CASH", "Caja ficticia de apartados")), HttpStatusCode.Created);
        if (sharedShift)
        {
            var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
            _ = await ReadAsync<CashStateDto>(await client.PutAsJsonAsync("/api/cash/shift-mode",
                new UpdateCashShiftModeRequest(CashConstants.SharedMode,
                    state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        }
        var shift = await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Turno de apartados aislado")), HttpStatusCode.Created);
        var supervisor = await LoginAsync(client, SupervisorName, SupervisorPassword);
        var cashier = await LoginAsync(client, CashierName, CashierPassword);
        return new Scenario(admin, supervisor, cashier, supervisorUser, cashierUser,
            product.Id, product.InternalCode, customerId, shift);
    }

    private async Task<Guid> SeedProductCustomerAndSettingsAsync(
        Guid adminId, Guid productId, int stock)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await db.Producto.SingleAsync(x => x.Id == productId);
        product.StockActual = stock;
        product.CostoPromedio = 500m;
        var establishment = await db.Establecimiento.SingleAsync();
        establishment.DiasLimiteApartado = 15;
        establishment.PorcentajeMinimoApartado = 20m;
        establishment.RequireMinimumInitial = true;
        var customer = new Cliente
        {
            Id = Guid.CreateVersion7(), TipoDocumento = "CC",
            NumeroDocumento = $"LAY-{Guid.NewGuid():N}"[..30],
            NombreCompleto = "Cliente ficticio de apartados",
            Direccion = "Dirección ficticia de apartados",
            Activo = true, UsuarioCreacionId = adminId,
            FechaCreacion = DateTime.UtcNow, Version = 1
        };
        db.Cliente.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task SetDeadlineDaysAsync(int? days)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var establishment = await db.Establecimiento.SingleAsync();
        establishment.DiasLimiteApartado = days;
        await db.SaveChangesAsync();
    }

    private async Task SetMinimumRequirementAsync(bool required)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var establishment = await db.Establecimiento.SingleAsync();
        establishment.RequireMinimumInitial = required;
        await db.SaveChangesAsync();
    }

    private async Task<OperatorSessionDto> StartSessionAsync(
        HttpClient client, Scenario scenario, AuthenticationResult? cashier = null)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        var credential = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{(cashier ?? scenario.Cashier).User.Id}",
            new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        SetBearer(client, (cashier ?? scenario.Cashier).AccessToken);
        return await ReadAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions",
            new StartOperatorSessionRequest(credential.CredentialToken)), HttpStatusCode.Created);
    }

    private static CreateLayawayRequest Request(
        Scenario scenario, IReadOnlyCollection<CreateLayawayPaymentRequest> payments) => new(
        scenario.CustomerId, [new(scenario.ProductId, 1, "MINORISTA")], payments,
        null, new(SupervisorName, SupervisorPassword));

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string key, CreateLayawayRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/layaways")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private async Task<LayawayState> CaptureAsync(Scenario scenario, Guid? sessionId = null)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await db.Producto.AsNoTracking().SingleAsync(x => x.Id == scenario.ProductId);
        var authorizer = await db.Usuario.AsNoTracking()
            .SingleAsync(x => x.Id == scenario.Supervisor.User.Id);
        var session = sessionId is Guid id
            ? await db.SesionOperador.AsNoTracking().SingleAsync(x => x.Id == id)
            : null;
        return new LayawayState(
            await db.Apartado.CountAsync(),
            await db.DetalleApartado.CountAsync(),
            await db.PagoApartado.CountAsync(),
            await db.MovimientoCaja.CountAsync(),
            await db.MovimientoInventario.CountAsync(),
            await db.AutorizacionOperacion.CountAsync(),
            await db.OperacionIdempotente.CountAsync(),
            await db.EventoAuditoria.CountAsync(),
            product.StockActual, product.StockReservado,
            await db.ConsecutivoDocumento.Where(x => x.TipoDocumento == "APARTADO")
                .Select(x => x.UltimoNumero).SingleAsync(),
            await db.ConsecutivoDocumento.Where(x => x.TipoDocumento == "PAGO_APARTADO")
                .Select(x => x.UltimoNumero).SingleAsync(),
            session?.Estado, session?.FechaUltimoUso, session?.FechaFin,
            authorizer.IntentosFallidos, authorizer.BloqueoHasta, authorizer.UltimoAcceso);
    }

    private async Task InstallDeferredFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE ventas.test_layaway_commit_observed;
            CREATE FUNCTION ventas.test_fail_layaway_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF EXISTS (SELECT 1 FROM ventas.detalle_apartado WHERE apartado_id = NEW.id)
                 AND EXISTS (SELECT 1 FROM ventas.pago_apartado WHERE apartado_id = NEW.id)
                 AND EXISTS (SELECT 1 FROM compras.movimiento_inventario WHERE apartado_id = NEW.id)
                 AND EXISTS (SELECT 1 FROM caja.movimiento_caja WHERE id IN
                     (SELECT movimiento_caja_id FROM ventas.pago_apartado WHERE apartado_id = NEW.id))
                 AND EXISTS (SELECT 1 FROM seguridad.autorizacion_operacion WHERE id = NEW.autorizacion_operacion_id)
                 AND EXISTS (SELECT 1 FROM auditoria.operacion_idempotente WHERE entidad_id = NEW.id)
                 AND EXISTS (SELECT 1 FROM auditoria.evento_auditoria
                     WHERE entidad_id = NEW.id AND accion = 'LAYAWAYCREATED')
                 AND EXISTS (SELECT 1 FROM caja.sesion_operador
                     WHERE id = NEW.sesion_operador_id AND estado = 'CERRADA')
              THEN
                PERFORM nextval('ventas.test_layaway_commit_observed');
              END IF;
              RAISE EXCEPTION 'Forced isolated layaway commit failure' USING ERRCODE = '23514';
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_layaway_commit
              AFTER INSERT ON ventas.apartado DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION ventas.test_fail_layaway_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT is_called FROM ventas.test_layaway_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<AuthenticationResult> LoginAsync(
        HttpClient client, string userName, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(userName, password)), HttpStatusCode.OK);
    }

    private static async Task<Guid> GetRoleIdAsync(HttpClient client, string code)
    {
        var roles = await ReadAsync<PagedResult<RoleDto>>(
            await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        return roles.Items.Single(x => x.Code == code).Id;
    }

    private static async Task<Guid> GetPermissionIdAsync(HttpClient client, string code)
    {
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

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string RandomSecret(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private sealed record Scenario(
        AuthenticationResult Admin, AuthenticationResult Supervisor, AuthenticationResult Cashier,
        UserDto SupervisorUser, UserDto CashierUser, Guid ProductId, string ProductCode, Guid CustomerId,
        CashShiftDto Shift);

    private sealed record LayawayState(
        int Layaways, int Details, int Payments, int CashMovements,
        int InventoryMovements, int Authorizations, int IdempotencyOperations,
        int Audits, int ActualStock, int ReservedStock, long LayawaySequence,
        long PaymentSequence, string? SessionState, DateTime? SessionLastUse,
        DateTime? SessionEnd, int AuthorizerFailedAttempts,
        DateTime? AuthorizerLockedUntil, DateTime? AuthorizerLastAccess);
}
