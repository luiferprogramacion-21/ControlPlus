using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests : IAsyncLifetime
{
    private const string AdministratorUserName = "cash.admin.test";
    private const string AdministratorPassword = "Cash-Administrator-Test-2026!";
    private const string SupervisorUserName = "cash.supervisor.test";
    private const string SupervisorPassword = "Cash-Supervisor-Test-2026!";
    private const string CashierUserName = "cash.cashier.test";
    private const string CashierPassword = "Cash-Cashier-Test-2026!";

    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtSigningKey = RandomSecret(64);
    private readonly PostgreSqlContainer _postgres;
    private WebApplicationFactory<Program>? _factory;
    private readonly ControlledCashClock _clock = new();

    public CashApiFlowTests()
    {
        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("controlplus_cash_api_test")
            .WithUsername("controlplus_test")
            .WithPassword(RandomSecret(32))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ControlPlusDbContext(options);
        await context.Database.MigrateAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-cash-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-cash-test-client");
            builder.UseSetting("Jwt:SigningKey", _jwtSigningKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "240");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
                        notBefore <= _clock.UtcNow.UtcDateTime && expires > _clock.UtcNow.UtcDateTime);
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IndividualShift_EnforcesConcurrencyHybridPermissionsReconciliationAndAuthorizedDifference()
    {
        using var client = CreateClient();
        await SetupAsync(client);
        var administrator = await LoginAsync(client, AdministratorUserName, AdministratorPassword);
        SetBearer(client, administrator.AccessToken);
        var catalog = await SeedReconciliationCatalogAsync();
        var users = await CreateOperationalUsersAsync(client);
        var supervisor = await LoginAsync(client, SupervisorUserName, SupervisorPassword);
        var cashier = await LoginAsync(client, CashierUserName, CashierPassword);

        SetBearer(client, supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/cash/register", new ConfigureCashRegisterRequest("TEST-CASH", "Caja aislada"))).StatusCode);

        SetBearer(client, administrator.AccessToken);
        var registerResponse = await client.PostAsJsonAsync(
            "/api/cash/register", new ConfigureCashRegisterRequest("TEST-CASH", "Caja aislada"));
        var register = await ReadSuccessAsync<CashRegisterDto>(registerResponse, HttpStatusCode.Created);
        Assert.Equal("TEST-CASH", register.Code);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            "/api/cash/register", new ConfigureCashRegisterRequest("SECOND", "Caja repetida"))).StatusCode);

        SetBearer(client, cashier.AccessToken);
        var cashierState = await ReadSuccessAsync<CashStateDto>(
            await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        Assert.True(cashierState.IsConfigured);
        Assert.False(cashierState.HasOpenShift);
        Assert.Null(cashierState.CurrentShift);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(
            "/api/cash/shift-mode",
            new UpdateCashShiftModeRequest(CashConstants.IndividualMode, cashierState.ConfigurationVersion ?? 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(100_000m, "Apertura rechazada"))).StatusCode);

        SetBearer(client, supervisor.AccessToken);
        var supervisorState = await ReadSuccessAsync<CashStateDto>(
            await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        var configuredState = await ReadSuccessAsync<CashStateDto>(
            await client.PutAsJsonAsync(
                "/api/cash/shift-mode",
                new UpdateCashShiftModeRequest(CashConstants.IndividualMode, supervisorState.ConfigurationVersion!.Value)),
            HttpStatusCode.OK);
        Assert.Equal(CashConstants.IndividualMode, configuredState.DefaultShiftMode);

        SetBearer(client, administrator.AccessToken);
        var shiftPermissionId = await GetPermissionIdAsync(client, PermissionCodes.CashShiftManage);
        var movementPermissionId = await GetPermissionIdAsync(client, PermissionCodes.CashMovementsManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.Id}/permissions/{shiftPermissionId}",
            new SetUserPermissionRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.Id}/permissions/{movementPermissionId}",
            new SetUserPermissionRequest(true))).StatusCode);

        cashier = await LoginAsync(client, CashierUserName, CashierPassword);
        SetBearer(client, cashier.AccessToken);
        var concurrentOpenings = await Task.WhenAll(
            client.PostAsJsonAsync("/api/cash/shifts", new OpenCashShiftRequest(100_000m, "Apertura A")),
            client.PostAsJsonAsync("/api/cash/shifts", new OpenCashShiftRequest(100_000m, "Apertura B")));
        Assert.Single(concurrentOpenings, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(concurrentOpenings, response => response.StatusCode == HttpStatusCode.Conflict);
        var openedResponse = concurrentOpenings.Single(response => response.StatusCode == HttpStatusCode.Created);
        var opened = (await openedResponse.Content.ReadFromJsonAsync<CashShiftDto>())!;
        Assert.Equal(CashConstants.IndividualMode, opened.Mode);
        Assert.Equal(users.Cashier.Id, opened.ResponsibleUserId);
        Assert.Equal(100_000m, opened.InitialAmount);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/cash/movements/incomes", new CreateCashMovementRequest(0m, "Monto inválido", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/cash/movements/expenses", new CreateCashMovementRequest(1.5m, "Monto fraccionario", null))).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            "/api/cash/movements/incomes", new CreateCashMovementRequest(25_000m, "Ingreso de prueba", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            "/api/cash/movements/expenses", new CreateCashMovementRequest(200_000m, "Egreso sin fondos", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            "/api/cash/movements/expenses", new CreateCashMovementRequest(5_000m, "Gasto de prueba", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            "/api/cash/movements/cash-drops", new CreateCashMovementRequest(10_000m, "Sangría de prueba", null))).StatusCode);

        var movements = await ReadSuccessAsync<PagedResult<CashMovementDto>>(
            await client.GetAsync("/api/cash/movements?page=1&pageSize=20"), HttpStatusCode.OK);
        Assert.Equal(4, movements.TotalCount);
        Assert.Contains(movements.Items, movement => movement.Category == CashConstants.ManualIncomeCategory);
        Assert.Contains(movements.Items, movement => movement.Category == CashConstants.ExpenseCategory);
        Assert.Contains(movements.Items, movement => movement.Category == CashConstants.CashDropCategory);

        var reconciliation = await ReadSuccessAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(110_000m, reconciliation.TotalExpected);
        Assert.Equal(3, reconciliation.PaymentMethods.Count);
        Assert.Equal(110_000m, reconciliation.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        Assert.All(reconciliation.PaymentMethods.Where(x => x.Code != "EFECTIVO"), method => Assert.Equal(0m, method.ExpectedAmount));

        var balancedCounts = reconciliation.PaymentMethods
            .Select(method => new CountedPaymentMethodRequest(method.PaymentMethodId, method.ExpectedAmount))
            .ToArray();
        var staleClose = new CloseCashShiftRequest(
            reconciliation.ShiftVersion - 1, balancedCounts, null, null, null);
        await AssertProblemCodeAsync(
            await client.PostAsJsonAsync($"/api/cash/shifts/{opened.Id}/close", staleClose),
            HttpStatusCode.Conflict,
            "concurrency.conflict");

        var unbalancedCounts = reconciliation.PaymentMethods
            .Select(method => new CountedPaymentMethodRequest(
                method.PaymentMethodId,
                method.Code == "EFECTIVO" ? method.ExpectedAmount - 1_000m : method.ExpectedAmount))
            .ToArray();
        var sameActorAuthorization = new CloseCashShiftRequest(
            reconciliation.ShiftVersion,
            unbalancedCounts,
            catalog.DifferenceReasonId,
            "Diferencia ficticia para prueba",
            new CashCloseAuthorizationRequest(CashierUserName, CashierPassword));
        var withoutAuthorization = sameActorAuthorization with { Authorization = null };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{opened.Id}/close", withoutAuthorization)).StatusCode);
        await AssertFailedCloseWasAtomicAsync(opened.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{opened.Id}/close", sameActorAuthorization)).StatusCode);
        await AssertFailedCloseWasAtomicAsync(opened.Id);

        var authorizedClose = sameActorAuthorization with
        {
            Authorization = new CashCloseAuthorizationRequest(SupervisorUserName, SupervisorPassword)
        };
        var wrongPasswordClose = authorizedClose with
        {
            Authorization = new CashCloseAuthorizationRequest(SupervisorUserName, "Invalid-Cash-Authorization-Test-2026!")
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{opened.Id}/close", wrongPasswordClose)).StatusCode);
        await AssertFailedCloseWasAtomicAsync(opened.Id);
        await AssertUserAuthenticationStateAsync(users.Supervisor.Id, expectedFailedAttempts: 1, expectedLocked: false);

        SetBearer(client, administrator.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(
            $"/api/users/{users.Supervisor.Id}/deactivate", null)).StatusCode);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{opened.Id}/close", authorizedClose)).StatusCode);
        await AssertFailedCloseWasAtomicAsync(opened.Id);

        SetBearer(client, administrator.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(
            $"/api/users/{users.Supervisor.Id}/activate", null)).StatusCode);
        await SetUserBlockedAsync(users.Supervisor.Id, true);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{opened.Id}/close", authorizedClose)).StatusCode);
        await AssertFailedCloseWasAtomicAsync(opened.Id);

        await SetUserBlockedAsync(users.Supervisor.Id, false);
        SetBearer(client, administrator.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Supervisor.Id}/permissions/{shiftPermissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{opened.Id}/close", authorizedClose)).StatusCode);
        await AssertFailedCloseWasAtomicAsync(opened.Id);

        SetBearer(client, administrator.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(
            $"/api/users/{users.Supervisor.Id}/permissions")).StatusCode);
        SetBearer(client, cashier.AccessToken);
        var closed = await ReadSuccessAsync<CashReconciliationDto>(
            await client.PostAsJsonAsync($"/api/cash/shifts/{opened.Id}/close", authorizedClose),
            HttpStatusCode.OK);
        Assert.Equal(109_000m, closed.TotalCounted);
        Assert.Equal(-1_000m, closed.TotalDifference);
        await AssertUserAuthenticationStateAsync(users.Supervisor.Id, expectedFailedAttempts: 0, expectedLocked: false);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{opened.Id}/close", authorizedClose with { Version = closed.ShiftVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            "/api/cash/movements/incomes", new CreateCashMovementRequest(1_000m, "Turno cerrado", null))).StatusCode);

        await AssertClosedShiftPersistenceAsync(
            opened.Id,
            users.Cashier.Id,
            users.Supervisor.Id,
            catalog.PaymentMethodIds,
            expectedCash: 110_000m,
            countedCash: 109_000m,
            expectedDifference: -1_000m);

        var secondShift = await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(20_000m, "Segundo turno histórico")),
            HttpStatusCode.Created);
        Assert.NotEqual(opened.Id, secondShift.Id);
        await AssertShiftHistoryCountAsync(register.Id, 2);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SharedShift_UsesCode128HashedCredentialActiveSessionAndHybridMovementPermission()
    {
        using var client = CreateClient();
        await SetupAsync(client);
        var administrator = await LoginAsync(client, AdministratorUserName, AdministratorPassword);
        SetBearer(client, administrator.AccessToken);
        var catalog = await SeedReconciliationCatalogAsync();
        var users = await CreateOperationalUsersAsync(client);
        var supervisor = await LoginAsync(client, SupervisorUserName, SupervisorPassword);
        var cashier = await LoginAsync(client, CashierUserName, CashierPassword);

        SetBearer(client, administrator.AccessToken);
        await ReadSuccessAsync<CashRegisterDto>(await client.PostAsJsonAsync(
            "/api/cash/register", new ConfigureCashRegisterRequest("SHARED-CASH", "Caja compartida")), HttpStatusCode.Created);

        SetBearer(client, supervisor.AccessToken);
        var state = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        state = await ReadSuccessAsync<CashStateDto>(await client.PutAsJsonAsync(
            "/api/cash/shift-mode", new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        Assert.Equal(CashConstants.SharedMode, state.DefaultShiftMode);
        var shift = await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(50_000m, "Turno compartido")), HttpStatusCode.Created);
        Assert.Equal(CashConstants.SharedMode, shift.Mode);
        Assert.Null(shift.ResponsibleUserId);

        SetBearer(client, administrator.AccessToken);
        var supervisorCredential = await ReadSuccessAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{users.Supervisor.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        var cashierCredential = await ReadSuccessAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{users.Cashier.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        Assert.Equal("CODE128", supervisorCredential.Format);
        Assert.Equal("CODE128", cashierCredential.Format);
        Assert.False(string.IsNullOrWhiteSpace(cashierCredential.CredentialToken));
        await AssertCredentialIsHashedAndNotAuditedAsync(cashierCredential);

        SetBearer(client, supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest("invalid-test-token"))).StatusCode);
        var supervisorSession = await ReadSuccessAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(supervisorCredential.CredentialToken)), HttpStatusCode.Created);
        Assert.Equal(users.Supervisor.Id, supervisorSession.UserId);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            "/api/cash/movements/expenses",
            new CreateCashMovementRequest(1_000m, "Gasto supervisor", supervisorSession.Id))).StatusCode);

        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(cashierCredential.CredentialToken))).StatusCode);

        SetBearer(client, supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/api/cash/operator-sessions/{supervisorSession.Id}/close", null)).StatusCode);

        SetBearer(client, administrator.AccessToken);
        var replacementCashierCredential = await ReadSuccessAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{users.Cashier.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        await AssertCredentialIsHashedAndNotAuditedAsync(replacementCashierCredential);
        await AssertCredentialHistoryAsync(users.Cashier.Id, cashierCredential.CredentialId, expectedCount: 2);

        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(cashierCredential.CredentialToken))).StatusCode);
        var cashierSession = await ReadSuccessAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(replacementCashierCredential.CredentialToken)), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/cash/movements/incomes",
            new CreateCashMovementRequest(5_000m, "Ingreso sin concesión", cashierSession.Id))).StatusCode);

        SetBearer(client, administrator.AccessToken);
        var movementPermissionId = await GetPermissionIdAsync(client, PermissionCodes.CashMovementsManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.Id}/permissions/{movementPermissionId}", new SetUserPermissionRequest(true))).StatusCode);
        cashier = await LoginAsync(client, CashierUserName, CashierPassword);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            "/api/cash/movements/incomes",
            new CreateCashMovementRequest(5_000m, "Ingreso con concesión", cashierSession.Id))).StatusCode);

        SetBearer(client, administrator.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.Id}/permissions/{movementPermissionId}", new SetUserPermissionRequest(false))).StatusCode);
        cashier = await LoginAsync(client, CashierUserName, CashierPassword);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/cash/movements/incomes",
            new CreateCashMovementRequest(1_000m, "Ingreso revocado", cashierSession.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/api/cash/operator-sessions/{cashierSession.Id}/close", null)).StatusCode);
        SetBearer(client, administrator.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.Id}/permissions/{movementPermissionId}", new SetUserPermissionRequest(true))).StatusCode);
        cashier = await LoginAsync(client, CashierUserName, CashierPassword);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            "/api/cash/movements/incomes",
            new CreateCashMovementRequest(1_000m, "Sesión cerrada", cashierSession.Id))).StatusCode);

        SetBearer(client, supervisor.AccessToken);
        var reconciliation = await ReadSuccessAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(54_000m, reconciliation.TotalExpected);
        var exactCounts = reconciliation.PaymentMethods
            .Select(method => new CountedPaymentMethodRequest(method.PaymentMethodId, method.ExpectedAmount))
            .ToArray();
        var closed = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{shift.Id}/close",
            new CloseCashShiftRequest(reconciliation.ShiftVersion, exactCounts, null, null, null)), HttpStatusCode.OK);
        Assert.Equal(0m, closed.TotalDifference);

        await AssertSharedAuditAndPersistenceAsync(shift.Id, supervisorCredential.CredentialToken,
            cashierCredential.CredentialToken, replacementCashierCredential.CredentialToken);

        SetBearer(client, supervisor.AccessToken);
        var postCloseState = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        await ReadSuccessAsync<CashStateDto>(await client.PutAsJsonAsync(
            "/api/cash/shift-mode",
            new UpdateCashShiftModeRequest(CashConstants.IndividualMode, postCloseState.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        var administratorAuthorizedShift = await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(20_000m, "Cierre autorizado por Administrador")),
            HttpStatusCode.Created);
        var administratorReconciliation = await ReadSuccessAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        var administratorCounts = administratorReconciliation.PaymentMethods
            .Select(method => new CountedPaymentMethodRequest(
                method.PaymentMethodId,
                method.Code == "EFECTIVO" ? method.ExpectedAmount - 1_000m : method.ExpectedAmount))
            .ToArray();
        var administratorClosed = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{administratorAuthorizedShift.Id}/close",
            new CloseCashShiftRequest(administratorReconciliation.ShiftVersion, administratorCounts,
                catalog.DifferenceReasonId, "Diferencia autorizada en prueba",
                new CashCloseAuthorizationRequest(AdministratorUserName, AdministratorPassword))), HttpStatusCode.OK);
        Assert.Equal(-1_000m, administratorClosed.TotalDifference);
        await AssertAdministratorAuthorizationAsync(administratorAuthorizedShift.Id, administrator.User.Id);
    }

    private HttpClient CreateClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private async Task SetupAsync(HttpClient client)
    {
        var setup = new SetupFirstAdministratorRequest(
            "Comercio ficticio de Caja",
            "TEST-ONLY",
            "TEST-CASH-INSTALLATION",
            "CASHTEST01",
            "TEST-CASH-TERMINAL",
            "Terminal ficticia de Caja",
            AdministratorUserName,
            "Administrador ficticio de Caja",
            AdministratorPassword);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(setup)
        };
        request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
    }

    private async Task<ReconciliationCatalog> SeedReconciliationCatalogAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var establishmentId = await context.Establecimiento.Select(x => x.Id).SingleAsync();
        var now = _clock.UtcNow.UtcDateTime;
        var paymentMethods = await context.MetodoPago
            .Where(x => x.Codigo == "EFECTIVO" || x.Codigo == "TRANSFERENCIA" || x.Codigo == "NEQUI")
            .OrderBy(x => x.OrdenVisual)
            .ToArrayAsync();
        Assert.Equal(3, paymentMethods.Length);
        Assert.Single(paymentMethods, x => x.Codigo == "EFECTIVO" && x.AfectaEfectivo);
        Assert.Equal(2, paymentMethods.Count(x => x.Codigo != "EFECTIVO" && !x.AfectaEfectivo));
        var reason = new MotivoOperacion
        {
            Id = Guid.CreateVersion7(),
            EstablecimientoId = establishmentId,
            TipoOperacion = CashConstants.CashDifferenceReasonType,
            Codigo = "CONTEO_DIFERENTE",
            Nombre = "Diferencia ficticia de conteo",
            OrdenVisual = 1,
            Activo = true,
            FechaCreacion = now,
            Version = 1
        };
        context.MotivoOperacion.Add(reason);
        await context.SaveChangesAsync();
        return new ReconciliationCatalog(paymentMethods.Select(x => x.Id).ToArray(), reason.Id);
    }

    private async Task<OperationalUsers> CreateOperationalUsersAsync(HttpClient client)
    {
        var supervisorRoleId = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRoleId = await GetRoleIdAsync(client, RoleCodes.Cashier);
        var supervisor = await ReadSuccessAsync<UserDto>(await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(SupervisorUserName, "Supervisor ficticio de Caja", SupervisorPassword, [supervisorRoleId])),
            HttpStatusCode.Created);
        var cashier = await ReadSuccessAsync<UserDto>(await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(CashierUserName, "Cajero ficticio de Caja", CashierPassword, [cashierRoleId])),
            HttpStatusCode.Created);
        return new OperationalUsers(supervisor, cashier);
    }

    private async Task AssertFailedCloseWasAtomicAsync(Guid shiftId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(CashConstants.OpenState, await context.TurnoCaja
            .Where(x => x.Id == shiftId).Select(x => x.Estado).SingleAsync());
        Assert.Equal(0, await context.DetalleArqueoMedioPago.CountAsync(x => x.TurnoCajaId == shiftId));
        Assert.Equal(0, await context.AutorizacionOperacion.CountAsync(
            x => x.TipoOperacion == CashConstants.CashDifferenceAuthorizationType && x.UsuarioSolicitanteId != Guid.Empty));
        var rejected = await context.EventoAuditoria.AsNoTracking()
            .Where(x => x.EntidadId == shiftId && x.Accion == "CASHCLOSEAUTHORIZATIONREJECTED")
            .ToArrayAsync();
        Assert.NotEmpty(rejected);
        Assert.All(rejected, audit =>
        {
            Assert.Equal("FALLIDO", audit.Resultado);
            Assert.Null(audit.UsuarioAutorizadorId);
        });
    }

    private async Task AssertUserAuthenticationStateAsync(
        Guid userId,
        int expectedFailedAttempts,
        bool expectedLocked)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var user = await context.Usuario.AsNoTracking().SingleAsync(x => x.Id == userId);
        Assert.Equal(expectedFailedAttempts, user.IntentosFallidos);
        Assert.Equal(expectedLocked, user.BloqueoHasta is not null);
    }

    private async Task SetUserBlockedAsync(Guid userId, bool blocked)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        await context.Usuario.Where(x => x.Id == userId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.BloqueoHasta, blocked ? _clock.UtcNow.UtcDateTime : null));
    }

    private async Task AssertShiftHistoryCountAsync(Guid cashRegisterId, int expected)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var cashRegister = await context.Caja.AsNoTracking()
            .Include(x => x.TurnoCaja)
            .SingleAsync(x => x.Id == cashRegisterId);
        Assert.Equal(expected, cashRegister.TurnoCaja.Count);
    }

    private async Task AssertAdministratorAuthorizationAsync(Guid shiftId, Guid administratorId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var shift = await context.TurnoCaja.AsNoTracking().SingleAsync(x => x.Id == shiftId);
        var link = await context.AutorizacionCierreTurno.AsNoTracking().SingleAsync(x => x.TurnoCajaId == shiftId);
        var authorization = await context.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == link.AutorizacionId);
        Assert.Equal(administratorId, authorization.UsuarioAutorizadorId);
    }

    private async Task AssertClosedShiftPersistenceAsync(
        Guid shiftId,
        Guid executorId,
        Guid authorizerId,
        IReadOnlyCollection<Guid> paymentMethodIds,
        decimal expectedCash,
        decimal countedCash,
        decimal expectedDifference)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var shift = await context.TurnoCaja.AsNoTracking().SingleAsync(x => x.Id == shiftId);
        Assert.Equal(CashConstants.ClosedState, shift.Estado);
        Assert.Equal(expectedCash, shift.EfectivoEsperado);
        Assert.Equal(countedCash, shift.EfectivoContado);
        Assert.Equal(expectedDifference, shift.Diferencia);
        Assert.Equal(expectedDifference, shift.DiferenciaTotal);
        var link = await context.AutorizacionCierreTurno.AsNoTracking().SingleAsync(x => x.TurnoCajaId == shiftId);
        var authorization = await context.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == link.AutorizacionId);
        Assert.Equal(executorId, authorization.UsuarioSolicitanteId);
        Assert.Equal(authorizerId, authorization.UsuarioAutorizadorId);
        Assert.Equal(CashConstants.CashDifferenceAuthorizationType, authorization.TipoOperacion);
        Assert.Equal("UTILIZADA", authorization.Estado);

        var details = await context.DetalleArqueoMedioPago.AsNoTracking()
            .Where(x => x.TurnoCajaId == shiftId).ToArrayAsync();
        Assert.Equal(paymentMethodIds.Order(), details.Select(x => x.MetodoPagoId).Order());
        Assert.Equal(expectedDifference, details.Sum(x => x.Diferencia));
        Assert.True(await context.EventoAuditoria.AnyAsync(x =>
            x.EntidadId == shiftId && x.Accion == "CASHSHIFTCLOSED" &&
            x.UsuarioId == executorId && x.UsuarioAutorizadorId == authorizerId));

        var auditRows = await context.EventoAuditoria.AsNoTracking()
            .Select(x => new { x.DatosAnteriores, x.DatosNuevos, x.Motivo })
            .ToArrayAsync();
        var auditText = string.Join('|', auditRows.Select(x =>
            (x.DatosAnteriores ?? string.Empty) + (x.DatosNuevos ?? string.Empty) + (x.Motivo ?? string.Empty)));
        Assert.DoesNotContain(CashierPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(SupervisorPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(AdministratorPassword, auditText, StringComparison.Ordinal);

        var existingDetail = details[0];
        context.DetalleArqueoMedioPago.Add(new DetalleArqueoMedioPago
        {
            Id = Guid.CreateVersion7(),
            TurnoCajaId = shiftId,
            MetodoPagoId = existingDetail.MetodoPagoId,
            ValorEsperado = existingDetail.ValorEsperado,
            ValorContado = existingDetail.ValorContado,
            Diferencia = existingDetail.Diferencia
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private async Task AssertCredentialIsHashedAndNotAuditedAsync(IssuedOperatorCredentialDto issued)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var credential = await context.CredencialUsuario.AsNoTracking().SingleAsync(x => x.Id == issued.CredentialId);
        Assert.NotEqual(issued.CredentialToken, credential.TokenHash);
        Assert.Equal(issued.VisibleFragment, credential.FragmentoVisible);
        Assert.Equal("CODE128", credential.FormatoCodigo);
        var auditRows = await context.EventoAuditoria.AsNoTracking()
            .Select(x => new { x.DatosAnteriores, x.DatosNuevos, x.Motivo })
            .ToArrayAsync();
        var auditText = string.Join('|', auditRows.Select(x =>
            (x.DatosAnteriores ?? string.Empty) + (x.DatosNuevos ?? string.Empty) + (x.Motivo ?? string.Empty)));
        Assert.DoesNotContain(issued.CredentialToken, auditText, StringComparison.Ordinal);
    }

    private async Task AssertSharedAuditAndPersistenceAsync(Guid shiftId, params string[] rawTokens)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(CashConstants.ClosedState, await context.TurnoCaja
            .Where(x => x.Id == shiftId).Select(x => x.Estado).SingleAsync());
        Assert.False(await context.SesionOperador.AnyAsync(x => x.TurnoCajaId == shiftId && x.Estado == CashConstants.ActiveSessionState));
        Assert.True(await context.EventoAuditoria.AnyAsync(x => x.Accion == "OPERATORCREDENTIALISSUED"));
        Assert.True(await context.EventoAuditoria.AnyAsync(x => x.Accion == "OPERATORSESSIONSTARTED"));
        Assert.True(await context.EventoAuditoria.AnyAsync(x => x.Accion == "CASHMOVEMENTREGISTERED"));
        Assert.True(await context.EventoAuditoria.AnyAsync(x => x.Accion == "CASHRECONCILED"));
        Assert.True(await context.EventoAuditoria.AnyAsync(x => x.Accion == "CASHSHIFTCLOSED"));
        var auditRows = await context.EventoAuditoria.AsNoTracking()
            .Select(x => new { x.DatosAnteriores, x.DatosNuevos, x.Motivo })
            .ToArrayAsync();
        var auditText = string.Join('|', auditRows.Select(x =>
            (x.DatosAnteriores ?? string.Empty) + (x.DatosNuevos ?? string.Empty) + (x.Motivo ?? string.Empty)));
        foreach (var rawToken in rawTokens) Assert.DoesNotContain(rawToken, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(SupervisorPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(CashierPassword, auditText, StringComparison.Ordinal);
    }

    private async Task AssertCredentialHistoryAsync(Guid userId, Guid revokedCredentialId, int expectedCount)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var user = await context.Usuario.AsNoTracking()
            .Include(x => x.CredencialUsuarioUsuario)
            .SingleAsync(x => x.Id == userId);
        Assert.Equal(expectedCount, user.CredencialUsuarioUsuario.Count);
        Assert.Equal("REVOCADA", user.CredencialUsuarioUsuario.Single(x => x.Id == revokedCredentialId).Estado);
        Assert.Single(user.CredencialUsuarioUsuario, x => x.Estado == "ACTIVA");
    }

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string userName, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        return await ReadSuccessAsync<AuthenticationResult>(response, HttpStatusCode.OK);
    }

    private static async Task<Guid> GetRoleIdAsync(HttpClient client, string code)
    {
        var roles = await ReadSuccessAsync<PagedResult<RoleDto>>(
            await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        return roles.Items.Single(role => role.Code == code).Id;
    }

    private static async Task<Guid> GetPermissionIdAsync(HttpClient client, string code)
    {
        var permissions = await ReadSuccessAsync<PagedResult<PermissionDto>>(
            await client.GetAsync("/api/permissions?page=1&pageSize=100"), HttpStatusCode.OK);
        return permissions.Items.Single(permission => permission.Code == code).Id;
    }

    private static async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var value = await response.Content.ReadFromJsonAsync<T>();
        return Assert.IsType<T>(value);
    }

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemContract>();
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem.Code);
    }

    private static void SetBearer(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static string RandomSecret(int byteCount) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount));

    private sealed record ReconciliationCatalog(IReadOnlyCollection<Guid> PaymentMethodIds, Guid DifferenceReasonId);
    private sealed record OperationalUsers(UserDto Supervisor, UserDto Cashier);
    private sealed record ProblemContract(string? Code);

    private sealed class ControlledCashClock : IClock
    {
        // Capture once: credential validity is also checked against PostgreSQL statement_timestamp().
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;
        public void Set(DateTimeOffset instant) => UtcNow = instant;
        public void Advance(TimeSpan elapsed) => UtcNow += elapsed;
    }
}
