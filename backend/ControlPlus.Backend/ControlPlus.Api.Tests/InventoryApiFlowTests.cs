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
using ControlPlus.Application.Inventory.Contracts;
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

public sealed partial class InventoryApiFlowTests : IAsyncLifetime
{
    private const string AdminUser = "inventory.admin.test";
    private const string AdminPassword = "Inventory-Admin-Test-2026!";
    private const string AuthorizerUser = "inventory.authorizer.test";
    private const string AuthorizerPassword = "Inventory-Authorizer-Test-2026!";
    private const string SecondAuthorizerUser = "inventory.authorizer.second.test";
    private const string SecondAuthorizerPassword = "Inventory-Authorizer-Second-Test-2026!";
    private const string SupervisorUser = "inventory.supervisor.test";
    private const string SupervisorPassword = "Inventory-Supervisor-Test-2026!";

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
        {
            await context.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-inventory-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-inventory-client");
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
    public async Task AdministratorAdjustmentWithoutSecondAuthorizer_PersistsOwnAuthorizationAndReplays()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, 10);
        var reason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(client, "/api/inventory/adjustment-reasons", scenario.Admin.AccessToken),
            HttpStatusCode.OK)).Single(x => x.Code == "CONTEO_FISICO");
        var request = new CreateInventoryAdjustmentRequest(
            scenario.ProductId, 1, reason.Id, scenario.ProductVersion, "Conteo propio", null);
        var created = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(client,
            scenario.Admin.AccessToken, "inventory-admin-self", request), HttpStatusCode.Created);
        var repeated = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(client,
            scenario.Admin.AccessToken, "inventory-admin-self", request), HttpStatusCode.Created);
        Assert.Equal(created.OccurredAtUtc.UtcTicks / 10, repeated.OccurredAtUtc.UtcTicks / 10);
        Assert.Equal(created with { OccurredAtUtc = repeated.OccurredAtUtc }, repeated);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var authorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == created.AuthorizationId);
        Assert.Equal(authorization.UsuarioSolicitanteId, authorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", authorization.MetodoAutenticacion);
        Assert.Null(authorization.CredencialAutorizadorId);
        Assert.Contains("sin segundo autorizador", authorization.DescripcionOperacion);
        Assert.Equal(1, await db.MovimientoInventario.CountAsync(x => x.AutorizacionOperacionId == authorization.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Adjustment_UsesEffectivePermissionDoubleControlAndIdempotency()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, 10);

        SetBearer(client, scenario.Supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/inventory/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/inventory/adjustment-reasons")).StatusCode);

        SetBearer(client, scenario.Admin.AccessToken);
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.InventoryAdjust);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.SupervisorUser.Id}/permissions/{permissionId}",
            new SetUserPermissionRequest(true))).StatusCode);
        var grantedSupervisor = await LoginAsync(client, SupervisorUser, SupervisorPassword);

        var reasons = await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(client, "/api/inventory/adjustment-reasons", grantedSupervisor.AccessToken),
            HttpStatusCode.OK);
        Assert.Equal(
            ["CONTEO_FISICO", "MERMA_DANO", "PERDIDA", "CORRECCION_REGISTRO"],
            reasons.Select(x => x.Code));
        var reason = reasons.Single(x => x.Code == "CONTEO_FISICO");

        var selfAuthorization = new CreateInventoryAdjustmentRequest(
            scenario.ProductId, 1, reason.Id, scenario.ProductVersion, "No debe persistir",
            new(SupervisorUser, SupervisorPassword));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAdjustmentAsync(
            client, grantedSupervisor.AccessToken, "inventory-self-authorization", selfAuthorization)).StatusCode);

        var request = selfAuthorization with
        {
            QuantityDelta = 5,
            Observation = "Conteo documentado de prueba",
            Authorization = new(AuthorizerUser, AuthorizerPassword)
        };
        var created = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(
            client, grantedSupervisor.AccessToken, "inventory-idempotent-adjustment", request), HttpStatusCode.Created);
        Assert.Equal("AJUSTE_ENTRADA", created.Type);
        Assert.Equal(10, created.PreviousStock);
        Assert.Equal(15, created.NewStock);
        Assert.Equal(reason.Id, created.ReasonId);
        Assert.NotNull(created.AuthorizationId);
        Assert.Null(created.PreviousAverageCost);

        var replay = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(
            client, grantedSupervisor.AccessToken, "inventory-idempotent-adjustment", request), HttpStatusCode.Created);
        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAdjustmentAsync(
            client, grantedSupervisor.AccessToken, "inventory-idempotent-adjustment",
            request with { QuantityDelta = 6 })).StatusCode);

        var stock = await ReadAsync<PagedResult<InventoryStockDto>>(
            await GetWithBearerAsync(client, "/api/inventory/stock?search=INV-PRODUCT", grantedSupervisor.AccessToken),
            HttpStatusCode.OK);
        var stockItem = Assert.Single(stock.Items);
        Assert.Equal(15, stockItem.CurrentStock);
        Assert.Null(stockItem.AverageCost);
        var kardex = await ReadAsync<PagedResult<InventoryMovementDto>>(
            await GetWithBearerAsync(client,
                $"/api/inventory/movements?productId={scenario.ProductId}", grantedSupervisor.AccessToken),
            HttpStatusCode.OK);
        Assert.Equal(created.Id, Assert.Single(kardex.Items).Id);
        Assert.Null(kardex.Items.Single().PreviousAverageCost);

        var administratorStock = await ReadAsync<PagedResult<InventoryStockDto>>(
            await GetWithBearerAsync(client, "/api/inventory/stock?search=INV-PRODUCT", scenario.Admin.AccessToken),
            HttpStatusCode.OK);
        Assert.Equal(40m, Assert.Single(administratorStock.Items).AverageCost);

        // Filtro de estado: cada estado devuelve exactamente los productos que se calculan aparte, en memoria.
        await using (var statusScope = _factory!.Services.CreateAsyncScope())
        {
            var products = await statusScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>()
                .Producto.AsNoTracking().ToArrayAsync();
            var expectedByStatus = new Dictionary<string, Guid[]>
            {
                // Pruebas del usuario del 26/11/2026: «Stock bajo» ya no incluye los agotados.
                ["STOCK_BAJO"] = Ids(products.Where(p => p.Activo && p.StockActual - p.StockReservado > 0 && p.StockActual - p.StockReservado <= p.StockMinimo).Select(p => p.Id)),
                ["AGOTADO"] = Ids(products.Where(p => p.Activo && p.StockActual - p.StockReservado <= 0).Select(p => p.Id)),
                ["DISPONIBLE"] = Ids(products.Where(p => p.Activo && p.StockActual - p.StockReservado > p.StockMinimo).Select(p => p.Id)),
                ["INACTIVO"] = Ids(products.Where(p => !p.Activo).Select(p => p.Id))
            };
            foreach (var (status, expected) in expectedByStatus)
            {
                var filtered = await ReadAsync<PagedResult<InventoryStockDto>>(
                    await GetWithBearerAsync(client, $"/api/inventory/stock?status={status}&pageSize=100", scenario.Admin.AccessToken),
                    HttpStatusCode.OK);
                Assert.Equal(expected, Ids(filtered.Items.Select(x => x.ProductId)));
                Assert.Equal(expected.Length, filtered.TotalCount);
            }
            // Disponibles, stock bajo y agotados reparten a todos los activos sin solaparse.
            Assert.Equal(Ids(products.Where(p => p.Activo).Select(p => p.Id)),
                Ids(expectedByStatus["DISPONIBLE"].Concat(expectedByStatus["STOCK_BAJO"]).Concat(expectedByStatus["AGOTADO"])));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await GetWithBearerAsync(
            client, "/api/inventory/stock?status=OTRO", scenario.Admin.AccessToken)).StatusCode);
        // Los inactivos siguen exigiendo PRODUCTS.SENSITIVE_UPDATE, que el Supervisor no tiene por plantilla.
        Assert.Equal(HttpStatusCode.Forbidden, (await GetWithBearerAsync(
            client, "/api/inventory/stock?status=INACTIVO", grantedSupervisor.AccessToken)).StatusCode);

        static Guid[] Ids(IEnumerable<Guid> ids) => ids.OrderBy(id => id).ToArray();

        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.SupervisorUser.Id}/permissions/{permissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        var revokedSupervisor = await LoginAsync(client, SupervisorUser, SupervisorPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAdjustmentAsync(
            client, revokedSupervisor.AccessToken, "inventory-revoked-adjustment",
            request with { ExpectedVersion = scenario.ProductVersion + 1 })).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(15, await context.Producto.Where(x => x.Id == scenario.ProductId).Select(x => x.StockActual).SingleAsync());
        Assert.Equal(1, await context.MovimientoInventario.CountAsync(x => x.ProductoId == scenario.ProductId));
        Assert.Equal(1, await context.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO"));
        Assert.Equal(1, await context.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO"));
        var authorization = await context.AutorizacionOperacion.SingleAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO");
        Assert.Equal("UTILIZADA", authorization.Estado);
        Assert.NotEqual(authorization.UsuarioSolicitanteId, authorization.UsuarioAutorizadorId);
        Assert.Equal("INVENTORYADJUSTED", (await context.EventoAuditoria.SingleAsync(x => x.EntidadId == created.Id)).Accion);
        var persistedAudits = await context.EventoAuditoria.AsNoTracking().ToArrayAsync();
        var persistedText = string.Join('|', persistedAudits.Select(x => x.DatosNuevos ?? string.Empty));
        Assert.DoesNotContain(AuthorizerPassword, persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain("inventory-idempotent-adjustment", persistedText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Adjustment_AuthorizedWithScannedCredential_RecordsTheCredentialMethod()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, 10);
        SetBearer(client, scenario.Admin.AccessToken);
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.InventoryAdjust);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.SupervisorUser.Id}/permissions/{permissionId}",
            new SetUserPermissionRequest(true))).StatusCode);
        var supervisor = await LoginAsync(client, SupervisorUser, SupervisorPassword);
        var reasons = await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(client, "/api/inventory/adjustment-reasons", supervisor.AccessToken), HttpStatusCode.OK);
        var reason = reasons.Single(x => x.Code == "CONTEO_FISICO");
        SetBearer(client, scenario.Admin.AccessToken);
        var issued = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{scenario.AuthorizerUser.Id}", new IssueOperatorCredentialRequest(null)),
            HttpStatusCode.Created);

        var unknown = new CreateInventoryAdjustmentRequest(scenario.ProductId, 5, reason.Id, scenario.ProductVersion,
            "Credencial desconocida", new(null, null, "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF"));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAdjustmentAsync(
            client, supervisor.AccessToken, "inventory-unknown-credential", unknown)).StatusCode);

        var request = unknown with
        {
            Observation = "Conteo con credencial escaneada",
            Authorization = new(null, null, issued.CredentialToken)
        };
        var created = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(
            client, supervisor.AccessToken, "inventory-scanned-credential", request), HttpStatusCode.Created);
        Assert.Equal(15, created.NewStock);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var authorization = await context.AutorizacionOperacion.SingleAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO");
        Assert.Equal("CODIGO_BARRAS_PIN", authorization.MetodoAutenticacion);
        Assert.Equal(scenario.AuthorizerUser.Id, authorization.UsuarioAutorizadorId);
        Assert.Equal(issued.CredentialId, authorization.CredencialAutorizadorId);
        var persistedText = string.Join('|', (await context.EventoAuditoria.AsNoTracking().ToArrayAsync())
            .Select(x => x.DatosNuevos ?? string.Empty));
        Assert.DoesNotContain(issued.CredentialToken, persistedText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task WrongAuthorizerAttempts_ShareLoginLockout_AuditAndSanitization()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, 10);
        var supervisorToken = await GrantedSupervisorTokenAsync(client, scenario);
        var reason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(client, "/api/inventory/adjustment-reasons", supervisorToken),
            HttpStatusCode.OK)).Single(x => x.Code == "CONTEO_FISICO");
        var request = new CreateInventoryAdjustmentRequest(
            scenario.ProductId, 1, reason.Id, scenario.ProductVersion, "Credencial rechazada",
            new(AuthorizerUser, "Wrong-Authorizer-Password!"));

        await AssertProblemAsync(await PostAdjustmentAsync(
            client, supervisorToken, "wrong-authorizer-isolated", request), HttpStatusCode.Forbidden);
        Assert.Equal(1, (await ReadAuthenticationStateAsync(scenario.AuthorizerUser.Id)).Attempts);

        using var concurrentAdjustmentClient = CreateClient();
        using var concurrentLoginClient = CreateClient();
        var concurrentResponses = await Task.WhenAll(
            PostAdjustmentAsync(concurrentAdjustmentClient, supervisorToken,
                "wrong-authorizer-concurrent", request),
            concurrentLoginClient.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(AuthorizerUser, "Wrong-Login-Password!")));
        Assert.Equal(HttpStatusCode.Forbidden, concurrentResponses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, concurrentResponses[1].StatusCode);
        Assert.Equal(3, (await ReadAuthenticationStateAsync(scenario.AuthorizerUser.Id)).Attempts);

        await AssertProblemAsync(await PostAdjustmentAsync(
            client, supervisorToken, "wrong-authorizer-fourth", request), HttpStatusCode.Forbidden);
        Assert.Equal(4, (await ReadAuthenticationStateAsync(scenario.AuthorizerUser.Id)).Attempts);

        using var fifthAdjustmentClient = CreateClient();
        using var fifthLoginClient = CreateClient();
        var fifthResponses = await Task.WhenAll(
            PostAdjustmentAsync(fifthAdjustmentClient, supervisorToken,
                "wrong-authorizer-fifth-race", request),
            fifthLoginClient.PostAsJsonAsync("/api/auth/login",
                new LoginRequest(AuthorizerUser, "Wrong-Fifth-Password!")));
        Assert.Equal(HttpStatusCode.Forbidden, fifthResponses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, fifthResponses[1].StatusCode);

        var locked = await ReadAuthenticationStateAsync(scenario.AuthorizerUser.Id);
        Assert.Equal(5, locked.Attempts);
        Assert.NotNull(locked.LockedAt);
        await AssertProblemAsync(await PostAdjustmentAsync(client, supervisorToken,
            "blocked-authorizer-after-fifth", request with
            {
                Authorization = new(AuthorizerUser, AuthorizerPassword)
            }), HttpStatusCode.Forbidden);
        Assert.Equal(locked, await ReadAuthenticationStateAsync(scenario.AuthorizerUser.Id));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(
            x => x.Accion == "USERLOCKED" && x.EntidadId == scenario.AuthorizerUser.Id));
        Assert.True(await context.EventoAuditoria.CountAsync(
            x => x.Accion == "INVENTORYADJUSTMENTAUTHORIZATIONREJECTED") >= 4);
        var executor = await context.Usuario.AsNoTracking().SingleAsync(x => x.Id == scenario.SupervisorUser.Id);
        Assert.Equal(0, executor.IntentosFallidos);
        Assert.Null(executor.BloqueoHasta);
        Assert.Equal(0, await context.MovimientoInventario.CountAsync());
        Assert.Equal(0, await context.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO"));
        var authorizer = await context.Usuario.AsNoTracking().SingleAsync(x => x.Id == scenario.AuthorizerUser.Id);
        var auditRows = await context.EventoAuditoria.AsNoTracking().ToArrayAsync();
        var auditText = string.Join('|', auditRows.Select(x => x.DatosNuevos ?? string.Empty));
        Assert.DoesNotContain("Wrong-", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthorizerPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(authorizer.PasswordHash, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(supervisorToken, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong-authorizer", auditText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Authorizer_MustBeActiveAndHaveEffectiveIndividualGrant()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, 10);
        var reason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(client, "/api/inventory/adjustment-reasons", scenario.Admin.AccessToken),
            HttpStatusCode.OK)).Single(x => x.Code == "CONTEO_FISICO");
        var supervisorToken = await GrantedSupervisorTokenAsync(client, scenario);
        var request = new CreateInventoryAdjustmentRequest(
            scenario.ProductId, 1, reason.Id, scenario.ProductVersion, null,
            new(AuthorizerUser, AuthorizerPassword));
        SetBearer(client, scenario.Admin.AccessToken);
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.InventoryAdjust);

        // The authorizer's effective permission is revoked, granted again, and revoked once more.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.AuthorizerUser.Id}/permissions/{permissionId}", new SetUserPermissionRequest(false))).StatusCode);
        await AssertProblemAsync(await PostAdjustmentAsync(
            client, supervisorToken, "authorizer-without-grant", request), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.AuthorizerUser.Id}/permissions/{permissionId}", new SetUserPermissionRequest(true))).StatusCode);
        var created = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(
            client, supervisorToken, "authorizer-with-grant", request), HttpStatusCode.Created);

        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.AuthorizerUser.Id}/permissions/{permissionId}", new SetUserPermissionRequest(false))).StatusCode);
        await AssertProblemAsync(await PostAdjustmentAsync(client, supervisorToken,
            "authorizer-after-revoke", request with { ExpectedVersion = scenario.ProductVersion + 1 }),
            HttpStatusCode.Forbidden);

        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(
            $"/api/users/{scenario.SecondAuthorizerUser.Id}/deactivate", null)).StatusCode);
        await AssertProblemAsync(await PostAdjustmentAsync(client, supervisorToken,
            "inactive-authorizer", request with
            {
                ExpectedVersion = scenario.ProductVersion + 1,
                Authorization = new(SecondAuthorizerUser, SecondAuthorizerPassword)
            }), HttpStatusCode.Forbidden);
        Assert.NotEqual(Guid.Empty, created.Id);    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdjustmentValidation_RejectsUnknownJsonNumericLimitsReservationsAndInactiveProduct_AsProblemJson()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, 10);
        var reason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(client, "/api/inventory/adjustment-reasons", scenario.Admin.AccessToken),
            HttpStatusCode.OK)).Single(x => x.Code == "CONTEO_FISICO");
        var baseRequest = new CreateInventoryAdjustmentRequest(
            scenario.ProductId, 1, reason.Id, scenario.ProductVersion, null,
            null);

        var unknownJson = JsonSerializer.Serialize(new
        {
            productId = scenario.ProductId,
            quantityDelta = 1,
            reasonId = reason.Id,
            expectedVersion = scenario.ProductVersion,
            observation = (string?)null,
            authorization = (object?)null,
            unknownInventoryField = true
        });
        await AssertProblemAsync(await PostRawAdjustmentAsync(
            client, scenario.Admin.AccessToken, "unknown-json", unknownJson), HttpStatusCode.BadRequest);

        foreach (var quantity in new[] { "1.5", "2147483648", "-2147483649" })
        {
            var numericJson = $$$"""
                {"productId":"{{{scenario.ProductId}}}","quantityDelta":{{{quantity}}},"reasonId":"{{{reason.Id}}}",
                 "expectedVersion":{{{scenario.ProductVersion}}},"observation":null}
                """;
            await AssertProblemAsync(await PostRawAdjustmentAsync(
                client, scenario.Admin.AccessToken, $"numeric-{quantity}", numericJson), HttpStatusCode.BadRequest);
        }

        await AssertProblemAsync(await PostAdjustmentAsync(client, scenario.Admin.AccessToken,
            "int-min-value", baseRequest with { QuantityDelta = int.MinValue }), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await PostAdjustmentAsync(client, scenario.Admin.AccessToken,
            "int-max-value", baseRequest with { QuantityDelta = int.MaxValue }), HttpStatusCode.BadRequest);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var product = await context.Producto.SingleAsync(x => x.Id == scenario.ProductId);
            product.StockReservado = 8;
            await context.SaveChangesAsync();
        }
        var reservedVersion = await ReadProductVersionAsync(scenario.ProductId);
        await AssertProblemAsync(await PostAdjustmentAsync(client, scenario.Admin.AccessToken,
            "reserved-stock", baseRequest with { QuantityDelta = -3, ExpectedVersion = reservedVersion }),
            HttpStatusCode.BadRequest);

        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsync($"/api/products/{scenario.ProductId}/deactivate", null)).StatusCode);
        var inactiveVersion = await ReadProductVersionAsync(scenario.ProductId);
        await AssertProblemAsync(await PostAdjustmentAsync(client, scenario.Admin.AccessToken,
            "inactive-product", baseRequest with { ExpectedVersion = inactiveVersion }), HttpStatusCode.BadRequest);

        await using var verificationScope = _factory!.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(0, await verification.MovimientoInventario.CountAsync());
        Assert.Equal(0, await verification.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO"));
        Assert.Equal(0, await verification.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IdempotentReplay_ReevaluatesProductsCostsReadPermission()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, 10);
        var reason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(client, "/api/inventory/adjustment-reasons", scenario.Admin.AccessToken),
            HttpStatusCode.OK)).Single(x => x.Code == "CONTEO_FISICO");
        var request = new CreateInventoryAdjustmentRequest(
            scenario.ProductId, 1, reason.Id, scenario.ProductVersion, null,
            null);
        var created = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(
            client, scenario.Admin.AccessToken, "cost-redaction-replay", request), HttpStatusCode.Created);
        Assert.Equal(40m, created.PreviousAverageCost);

        SetBearer(client, scenario.Admin.AccessToken);
        var costsPermissionId = await GetPermissionIdAsync(client, PermissionCodes.ProductCostsRead);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Admin.User.Id}/permissions/{costsPermissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        var adminWithoutCosts = await LoginAsync(client, AdminUser, AdminPassword);
        var replay = await ReadAsync<InventoryMovementDto>(await PostAdjustmentAsync(
            client, adminWithoutCosts.AccessToken, "cost-redaction-replay", request), HttpStatusCode.Created);
        Assert.Equal(created.Id, replay.Id);
        Assert.Null(replay.PreviousAverageCost);
        Assert.Null(replay.NewAverageCost);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeferredCommitFailure_RollsBackCompleteAdjustmentAndSuccessfulReauthorizationOnly()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client, 10);
        var foreignFixture = await PrepareForeignSaleFixtureAsync(client, scenario);
        var supervisorToken = await GrantedSupervisorTokenAsync(client, scenario);
        var reason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(client, "/api/inventory/adjustment-reasons", supervisorToken),
            HttpStatusCode.OK)).Single(x => x.Code == "CONTEO_FISICO");
        var currentProductVersion = await ReadProductVersionAsync(scenario.ProductId);
        var failedRequest = new CreateInventoryAdjustmentRequest(
            scenario.ProductId, 1, reason.Id, currentProductVersion, null,
            new(AuthorizerUser, "Wrong-Before-Commit!"));
        await AssertProblemAsync(await PostAdjustmentAsync(client, supervisorToken,
            "persisted-rejection-before-commit", failedRequest), HttpStatusCode.Forbidden);

        var before = await CaptureRollbackStateAsync(scenario.ProductId, scenario.AuthorizerUser.Id);
        var foreignBefore = await CaptureForeignOperationalStateAsync(foreignFixture);
        AssertForeignFixtureIsRealAndRelated(foreignBefore, scenario);
        await InstallDeferredInventoryFailureAsync(
            scenario,
            "inventory-forced-commit-failure",
            before.Stock + 1,
            before.ProductVersion + 1);
        var validRequest = failedRequest with
        {
            Authorization = new(AuthorizerUser, AuthorizerPassword),
            Observation = "Debe revertirse por completo"
        };
        await AssertProblemAsync(await PostAdjustmentAsync(client, supervisorToken,
            "inventory-forced-commit-failure", validRequest), HttpStatusCode.Conflict);

        Assert.Equal(before, await CaptureRollbackStateAsync(scenario.ProductId, scenario.AuthorizerUser.Id));
        Assert.Equal(foreignBefore, await CaptureForeignOperationalStateAsync(foreignFixture));
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var observed = new NpgsqlCommand(
            "SELECT is_called FROM public.inventory_test_commit_observed", connection);
        Assert.True((bool)(await observed.ExecuteScalarAsync())!);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentAdjustments_WithSameVersion_PersistExactlyOneAdjustment()
    {
        using var setupClient = CreateClient();
        var scenario = await BootstrapAsync(setupClient, 10);
        var supervisorToken = await GrantedSupervisorTokenAsync(setupClient, scenario);
        var reason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(setupClient, "/api/inventory/adjustment-reasons", supervisorToken),
            HttpStatusCode.OK)).Single(x => x.Code == "CORRECCION_REGISTRO");
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();

        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        await LockActiveInstallationAsync(blocker, blockerTransaction);

        var first = PostAdjustmentAsync(firstClient, supervisorToken, "adjustment-race-a", new(
            scenario.ProductId, 1, reason.Id, scenario.ProductVersion, "Carrera A",
            new(AuthorizerUser, AuthorizerPassword)));
        var second = PostAdjustmentAsync(secondClient, supervisorToken, "adjustment-race-b", new(
            scenario.ProductId, 2, reason.Id, scenario.ProductVersion, "Carrera B",
            new(SecondAuthorizerUser, SecondAuthorizerPassword)));
        await WaitForInstallationLockWaitersAsync(2);
        await blockerTransaction.CommitAsync();

        var responses = await Task.WhenAll(first, second);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var winner = await ReadAsync<InventoryMovementDto>(
            responses.Single(x => x.StatusCode == HttpStatusCode.Created), HttpStatusCode.Created);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(10 + winner.StockQuantity,
            await context.Producto.Where(x => x.Id == scenario.ProductId).Select(x => x.StockActual).SingleAsync());
        Assert.Equal(1, await context.MovimientoInventario.CountAsync(x => x.ProductoId == scenario.ProductId));
        Assert.Equal(1, await context.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentSaleAndAdjustment_PreserveOneStockEffectAndOneMovement()
    {
        using var setupClient = CreateClient();
        var scenario = await BootstrapAsync(setupClient, 1);
        await OpenIndividualShiftAsync(setupClient, scenario.Admin.AccessToken);
        var reason = (await ReadAsync<IReadOnlyCollection<InventoryAdjustmentReasonDto>>(
            await GetWithBearerAsync(setupClient, "/api/inventory/adjustment-reasons", scenario.Admin.AccessToken),
            HttpStatusCode.OK)).Single(x => x.Code == "PERDIDA");
        using var saleClient = CreateClient();
        using var adjustmentClient = CreateClient();

        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        await LockActiveInstallationAsync(blocker, blockerTransaction);

        var saleTask = PostSaleAsync(saleClient, scenario.Admin.AccessToken, "inventory-sale-race", scenario.ProductId);
        var adjustmentTask = PostAdjustmentAsync(adjustmentClient, scenario.Admin.AccessToken, "inventory-adjustment-race", new(
            scenario.ProductId, -1, reason.Id, scenario.ProductVersion, "Pérdida concurrente",
            null));
        await WaitForInstallationLockWaitersAsync(2);
        await blockerTransaction.CommitAsync();

        var saleResponse = await saleTask;
        var adjustmentResponse = await adjustmentTask;
        Assert.Equal(1, new[] { saleResponse, adjustmentResponse }.Count(x => x.StatusCode == HttpStatusCode.Created));
        if (saleResponse.StatusCode == HttpStatusCode.Created)
            Assert.Equal(HttpStatusCode.Conflict, adjustmentResponse.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, saleResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Created, adjustmentResponse.StatusCode);
        }

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(0, await context.Producto.Where(x => x.Id == scenario.ProductId).Select(x => x.StockActual).SingleAsync());
        Assert.Equal(1, await context.MovimientoInventario.CountAsync(x => x.ProductoId == scenario.ProductId));
        var movement = await context.MovimientoInventario.SingleAsync(x => x.ProductoId == scenario.ProductId);
        Assert.Contains(movement.TipoMovimiento, new[] { "VENTA", "AJUSTE_SALIDA" });
        Assert.Equal(movement.TipoMovimiento == "VENTA" ? 1 : 0, await context.Venta.CountAsync());
    }

    /// <summary>Grants the Supervisor INVENTORY.ADJUST (it is not part of its role) and signs it in; the Administrators then authorize.</summary>
    private static async Task<string> GrantedSupervisorTokenAsync(HttpClient client, Scenario scenario)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.InventoryAdjust);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.SupervisorUser.Id}/permissions/{permissionId}", new SetUserPermissionRequest(true))).StatusCode);
        return (await LoginAsync(client, SupervisorUser, SupervisorPassword)).AccessToken;
    }

    private HttpClient CreateClient()
    {
        return _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    private async Task<Scenario> BootstrapAsync(HttpClient client, int initialStock)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                "Comercio ficticio de Inventario", "INVENTORY-TEST", "INVENTORY-INSTALLATION", "INVTEST1",
                "INVENTORY-TERMINAL", "Terminal ficticia de Inventario", AdminUser,
                "Administrador ficticio de Inventario", AdminPassword))
        })
        {
            request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
            Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        }

        var admin = await LoginAsync(client, AdminUser, AdminPassword);
        SetBearer(client, admin.AccessToken);
        var roles = await ReadAsync<PagedResult<RoleDto>>(await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        var administratorRole = roles.Items.Single(x => x.Code == RoleCodes.Administrator).Id;
        var supervisorRole = roles.Items.Single(x => x.Code == RoleCodes.Supervisor).Id;
        var authorizer = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            AuthorizerUser, "Autorizador ficticio de Inventario", AuthorizerPassword, [administratorRole])), HttpStatusCode.Created);
        var secondAuthorizer = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            SecondAuthorizerUser, "Segundo autorizador ficticio de Inventario", SecondAuthorizerPassword,
            [administratorRole])), HttpStatusCode.Created);
        var supervisorUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            SupervisorUser, "Supervisor ficticio de Inventario", SupervisorPassword, [supervisorRole])), HttpStatusCode.Created);
        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync(
            "/api/categories", new CreateCategoryRequest("Categoría aislada de Inventario", null, "INV")), HttpStatusCode.Created);
        var units = await ReadAsync<IReadOnlyCollection<MeasurementUnitDto>>(
            await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        var product = await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products", new CreateProductRequest(
            category.Id, units.Single(x => x.Code == "UNIDAD").Id, null, "INV-PRODUCT", null, null, true,
            "Producto ficticio de Inventario", null, 100m, null, null, 0)), HttpStatusCode.Created);

        long version;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var row = await context.Producto.SingleAsync(x => x.Id == product.Id);
            row.StockActual = initialStock;
            row.CostoPromedio = 40m;
            // The VTA consecutive is created by the installation itself.
            await context.SaveChangesAsync();
            version = row.Version;
        }

        return new Scenario(
            admin,
            await LoginAsync(client, SupervisorUser, SupervisorPassword),
            authorizer,
            secondAuthorizer,
            supervisorUser,
            product.Id,
            version);
    }

    private static async Task<ForeignFixtureIds> PrepareForeignSaleFixtureAsync(HttpClient client, Scenario scenario)
    {
        SetBearer(client, scenario.Admin.AccessToken);
        _ = await ReadAsync<CashRegisterDto>(await client.PostAsJsonAsync(
            "/api/cash/register",
            new ConfigureCashRegisterRequest("INV-ROLLBACK-CASH", "Caja ajena para rollback de Inventario")),
            HttpStatusCode.Created);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        _ = await ReadAsync<CashStateDto>(await client.PutAsJsonAsync(
            "/api/cash/shift-mode",
            new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)),
            HttpStatusCode.OK);
        var shift = await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync(
            "/api/cash/shifts",
            new OpenCashShiftRequest(500m, "Turno ajeno previo al ajuste fallido")),
            HttpStatusCode.Created);
        var credential = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{scenario.SupervisorUser.Id}",
            new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);

        SetBearer(client, scenario.Supervisor.AccessToken);
        var session = await ReadAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions",
            new StartOperatorSessionRequest(credential.CredentialToken)), HttpStatusCode.Created);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(
            client,
            scenario.Supervisor.AccessToken,
            "foreign-sale-before-inventory-rollback",
            scenario.ProductId), HttpStatusCode.Created);
        return new ForeignFixtureIds(shift.Id, session.Id, sale.Id);
    }

    private static async Task OpenIndividualShiftAsync(HttpClient client, string token)
    {
        SetBearer(client, token);
        await ReadAsync<CashRegisterDto>(await client.PostAsJsonAsync(
            "/api/cash/register", new ConfigureCashRegisterRequest("INV-CASH", "Caja aislada de Inventario")),
            HttpStatusCode.Created);
        await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(0m, "Turno para carrera Venta-Inventario")),
            HttpStatusCode.Created);
    }

    private static async Task<HttpResponseMessage> GetWithBearerAsync(HttpClient client, string path, string token)
    {
        SetBearer(client, token);
        return await client.GetAsync(path);
    }

    private static async Task<HttpResponseMessage> PostAdjustmentAsync(
        HttpClient client,
        string token,
        string idempotencyKey,
        CreateInventoryAdjustmentRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/adjustments")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> PostRawAdjustmentAsync(
        HttpClient client,
        string token,
        string idempotencyKey,
        string json)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/adjustments")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> PostSaleAsync(
        HttpClient client,
        string token,
        string idempotencyKey,
        Guid productId)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/sales")
        {
            Content = JsonContent.Create(new ConfirmSaleRequest(
                null, [new SaleLineRequest(productId, 1, "MINORISTA")], null, null,
                [new SalePaymentRequest("EFECTIVO", 100m, 100m, null)]))
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string userName, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(userName, password)), HttpStatusCode.OK);
    }

    private async Task<(int Attempts, DateTime? LockedAt, long Version)> ReadAuthenticationStateAsync(Guid userId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        return await context.Usuario.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new ValueTuple<int, DateTime?, long>(x.IntentosFallidos, x.BloqueoHasta, x.Version))
            .SingleAsync();
    }

    private async Task<long> ReadProductVersionAsync(Guid productId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        return await context.Producto.AsNoTracking().Where(x => x.Id == productId)
            .Select(x => x.Version).SingleAsync();
    }

    private async Task<RollbackState> CaptureRollbackStateAsync(Guid productId, Guid authorizerId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await context.Producto.AsNoTracking().SingleAsync(x => x.Id == productId);
        var authorizer = await context.Usuario.AsNoTracking().SingleAsync(x => x.Id == authorizerId);
        return new RollbackState(
            product.StockActual,
            product.StockReservado,
            product.Version,
            await context.MovimientoInventario.CountAsync(),
            await context.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO"),
            await context.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "AJUSTAR_INVENTARIO"),
            await context.EventoAuditoria.CountAsync(),
            authorizer.IntentosFallidos,
            authorizer.BloqueoHasta,
            authorizer.UltimoAcceso,
            authorizer.Version,
            await context.SesionOperador.CountAsync(),
            await context.Venta.CountAsync(),
            await context.TurnoCaja.CountAsync(),
            await context.MovimientoCaja.CountAsync());
    }

    private async Task<ForeignOperationalState> CaptureForeignOperationalStateAsync(ForeignFixtureIds fixture)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var shift = await context.TurnoCaja.AsNoTracking().SingleAsync(x => x.Id == fixture.ShiftId);
        var session = await context.SesionOperador.AsNoTracking().SingleAsync(x => x.Id == fixture.SessionId);
        var sale = await context.Venta.AsNoTracking().SingleAsync(x => x.Id == fixture.SaleId);
        var detail = await context.DetalleVenta.AsNoTracking().SingleAsync(x => x.VentaId == sale.Id);
        var payment = await context.PagoVenta.AsNoTracking().SingleAsync(x => x.VentaId == sale.Id);
        var cashMovement = await context.MovimientoCaja.AsNoTracking()
            .SingleAsync(x => x.Id == payment.MovimientoCajaId);
        var inventoryMovement = await context.MovimientoInventario.AsNoTracking()
            .SingleAsync(x => x.VentaId == sale.Id && x.ProductoId == detail.ProductoId);
        var product = await context.Producto.AsNoTracking().SingleAsync(x => x.Id == detail.ProductoId);
        var idempotency = await context.OperacionIdempotente.AsNoTracking()
            .SingleAsync(x => x.EntidadId == sale.Id);

        return new ForeignOperationalState(
            shift.Id, shift.CajaId, shift.TerminalId, shift.ModoOperacion,
            shift.UsuarioAperturaId, shift.UsuarioResponsableId, shift.FechaOperativa,
            shift.FechaHoraApertura, shift.MontoInicial, shift.Estado, shift.Version,
            session.Id, session.TurnoCajaId, session.TerminalId, session.UsuarioId,
            session.CredencialUsuarioId, session.FechaInicio, session.FechaUltimoUso,
            session.FechaFin, session.Estado, session.MotivoCierre, session.CorrelacionId,
            sale.Id, sale.InstalacionId, sale.TurnoCajaId, sale.UsuarioId,
            sale.SesionOperadorId, sale.Prefijo, sale.Serie, sale.Consecutivo,
            sale.FechaHora, sale.SubtotalBruto, sale.DescuentoTotal, sale.Total,
            sale.Estado, sale.Version,
            detail.Id, detail.VentaId, detail.ProductoId, detail.Cantidad,
            detail.PrecioUnitario, detail.SubtotalBruto, detail.SubtotalNeto,
            payment.Id, payment.VentaId, payment.MetodoPagoId, payment.MovimientoCajaId,
            payment.UsuarioId, payment.SesionOperadorId, payment.ValorAplicado,
            payment.ValorRecibido, payment.Cambio, payment.Estado, payment.FechaHora,
            cashMovement.Id, cashMovement.TurnoCajaId, cashMovement.UsuarioId,
            cashMovement.SesionOperadorId, cashMovement.Tipo,
            cashMovement.CategoriaMovimiento, cashMovement.Valor,
            cashMovement.CorrelacionId,
            inventoryMovement.Id, inventoryMovement.ProductoId,
            inventoryMovement.UsuarioId, inventoryMovement.SesionOperadorId,
            inventoryMovement.VentaId, inventoryMovement.TipoMovimiento,
            inventoryMovement.CantidadStock, inventoryMovement.StockAnterior,
            inventoryMovement.StockPosterior, inventoryMovement.ReservadoAnterior,
            inventoryMovement.ReservadoPosterior, inventoryMovement.CorrelacionId,
            product.StockActual, product.StockReservado, product.Version,
            idempotency.Id, idempotency.EntidadId, idempotency.UsuarioId,
            idempotency.SesionOperadorId, idempotency.TerminalId,
            idempotency.TipoOperacion, idempotency.CorrelacionId,
            await context.DetalleVenta.CountAsync(x => x.VentaId == sale.Id),
            await context.PagoVenta.CountAsync(x => x.VentaId == sale.Id),
            await context.MovimientoInventario.CountAsync(x => x.VentaId == sale.Id),
            await context.MovimientoCaja.CountAsync(x => x.TurnoCajaId == shift.Id));
    }

    private static void AssertForeignFixtureIsRealAndRelated(
        ForeignOperationalState state,
        Scenario scenario)
    {
        Assert.NotEqual(Guid.Empty, state.ShiftId);
        Assert.Equal(CashConstants.SharedMode, state.ShiftMode);
        Assert.Equal("ABIERTA", state.ShiftState);
        Assert.Equal(500m, state.ShiftInitialAmount);
        Assert.True(state.ShiftVersion > 0);

        Assert.NotEqual(Guid.Empty, state.SessionId);
        Assert.Equal(state.ShiftId, state.SessionShiftId);
        Assert.Equal(state.ShiftTerminalId, state.SessionTerminalId);
        Assert.Equal(scenario.SupervisorUser.Id, state.SessionUserId);
        Assert.NotEqual(scenario.Admin.User.Id, state.SessionUserId);
        Assert.Equal("CERRADA", state.SessionState);
        Assert.Equal("CAMBIO_OPERADOR", state.SessionCloseReason);
        Assert.NotNull(state.SessionEndedAt);

        Assert.NotEqual(Guid.Empty, state.SaleId);
        Assert.Equal(state.ShiftId, state.SaleShiftId);
        Assert.Equal(state.SessionId, state.SaleSessionId);
        Assert.Equal(state.SessionUserId, state.SaleUserId);
        Assert.NotEqual(scenario.Admin.User.Id, state.SaleUserId);
        Assert.Equal("CONFIRMADA", state.SaleState);
        Assert.Equal(100m, state.SaleTotal);
        Assert.True(state.SaleVersion > 0);

        Assert.Equal(state.SaleId, state.DetailSaleId);
        Assert.Equal(scenario.ProductId, state.DetailProductId);
        Assert.Equal(1, state.DetailQuantity);
        Assert.Equal(100m, state.DetailUnitPrice);
        Assert.Equal(100m, state.DetailNetAmount);

        Assert.Equal(state.SaleId, state.PaymentSaleId);
        Assert.Equal(state.SessionId, state.PaymentSessionId);
        Assert.Equal(state.SaleUserId, state.PaymentUserId);
        Assert.Equal(100m, state.PaymentAppliedAmount);
        Assert.Equal(100m, state.PaymentReceivedAmount);
        Assert.Equal(0m, state.PaymentChange);
        Assert.NotEqual(Guid.Empty, state.PaymentCashMovementId);

        Assert.Equal(state.PaymentCashMovementId, state.CashMovementId);
        Assert.Equal(state.ShiftId, state.CashMovementShiftId);
        Assert.Equal(state.SessionId, state.CashMovementSessionId);
        Assert.Equal(state.SaleUserId, state.CashMovementUserId);
        Assert.Equal("INGRESO", state.CashMovementType);
        Assert.Equal("VENTA", state.CashMovementCategory);
        Assert.Equal(100m, state.CashMovementAmount);

        Assert.Equal(state.SaleId, state.InventoryMovementSaleId);
        Assert.Equal(state.SessionId, state.InventoryMovementSessionId);
        Assert.Equal(state.SaleUserId, state.InventoryMovementUserId);
        Assert.Equal(scenario.ProductId, state.InventoryMovementProductId);
        Assert.Equal("VENTA", state.InventoryMovementType);
        Assert.Equal(-1, state.InventoryMovementQuantity);
        Assert.Equal(10, state.InventoryMovementPreviousStock);
        Assert.Equal(9, state.InventoryMovementNewStock);
        Assert.Equal(9, state.ProductStock);
        Assert.Equal(scenario.ProductVersion + 1, state.ProductVersion);

        Assert.Equal(state.SaleId, state.IdempotencyEntityId);
        Assert.Equal(state.SessionId, state.IdempotencySessionId);
        Assert.Equal(state.SaleUserId, state.IdempotencyUserId);
        Assert.Equal(state.ShiftTerminalId, state.IdempotencyTerminalId);
        Assert.Equal("REGISTRAR_VENTA", state.IdempotencyOperationType);
        Assert.Equal(1, state.SaleDetailCount);
        Assert.Equal(1, state.SalePaymentCount);
        Assert.Equal(1, state.SaleInventoryMovementCount);
        Assert.Equal(2, state.ShiftCashMovementCount);
    }

    private async Task InstallDeferredInventoryFailureAsync(
        Scenario scenario,
        string idempotencyKey,
        int expectedStockAfterAdjustment,
        long expectedVersionAfterAdjustment)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE SEQUENCE public.inventory_test_commit_observed;
            CREATE FUNCTION public.inventory_test_fail_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.producto_id = '{{scenario.ProductId}}'::uuid THEN
                IF EXISTS (
                     SELECT 1 FROM catalogo.producto
                     WHERE id = NEW.producto_id
                       AND stock_actual = {{expectedStockAfterAdjustment}}
                       AND version = {{expectedVersionAfterAdjustment}})
                  AND EXISTS (
                     SELECT 1 FROM seguridad.autorizacion_operacion
                     WHERE id = NEW.autorizacion_operacion_id
                       AND tipo_operacion = 'AJUSTAR_INVENTARIO'
                       AND estado = 'UTILIZADA')
                  AND EXISTS (
                     SELECT 1 FROM auditoria.operacion_idempotente
                     WHERE clave_idempotencia = '{{idempotencyKey}}'
                       AND entidad_id = NEW.id)
                  AND EXISTS (
                     SELECT 1 FROM auditoria.evento_auditoria
                     WHERE accion = 'INVENTORYADJUSTED'
                       AND entidad_id = NEW.id)
                  AND EXISTS (
                     SELECT 1 FROM seguridad.usuario
                     WHERE id = '{{scenario.AuthorizerUser.Id}}'::uuid
                       AND intentos_fallidos = 0
                       AND bloqueo_hasta IS NULL)
                THEN
                  PERFORM nextval('public.inventory_test_commit_observed');
                END IF;
                RAISE EXCEPTION 'Forced isolated inventory deferred failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER zz_inventory_test_fail_commit
              AFTER INSERT ON compras.movimiento_inventario DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION public.inventory_test_fail_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(body);
        Assert.Equal((int)expected, document.RootElement.GetProperty("status").GetInt32());
    }

    private static async Task<Guid> GetPermissionIdAsync(HttpClient client, string code)
    {
        var permissions = await ReadAsync<PagedResult<PermissionDto>>(
            await client.GetAsync("/api/permissions?page=1&pageSize=100"), HttpStatusCode.OK);
        return permissions.Items.Single(x => x.Code == code).Id;
    }

    private static async Task LockActiveInstallationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM configuracion.instalacion
            WHERE activo
            ORDER BY fecha_instalacion, id
            LIMIT 1
            FOR UPDATE
            """, connection, transaction);
        Assert.NotNull(await command.ExecuteScalarAsync());
    }

    private async Task WaitForInstallationLockWaitersAsync(int expectedCount)
    {
        await using var observer = new NpgsqlConnection(_postgres.GetConnectionString());
        await observer.OpenAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT count(*)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND wait_event_type = 'Lock'
                  AND query LIKE '%FROM configuracion.instalacion AS i%'
                  AND query LIKE '%FOR UPDATE%'
                """, observer);
            if ((long)(await command.ExecuteScalarAsync())! >= expectedCount) return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"No se observaron {expectedCount} solicitudes esperando el bloqueo de instalación.");
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} ({expected}) but received {(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
        var value = JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web);
        Assert.NotNull(value);
        return value;
    }

    private static void SetBearer(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static string RandomSecret(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private sealed record Scenario(
        AuthenticationResult Admin,
        AuthenticationResult Supervisor,
        UserDto AuthorizerUser,
        UserDto SecondAuthorizerUser,
        UserDto SupervisorUser,
        Guid ProductId,
        long ProductVersion);

    private sealed record ForeignFixtureIds(Guid ShiftId, Guid SessionId, Guid SaleId);

    private sealed record ForeignOperationalState(
        Guid ShiftId,
        Guid CashRegisterId,
        Guid ShiftTerminalId,
        string ShiftMode,
        Guid ShiftOpeningUserId,
        Guid? ShiftResponsibleUserId,
        DateOnly ShiftOperationalDate,
        DateTime ShiftOpenedAt,
        decimal ShiftInitialAmount,
        string ShiftState,
        long ShiftVersion,
        Guid SessionId,
        Guid SessionShiftId,
        Guid SessionTerminalId,
        Guid SessionUserId,
        Guid SessionCredentialId,
        DateTime SessionStartedAt,
        DateTime SessionLastUsedAt,
        DateTime? SessionEndedAt,
        string SessionState,
        string? SessionCloseReason,
        Guid SessionCorrelationId,
        Guid SaleId,
        Guid SaleInstallationId,
        Guid SaleShiftId,
        Guid SaleUserId,
        Guid? SaleSessionId,
        string SalePrefix,
        string SaleSeries,
        long SaleNumber,
        DateTime SaleOccurredAt,
        decimal SaleGrossAmount,
        decimal SaleDiscountAmount,
        decimal SaleTotal,
        string SaleState,
        long SaleVersion,
        Guid DetailId,
        Guid DetailSaleId,
        Guid DetailProductId,
        int DetailQuantity,
        decimal DetailUnitPrice,
        decimal DetailGrossAmount,
        decimal DetailNetAmount,
        Guid PaymentId,
        Guid PaymentSaleId,
        Guid PaymentMethodId,
        Guid? PaymentCashMovementId,
        Guid PaymentUserId,
        Guid? PaymentSessionId,
        decimal PaymentAppliedAmount,
        decimal? PaymentReceivedAmount,
        decimal PaymentChange,
        string PaymentState,
        DateTime PaymentOccurredAt,
        Guid CashMovementId,
        Guid CashMovementShiftId,
        Guid CashMovementUserId,
        Guid? CashMovementSessionId,
        string CashMovementType,
        string CashMovementCategory,
        decimal CashMovementAmount,
        Guid CashMovementCorrelationId,
        Guid InventoryMovementId,
        Guid InventoryMovementProductId,
        Guid InventoryMovementUserId,
        Guid? InventoryMovementSessionId,
        Guid? InventoryMovementSaleId,
        string InventoryMovementType,
        int InventoryMovementQuantity,
        int InventoryMovementPreviousStock,
        int InventoryMovementNewStock,
        int InventoryMovementPreviousReserved,
        int InventoryMovementNewReserved,
        Guid InventoryMovementCorrelationId,
        int ProductStock,
        int ProductReserved,
        long ProductVersion,
        Guid IdempotencyId,
        Guid? IdempotencyEntityId,
        Guid IdempotencyUserId,
        Guid? IdempotencySessionId,
        Guid? IdempotencyTerminalId,
        string IdempotencyOperationType,
        Guid IdempotencyCorrelationId,
        int SaleDetailCount,
        int SalePaymentCount,
        int SaleInventoryMovementCount,
        int ShiftCashMovementCount);

    private sealed record RollbackState(
        int Stock,
        int Reserved,
        long ProductVersion,
        int InventoryMovements,
        int InventoryAuthorizations,
        int InventoryIdempotencyRows,
        int AuditRows,
        int AuthorizerAttempts,
        DateTime? AuthorizerLockedAt,
        DateTime? AuthorizerLastAccess,
        long AuthorizerVersion,
        int OperatorSessions,
        int Sales,
        int CashShifts,
        int CashMovements);
}
