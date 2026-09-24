using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    private const decimal MaximumCashAmount = 999999999999999999m;
    private const string SecondaryAdministratorUserName = "cash.secondary.admin.test";
    private const string SecondaryAdministratorPassword = "Cash-Secondary-Administrator-Test-2026!";
    private static readonly IPAddress SupervisorClientAddress = IPAddress.Parse("198.51.100.10");
    private static readonly IPAddress AdministratorClientAddress = IPAddress.Parse("198.51.100.11");
    private static readonly string[] CashManagementPermissionCodes =
    [
        PermissionCodes.CashShiftManage,
        PermissionCodes.CashMovementsManage,
        PermissionCodes.CashShiftModeManage
    ];

    [Fact, Trait("Category", "Integration")]
    public async Task OperatingDate_UsesControlledBogotaMidnight_AndSameJwt()
    {
        _clock.Set(new DateTimeOffset(2026, 9, 16, 4, 30, 0, TimeSpan.Zero));
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        Assert.Equal(new DateOnly(2026, 9, 15), scenario.Shift.OperatingDate);
        var before = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        Assert.False(before.RequiresPreviousShiftClosure);
        _clock.Advance(TimeSpan.FromHours(1));
        var after = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        Assert.True(after.RequiresPreviousShiftClosure);
        Assert.NotNull(after.Guidance);
        Assert.Equal(scenario.Shift.Id, after.CurrentShift!.Id);
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(100, 50, 0, true)]
    [InlineData(100, -50, 0, true)]
    [InlineData(100, 0, 50, true)]
    [InlineData(100, 0, -50, true)]
    [InlineData(100, -50, 50, false)]
    public async Task DifferenceFields_PreserveCashAndElectronicSemantics(
        int initial, int cashDifference, int electronicDifference, bool requiresAuthorization)
    {
        var scenario = await PrepareCorrectionScenarioAsync(initial);
        await AddFourthPaymentMethodAsync();
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var reconciliation = await ReconcileAsync(client);
        Assert.Equal(4, reconciliation.PaymentMethods.Count);
        var counts = reconciliation.PaymentMethods.Select(method => new CountedPaymentMethodRequest(
            method.PaymentMethodId, method.ExpectedAmount + (method.Code switch
            {
                "EFECTIVO" => cashDifference,
                "TRANSFERENCIA" => electronicDifference,
                _ => 0m
            }))).ToArray();
        var request = new CloseCashShiftRequest(reconciliation.ShiftVersion, counts,
            requiresAuthorization ? scenario.Catalog.DifferenceReasonId : null, null,
            requiresAuthorization ? new CashCloseAuthorizationRequest(SupervisorUserName, SupervisorPassword) : null);
        var closed = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", request), HttpStatusCode.OK);
        Assert.Equal(cashDifference, closed.CashDifference);
        Assert.Equal(cashDifference + electronicDifference, closed.TotalDifference);
        Assert.Equal(closed.TotalDifference, closed.PaymentMethods.Sum(x => x.Difference));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var shift = await context.TurnoCaja.AsNoTracking().SingleAsync();
        Assert.Equal(cashDifference, shift.Diferencia);
        Assert.Equal(initial + cashDifference, shift.EfectivoContado);
        Assert.Equal(initial, shift.EfectivoEsperado);
        Assert.Equal(cashDifference + electronicDifference, shift.DiferenciaTotal);
        Assert.Equal(requiresAuthorization ? 1 : 0, await context.AutorizacionCierreTurno.CountAsync());
        var details = await context.DetalleArqueoMedioPago.AsNoTracking().ToArrayAsync();
        Assert.Equal(4, details.Length);
        Assert.Equal(shift.DiferenciaTotal, details.Sum(x => x.Diferencia));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MonetaryDifference_AcceptsPositiveAndNegativeNumeric18Limit(bool positive)
    {
        var scenario = await PrepareCorrectionScenarioAsync(positive ? 0m : MaximumCashAmount);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var reconciliation = await ReconcileAsync(client);
        var request = DifferenceClose(scenario, reconciliation, positive ? MaximumCashAmount : -MaximumCashAmount);
        var result = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", request), HttpStatusCode.OK);
        var difference = positive ? MaximumCashAmount : -MaximumCashAmount;
        Assert.Equal(difference, result.CashDifference);
        Assert.Equal(difference, result.TotalDifference);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var shift = await context.TurnoCaja.AsNoTracking().SingleAsync();
        Assert.Equal(difference, shift.Diferencia);
        Assert.Equal(difference, shift.DiferenciaTotal);
        Assert.Single(await context.AutorizacionCierreTurno.ToArrayAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task MonetaryOverflow_Returns400_WithoutMovementOrSuccessAudit()
    {
        var scenario = await PrepareCorrectionScenarioAsync(MaximumCashAmount);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var before = await CaptureFinancialStateAsync();
        foreach (var route in new[] { "incomes", "expenses", "cash-drops" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
                $"/api/cash/movements/{route}", new CreateCashMovementRequest(MaximumCashAmount + 1m,
                    "Límite excedido en prueba", null))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/cash/movements/incomes", new CreateCashMovementRequest(1m, "Acumulado excedido", null))).StatusCode);
        var reconciliation = await ReconcileAsync(client);
        var overflowCounts = reconciliation.PaymentMethods.Select(x => new CountedPaymentMethodRequest(
            x.PaymentMethodId, x.Code == "EFECTIVO" ? MaximumCashAmount + 1m : 0m)).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", new CloseCashShiftRequest(
                reconciliation.ShiftVersion, overflowCounts, null, null, null))).StatusCode);
        var accumulatedCounts = reconciliation.PaymentMethods.Select(x => new CountedPaymentMethodRequest(
            x.PaymentMethodId, x.Code == "EFECTIVO" ? MaximumCashAmount : x.Code == "NEQUI" ? 1m : 0m)).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", new CloseCashShiftRequest(
                reconciliation.ShiftVersion, accumulatedCounts, scenario.Catalog.DifferenceReasonId, null,
                new CashCloseAuthorizationRequest(SupervisorUserName, SupervisorPassword)))).StatusCode);
        Assert.Equal(before, await CaptureFinancialStateAsync());
        var exact = await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(reconciliation)), HttpStatusCode.OK);
        Assert.Equal(MaximumCashAmount, exact.TotalCounted);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(MaximumCashAmount + 1m, null))).StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await context.TurnoCaja.CountAsync());
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHSHIFTOPENED"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task OwnRead_RedactsAnotherIndividualShift_AndIgnoresClientIdentity()
    {
        var scenario = await PrepareCorrectionScenarioAsync(123456m);
        using var cashier = AuthenticatedClient(scenario.Cashier.AccessToken);
        foreach (var suffix in new[] { "", $"?userId={scenario.Administrator.User.Id}&shiftId={scenario.Shift.Id}" })
        {
            var response = await cashier.GetAsync("/api/cash/state" + suffix);
            var text = await response.Content.ReadAsStringAsync();
            var state = await ReadSuccessAsync<CashStateDto>(response, HttpStatusCode.OK);
            Assert.True(state.IsConfigured);
            Assert.True(state.HasOpenShift);
            Assert.Null(state.CurrentShift);
            Assert.Null(state.CashRegister);
            Assert.Null(state.DefaultShiftMode);
            Assert.Null(state.ConfigurationVersion);
            Assert.Null(state.OwnOperatorSession);
            Assert.DoesNotContain("123456", text, StringComparison.Ordinal);
            Assert.DoesNotContain(scenario.Administrator.User.Id.ToString(), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Observación privada", text, StringComparison.Ordinal);
        }
        using var manager = AuthenticatedClient(scenario.Supervisor.AccessToken);
        var full = await ReadSuccessAsync<CashStateDto>(await manager.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        Assert.Equal(scenario.Shift.Id, full.CurrentShift!.Id);
        Assert.Equal(123456m, full.CurrentShift.InitialAmount);
        Assert.Equal(scenario.Administrator.User.Id, full.CurrentShift.ResponsibleUserId);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task OwnRead_IndividualOwnerKeepsOwnDetailsAfterManagementPermissionRevoked()
    {
        var scenario = await PrepareCorrectionScenarioAsync(0m);
        using var administrator = AuthenticatedClient(scenario.Administrator.AccessToken);
        await ReadSuccessAsync<CashReconciliationDto>(await administrator.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(await ReconcileAsync(administrator))), HttpStatusCode.OK);
        var permission = await GetPermissionIdAsync(administrator, PermissionCodes.CashShiftManage);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.PutAsJsonAsync(
            $"/api/users/{scenario.Users.Cashier.Id}/permissions/{permission}", new SetUserPermissionRequest(true))).StatusCode);
        using var cashier = CreateClient();
        var login = await LoginAsync(cashier, CashierUserName, CashierPassword);
        SetBearer(cashier, login.AccessToken);
        var owned = await ReadSuccessAsync<CashShiftDto>(await cashier.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(456m, "Propio")), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync(
            $"/api/users/{scenario.Users.Cashier.Id}/permissions")).StatusCode);
        login = await LoginAsync(cashier, CashierUserName, CashierPassword);
        SetBearer(cashier, login.AccessToken);
        var state = await ReadSuccessAsync<CashStateDto>(await cashier.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        Assert.Equal(owned.Id, state.CurrentShift!.Id);
        Assert.Equal(456m, state.CurrentShift.InitialAmount);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/cash/reconciliation")).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task OwnRead_RevokedSupervisorAndAdministratorUseEffectivePermissionsAndRestoreRoleTemplate()
    {
        var scenario = await PrepareCorrectionScenarioAsync(654321m);
        using var manager = AuthenticatedClient(scenario.Administrator.AccessToken);
        var administratorRoleId = await GetRoleIdAsync(manager, RoleCodes.Administrator);
        var secondaryAdministrator = await ReadSuccessAsync<UserDto>(await manager.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(SecondaryAdministratorUserName,
                "Administrador secundario ficticio de Caja", SecondaryAdministratorPassword,
                [administratorRoleId])), HttpStatusCode.Created);
        var permissionIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var code in CashManagementPermissionCodes)
            permissionIds.Add(code, await GetPermissionIdAsync(manager, code));

        await AssertCashManagementOverrideCycleAsync(manager, scenario.Users.Supervisor,
            SupervisorUserName, SupervisorPassword, scenario, permissionIds, SupervisorClientAddress);
        await AssertCashManagementOverrideCycleAsync(manager, secondaryAdministrator,
            SecondaryAdministratorUserName, SecondaryAdministratorPassword, scenario, permissionIds,
            AdministratorClientAddress);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SharedOwnRead_ExposesOnlyOwnSession_AndClosedSessionFailsWithSameOwnerJwtAndPermission()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m, shared: true);
        var sessions = await PrepareTwoSessionsAsync(scenario);
        using (var ownReader = AuthenticatedClient(scenario.Cashier.AccessToken))
        {
            var state = await ReadSuccessAsync<CashStateDto>(await ownReader.GetAsync(
                $"/api/cash/state?userId={scenario.Administrator.User.Id}&operatorSessionId={sessions.OtherId}"), HttpStatusCode.OK);
            Assert.Null(state.CurrentShift);
            Assert.Equal(sessions.OwnId, state.OwnOperatorSession!.Id);
            Assert.Equal(scenario.Users.Cashier.Id, state.OwnOperatorSession.UserId);
            Assert.False(state.RequiresOperatorSession);
        }
        using var administrator = AuthenticatedClient(scenario.Administrator.AccessToken);
        var permission = await GetPermissionIdAsync(administrator, PermissionCodes.CashMovementsManage);
        Assert.Equal(HttpStatusCode.NoContent, (await administrator.PutAsJsonAsync(
            $"/api/users/{scenario.Users.Cashier.Id}/permissions/{permission}", new SetUserPermissionRequest(true))).StatusCode);
        using var cashier = CreateClient();
        var login = await LoginAsync(cashier, CashierUserName, CashierPassword);
        SetBearer(cashier, login.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await cashier.PostAsJsonAsync("/api/cash/movements/incomes",
            new CreateCashMovementRequest(1m, "Sesión propia activa", sessions.OwnId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync("/api/cash/movements/incomes",
            new CreateCashMovementRequest(1m, "Sesión ajena manipulada", sessions.OtherId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsync($"/api/cash/operator-sessions/{sessions.OwnId}/close", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cashier.PostAsJsonAsync("/api/cash/movements/incomes",
            new CreateCashMovementRequest(1m, "Mismo JWT y permiso; sesión cerrada", sessions.OwnId))).StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await context.MovimientoCaja.CountAsync(x => x.SesionOperadorId == sessions.OwnId));
        Assert.Equal(0, await context.MovimientoCaja.CountAsync(x => x.SesionOperadorId == sessions.OtherId));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentWrongAuthorizerAttempts_ArePreserved_AndCoordinateWithLogin(bool includeLogin)
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var request = DifferenceClose(scenario, await ReconcileAsync(client), -1m) with
        {
            Authorization = new CashCloseAuthorizationRequest(SupervisorUserName, "Incorrect-Test-Password-2026!")
        };
        var executorBefore = await ReadAuthenticationStateAsync(scenario.Administrator.User.Id);
        var attempts = Enumerable.Range(0, 5).Select(index => includeLogin && index < 2
            ? client.PostAsJsonAsync("/api/auth/login", new LoginRequest(SupervisorUserName, "Incorrect-Test-Password-2026!"))
            : client.PostAsJsonAsync($"/api/cash/shifts/{scenario.Shift.Id}/close", request));
        var results = await Task.WhenAll(attempts);
        Assert.All(results, response => Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized));
        await AssertUserAuthenticationStateAsync(scenario.Users.Supervisor.Id, 5, true);
        Assert.Equal(executorBefore, await ReadAuthenticationStateAsync(scenario.Administrator.User.Id));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(x => x.Accion == "USERLOCKED" && x.EntidadId == scenario.Users.Supervisor.Id));
        Assert.Equal(includeLogin ? 3 : 5, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHCLOSEAUTHORIZATIONREJECTED"));
        Assert.Equal(CashConstants.OpenState, await context.TurnoCaja.Select(x => x.Estado).SingleAsync());
        Assert.Equal(0, await context.DetalleArqueoMedioPago.CountAsync());
        Assert.Equal(0, await context.AutorizacionCierreTurno.CountAsync());
        Assert.Equal(0, await context.AutorizacionOperacion.CountAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CorrectCloseConcurrentWithWrongLogin_PreservesBothOutcomesWithoutDuplicateFinance()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var request = DifferenceClose(scenario, await ReconcileAsync(client), -1m);
        var before = await ReadAuthenticationStateAsync(scenario.Administrator.User.Id);
        var results = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/cash/shifts/{scenario.Shift.Id}/close", request),
            client.PostAsJsonAsync("/api/auth/login", new LoginRequest(SupervisorUserName, "Incorrect-Test-Password-2026!")));
        Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, results[1].StatusCode);
        Assert.Equal(before, await ReadAuthenticationStateAsync(scenario.Administrator.User.Id));
        var authorizer = await ReadAuthenticationStateAsync(scenario.Users.Supervisor.Id);
        Assert.InRange(authorizer.Attempts, 0, 1); // Correct attempt either precedes or resets the failed attempt.
        Assert.Null(authorizer.LockedUntil);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(CashConstants.ClosedState, await context.TurnoCaja.Select(x => x.Estado).SingleAsync());
        Assert.Equal(1, await context.AutorizacionCierreTurno.CountAsync());
        Assert.Equal(1, await context.AutorizacionOperacion.CountAsync());
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHSHIFTCLOSED"));
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(x => x.Accion == "LOGINFAILED" && x.EntidadId == scenario.Users.Supervisor.Id));
        Assert.Equal(0, await context.EventoAuditoria.CountAsync(x => x.Accion == "USERLOCKED"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConcurrentCloses_CommitOneCompleteAuthorizationAndReconciliation()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var close = DifferenceClose(scenario, await ReconcileAsync(client), -1m);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            client.PostAsJsonAsync($"/api/cash/shifts/{scenario.Shift.Id}/close", close)));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(CashConstants.ClosedState, await context.TurnoCaja.Select(x => x.Estado).SingleAsync());
        Assert.Equal(3, await context.DetalleArqueoMedioPago.CountAsync());
        Assert.Equal(1, await context.AutorizacionCierreTurno.CountAsync());
        Assert.Equal(1, await context.AutorizacionOperacion.CountAsync());
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHSHIFTCLOSED"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConcurrentExpenses_CannotSpendSameFundsTwice()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(
            "/api/cash/movements/expenses", new CreateCashMovementRequest(75m, "Egreso concurrente", null))));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var reconciliation = await ReconcileAsync(client);
        Assert.Equal(25m, reconciliation.TotalExpected);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(2, await context.MovimientoCaja.CountAsync());
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHMOVEMENTREGISTERED"));
        Assert.Equal(CashConstants.OpenState, await context.TurnoCaja.Select(x => x.Estado).SingleAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConcurrentCloseAndMovement_FinalDatabaseMatchesOneSerialOrder()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var close = BalancedClose(await ReconcileAsync(client));
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/cash/shifts/{scenario.Shift.Id}/close", close),
            client.PostAsJsonAsync("/api/cash/movements/incomes", new CreateCashMovementRequest(10m, "Carrera con cierre", null)));
        Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        Assert.Contains(responses[1].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.NotFound });
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var shift = await context.TurnoCaja.AsNoTracking().SingleAsync();
        var committedIncome = responses[1].StatusCode == HttpStatusCode.Created;
        Assert.Equal(committedIncome ? 2 : 1, await context.MovimientoCaja.CountAsync());
        Assert.Equal(committedIncome ? 1 : 0, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHMOVEMENTREGISTERED"));
        if (responses[0].StatusCode == HttpStatusCode.OK)
        {
            Assert.False(committedIncome);
            Assert.Equal(CashConstants.ClosedState, shift.Estado);
            Assert.Equal(100m, shift.EfectivoEsperado);
            Assert.Equal(3, await context.DetalleArqueoMedioPago.CountAsync());
            Assert.Equal(1, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHSHIFTCLOSED"));
        }
        else
        {
            Assert.True(committedIncome);
            Assert.Equal(CashConstants.OpenState, shift.Estado);
            Assert.Equal(0, await context.DetalleArqueoMedioPago.CountAsync());
            Assert.Equal(0, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHSHIFTCLOSED"));
        }
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticActiveSessionClosure_WithSessionHistory_IsAuditedAtomically_WithDeferredCommitFailure(bool failCommit)
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m, shared: true);
        var sessions = await PrepareTwoSessionsAsync(scenario);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var close = DifferenceClose(scenario, await ReconcileAsync(client), -1m);
        var authorizerBefore = await ReadAuthenticationStateAsync(scenario.Users.Supervisor.Id);
        if (failCommit) await InstallDeferredCloseFailureAsync();
        var response = await client.PostAsJsonAsync($"/api/cash/shifts/{scenario.Shift.Id}/close", close);
        Assert.Equal(failCommit ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var shift = await context.TurnoCaja.AsNoTracking().SingleAsync();
        var persistedSessions = await context.SesionOperador.AsNoTracking().ToArrayAsync();
        var audits = await context.EventoAuditoria.AsNoTracking()
            .Where(x => x.Accion == "OPERATORSESSIONCLOSED").ToArrayAsync();
        Assert.Equal(2, persistedSessions.Length);
        var ownSession = Assert.Single(persistedSessions, x => x.Id == sessions.OwnId);
        var historicalSession = Assert.Single(persistedSessions, x => x.Id == sessions.OtherId);
        Assert.Equal(CashConstants.ClosedState, historicalSession.Estado);
        Assert.NotNull(historicalSession.FechaFin);
        if (failCommit)
        {
            Assert.Equal(CashConstants.OpenState, shift.Estado);
            Assert.Null(shift.FechaHoraCierre);
            Assert.Null(shift.DiferenciaTotal);
            Assert.Equal(CashConstants.ActiveSessionState, ownSession.Estado);
            Assert.Null(ownSession.FechaFin);
            Assert.Empty(audits);
            Assert.Equal(0, await context.DetalleArqueoMedioPago.CountAsync());
            Assert.Equal(0, await context.AutorizacionCierreTurno.CountAsync());
            Assert.Equal(0, await context.AutorizacionOperacion.CountAsync());
            Assert.Equal(0, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHSHIFTCLOSED"));
            Assert.Equal(authorizerBefore, await ReadAuthenticationStateAsync(scenario.Users.Supervisor.Id));
            await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT is_called FROM caja.test_commit_observed", connection);
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
        }
        else
        {
            Assert.Equal(CashConstants.ClosedState, shift.Estado);
            Assert.Equal(CashConstants.ClosedState, ownSession.Estado);
            Assert.NotNull(ownSession.FechaFin);
            var audit = Assert.Single(audits);
            var closeAudit = await context.EventoAuditoria.SingleAsync(x => x.Accion == "CASHSHIFTCLOSED");
            Assert.Equal(sessions.OwnId, audit.EntidadId);
            Assert.Equal(scenario.Administrator.User.Id, audit.UsuarioId);
            Assert.Equal(closeAudit.CorrelacionId, audit.CorrelacionId);
            Assert.Contains(ownSession.UsuarioId.ToString(), audit.DatosNuevos!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(scenario.Shift.Id.ToString(), audit.DatosNuevos!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("true", audit.DatosNuevos!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializationAbortAtCommit_RetriesWithFreshState_AndIsBounded(bool alwaysFail)
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var close = DifferenceClose(scenario, await ReconcileAsync(client), -1m);
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var failCondition = alwaysFail ? "true" : "attempt < 3";
        await using (var install = new NpgsqlCommand($$"""
            CREATE SEQUENCE caja.test_serialization_attempt;
            CREATE FUNCTION caja.test_serialization_abort() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE attempt bigint;
            BEGIN
              IF NEW.estado = 'CERRADA' THEN
                attempt := nextval('caja.test_serialization_attempt');
                IF {{failCondition}} THEN
                  RAISE EXCEPTION 'Isolated serialization abort' USING ERRCODE = '40001';
                END IF;
              END IF;
              RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_serialization_abort
              AFTER UPDATE ON caja.turno_caja DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION caja.test_serialization_abort();
            """, connection))
            await install.ExecuteNonQueryAsync();
        // Setup and login share one controlled instant. Make the successful
        // reauthorization observable as a real user update on every retry.
        _clock.Advance(TimeSpan.FromSeconds(1));
        var before = await ReadAuthenticationStateAsync(scenario.Users.Supervisor.Id);
        var response = await client.PostAsJsonAsync($"/api/cash/shifts/{scenario.Shift.Id}/close", close);
        Assert.Equal(alwaysFail ? HttpStatusCode.Conflict : HttpStatusCode.OK, response.StatusCode);
        await using var attempts = new NpgsqlCommand("SELECT last_value FROM caja.test_serialization_attempt", connection);
        Assert.Equal(3L, await attempts.ExecuteScalarAsync());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(alwaysFail ? "ABIERTA" : "CERRADA", await context.TurnoCaja.Select(x => x.Estado).SingleAsync());
        Assert.Equal(alwaysFail ? 0 : 1, await context.AutorizacionOperacion.CountAsync());
        Assert.Equal(alwaysFail ? 0 : 1, await context.AutorizacionCierreTurno.CountAsync());
        Assert.Equal(alwaysFail ? 0 : 3, await context.DetalleArqueoMedioPago.CountAsync());
        Assert.Equal(alwaysFail ? 0 : 1, await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHSHIFTCLOSED"));
        Assert.Equal(1, await context.MovimientoCaja.CountAsync());
        var after = await ReadAuthenticationStateAsync(scenario.Users.Supervisor.Id);
        Assert.Equal(alwaysFail ? before.Version : before.Version + 1, after.Version);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CloseRejectsSelfAuthorization_AndAnEarlierAuthorizationIdInRequest()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var request = DifferenceClose(scenario, await ReconcileAsync(client), -1m);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", request with
            { Authorization = new CashCloseAuthorizationRequest(AdministratorUserName, AdministratorPassword) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", new
            {
                request.Version, request.PaymentMethods, request.DifferenceReasonId,
                authorization = new { userName = SupervisorUserName, password = SupervisorPassword, authorizationId = Guid.CreateVersion7() }
            })).StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal("ABIERTA", await context.TurnoCaja.Select(x => x.Estado).SingleAsync());
        Assert.Empty(await context.AutorizacionOperacion.ToArrayAsync());
        Assert.Empty(await context.AutorizacionCierreTurno.ToArrayAsync());
        Assert.Empty(await context.DetalleArqueoMedioPago.ToArrayAsync());
    }

    private async Task<CorrectionScenario> PrepareCorrectionScenarioAsync(decimal initial, bool shared = false)
    {
        using var client = CreateClient();
        await SetupAsync(client);
        var administrator = await LoginAsync(client, AdministratorUserName, AdministratorPassword);
        SetBearer(client, administrator.AccessToken);
        var catalog = await SeedReconciliationCatalogAsync();
        var users = await CreateOperationalUsersAsync(client);
        await ReadSuccessAsync<CashRegisterDto>(await client.PostAsJsonAsync("/api/cash/register",
            new ConfigureCashRegisterRequest("CORRECTIONS", "Caja aislada de correcciones")), HttpStatusCode.Created);
        if (shared)
        {
            var state = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
            await ReadSuccessAsync<CashStateDto>(await client.PutAsJsonAsync("/api/cash/shift-mode",
                new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        }
        var shift = await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(initial, "Observación privada del responsable")), HttpStatusCode.Created);
        var supervisor = await LoginAsync(client, SupervisorUserName, SupervisorPassword);
        var cashier = await LoginAsync(client, CashierUserName, CashierPassword);
        return new CorrectionScenario(administrator, supervisor, cashier, users, catalog, shift);
    }

    private HttpClient AuthenticatedClient(string token)
    {
        var client = CreateClient();
        SetBearer(client, token);
        return client;
    }

    private HttpClient CreateClient(IPAddress remoteIp)
    {
        ArgumentNullException.ThrowIfNull(remoteIp);
        return new HttpClient(_factory!.Server.CreateHandler(context => context.Connection.RemoteIpAddress = remoteIp))
        {
            BaseAddress = _factory.Server.BaseAddress
        };
    }

    private async Task AssertCashManagementOverrideCycleAsync(
        HttpClient manager,
        UserDto target,
        string userName,
        string password,
        CorrectionScenario scenario,
        IReadOnlyDictionary<string, Guid> permissionIds,
        IPAddress remoteIp)
    {
        using var targetClient = CreateClient(remoteIp);
        var originalSession = await LoginAsync(targetClient, userName, password);
        SetBearer(targetClient, originalSession.AccessToken);
        await AssertFullForeignShiftStateAsync(targetClient, scenario);

        foreach (var code in CashManagementPermissionCodes)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(
                $"/api/users/{target.Id}/permissions/{permissionIds[code]}",
                new SetUserPermissionRequest(false))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/me")).StatusCode);

        var revokedSession = await LoginAsync(targetClient, userName, password);
        SetBearer(targetClient, revokedSession.AccessToken);
        await AssertMinimalForeignShiftStateAsync(targetClient, scenario);
        await AssertEffectiveCashManagementPermissionsAsync(manager, target.Id, granted: false, "REVOCAR");

        var individuallyGrantedCode = PermissionCodes.CashMovementsManage;
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(
            $"/api/users/{target.Id}/permissions/{permissionIds[individuallyGrantedCode]}",
            new SetUserPermissionRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/me")).StatusCode);

        var grantedSession = await LoginAsync(targetClient, userName, password);
        SetBearer(targetClient, grantedSession.AccessToken);
        await AssertFullForeignShiftStateAsync(targetClient, scenario);
        var grantedPermissions = await ReadSuccessAsync<EffectiveUserPermissionDto[]>(await manager.GetAsync(
            $"/api/users/{target.Id}/permissions/effective"), HttpStatusCode.OK);
        var individualGrant = Assert.Single(grantedPermissions, x => x.Code == individuallyGrantedCode);
        Assert.True(individualGrant.Granted);
        Assert.Equal("INDIVIDUAL", individualGrant.Source);
        Assert.Equal("CONCEDER", individualGrant.IndividualEffect);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(
            $"/api/users/{target.Id}/permissions/{permissionIds[individuallyGrantedCode]}",
            new SetUserPermissionRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/me")).StatusCode);

        var reRevokedSession = await LoginAsync(targetClient, userName, password);
        SetBearer(targetClient, reRevokedSession.AccessToken);
        await AssertMinimalForeignShiftStateAsync(targetClient, scenario);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(
            $"/api/users/{target.Id}/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/me")).StatusCode);

        var restoredSession = await LoginAsync(targetClient, userName, password);
        SetBearer(targetClient, restoredSession.AccessToken);
        await AssertFullForeignShiftStateAsync(targetClient, scenario);
        await AssertEffectiveCashManagementPermissionsAsync(manager, target.Id, granted: true, effect: null);
    }

    private static async Task AssertMinimalForeignShiftStateAsync(HttpClient client, CorrectionScenario scenario)
    {
        var response = await client.GetAsync("/api/cash/state");
        var payload = await response.Content.ReadAsStringAsync();
        var state = await ReadSuccessAsync<CashStateDto>(response, HttpStatusCode.OK);
        Assert.True(state.IsConfigured);
        Assert.True(state.HasOpenShift);
        Assert.Null(state.CashRegister);
        Assert.Null(state.CurrentShift);
        Assert.Null(state.DefaultShiftMode);
        Assert.Null(state.ConfigurationVersion);
        Assert.Null(state.Guidance);
        Assert.Null(state.OwnOperatorSession);
        Assert.DoesNotContain(scenario.Shift.InitialAmount.ToString("0"), payload, StringComparison.Ordinal);
        Assert.DoesNotContain(scenario.Administrator.User.Id.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Observación privada del responsable", payload, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/cash/reconciliation")).StatusCode);
    }

    private static async Task AssertFullForeignShiftStateAsync(HttpClient client, CorrectionScenario scenario)
    {
        var state = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        Assert.NotNull(state.CashRegister);
        Assert.NotNull(state.DefaultShiftMode);
        Assert.NotNull(state.ConfigurationVersion);
        Assert.Equal(scenario.Shift.Id, state.CurrentShift!.Id);
        Assert.Equal(scenario.Administrator.User.Id, state.CurrentShift.ResponsibleUserId);
        Assert.Equal(scenario.Shift.InitialAmount, state.CurrentShift.InitialAmount);
        Assert.Equal(scenario.Shift.Observations, state.CurrentShift.Observations);
    }

    private static async Task AssertEffectiveCashManagementPermissionsAsync(
        HttpClient manager,
        Guid userId,
        bool granted,
        string? effect)
    {
        var permissions = await ReadSuccessAsync<EffectiveUserPermissionDto[]>(await manager.GetAsync(
            $"/api/users/{userId}/permissions/effective"), HttpStatusCode.OK);
        foreach (var code in CashManagementPermissionCodes)
        {
            var permission = Assert.Single(permissions, x => x.Code == code);
            Assert.Equal(granted, permission.Granted);
            Assert.Equal(effect is null ? "ROL" : "INDIVIDUAL", permission.Source);
            Assert.Equal(effect, permission.IndividualEffect);
        }
    }

    private static Task<CashReconciliationDto> ReconcileAsync(HttpClient client) =>
        ReadReconciliationAsync(client);

    private static async Task<CashReconciliationDto> ReadReconciliationAsync(HttpClient client) =>
        await ReadSuccessAsync<CashReconciliationDto>(await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);

    private static CloseCashShiftRequest BalancedClose(CashReconciliationDto reconciliation) =>
        new(reconciliation.ShiftVersion, reconciliation.PaymentMethods.Select(x =>
            new CountedPaymentMethodRequest(x.PaymentMethodId, x.ExpectedAmount)).ToArray(), null, null, null);

    private static CloseCashShiftRequest DifferenceClose(CorrectionScenario scenario, CashReconciliationDto reconciliation, decimal cashDifference) =>
        new(reconciliation.ShiftVersion, reconciliation.PaymentMethods.Select(x =>
            new CountedPaymentMethodRequest(x.PaymentMethodId, x.ExpectedAmount + (x.AffectsCash ? cashDifference : 0m))).ToArray(),
            scenario.Catalog.DifferenceReasonId, "Diferencia aislada de prueba",
            new CashCloseAuthorizationRequest(SupervisorUserName, SupervisorPassword));

    private async Task AddFourthPaymentMethodAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        context.MetodoPago.Add(new MetodoPago
        {
            Id = Guid.CreateVersion7(), Codigo = "TARJETA_TEST", Nombre = "Cuarto medio aislado",
            AfectaEfectivo = false, Activo = true, OrdenVisual = 4, FechaCreacion = _clock.UtcNow.UtcDateTime, Version = 1
        });
        await context.SaveChangesAsync();
    }

    private async Task<(Guid OwnId, Guid OtherId)> PrepareTwoSessionsAsync(CorrectionScenario scenario)
    {
        using var administrator = AuthenticatedClient(scenario.Administrator.AccessToken);
        var ownCredential = await ReadSuccessAsync<IssuedOperatorCredentialDto>(await administrator.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{scenario.Users.Cashier.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        var otherCredential = await ReadSuccessAsync<IssuedOperatorCredentialDto>(await administrator.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{scenario.Administrator.User.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        using var cashier = AuthenticatedClient(scenario.Cashier.AccessToken);
        var own = await ReadSuccessAsync<OperatorSessionDto>(await cashier.PostAsJsonAsync("/api/cash/operator-sessions",
            new StartOperatorSessionRequest(ownCredential.CredentialToken)), HttpStatusCode.Created);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var terminal = new Terminal
        {
            Id = Guid.CreateVersion7(), InstalacionId = await context.Instalacion.Select(x => x.Id).SingleAsync(),
            Codigo = "SECOND-TEST", Nombre = "Segunda terminal aislada", Activo = true,
            FechaCreacion = _clock.UtcNow.UtcDateTime, Version = 1
        };
        context.Terminal.Add(terminal);
        var other = new SesionOperador
        {
            Id = Guid.CreateVersion7(), TurnoCajaId = scenario.Shift.Id, TerminalId = terminal.Id,
            UsuarioId = scenario.Administrator.User.Id, CredencialUsuarioId = otherCredential.CredentialId,
            FechaInicio = _clock.UtcNow.UtcDateTime, FechaUltimoUso = _clock.UtcNow.UtcDateTime,
            FechaFin = _clock.UtcNow.UtcDateTime, Estado = CashConstants.ClosedState,
            MotivoCierre = "MANUAL", CorrelacionId = Guid.CreateVersion7()
        };
        context.SesionOperador.Add(other);
        await context.SaveChangesAsync();
        return (own.Id, other.Id);
    }

    private async Task<(int Attempts, DateTime? LockedUntil, long Version)> ReadAuthenticationStateAsync(Guid userId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var user = await context.Usuario.AsNoTracking().SingleAsync(x => x.Id == userId);
        return (user.IntentosFallidos, user.BloqueoHasta, user.Version);
    }

    private async Task<(int Movements, int SuccessAudits, int Details, int Links)> CaptureFinancialStateAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        return (await context.MovimientoCaja.CountAsync(),
            await context.EventoAuditoria.CountAsync(x => x.Accion == "CASHMOVEMENTREGISTERED" || x.Accion == "CASHSHIFTCLOSED"),
            await context.DetalleArqueoMedioPago.CountAsync(), await context.AutorizacionCierreTurno.CountAsync());
    }

    private async Task InstallDeferredCloseFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE caja.test_commit_observed;
            CREATE FUNCTION caja.test_fail_close_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.estado = 'CERRADA' THEN
                IF (SELECT count(*) FROM caja.detalle_arqueo_medio_pago WHERE turno_caja_id = NEW.id) = 3
                  AND EXISTS (SELECT 1 FROM caja.autorizacion_cierre_turno WHERE turno_caja_id = NEW.id)
                  AND (SELECT count(*) FROM caja.sesion_operador WHERE turno_caja_id = NEW.id AND estado = 'CERRADA') = 2
                  AND (SELECT count(*) FROM auditoria.evento_auditoria WHERE accion = 'OPERATORSESSIONCLOSED') = 1
                  AND EXISTS (SELECT 1 FROM auditoria.evento_auditoria WHERE accion = 'CASHSHIFTCLOSED' AND entidad_id = NEW.id)
                THEN
                  PERFORM nextval('caja.test_commit_observed');
                END IF;
                RAISE EXCEPTION 'Forced isolated deferred failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_close_commit
              AFTER UPDATE ON caja.turno_caja DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION caja.test_fail_close_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record CorrectionScenario(AuthenticationResult Administrator, AuthenticationResult Supervisor,
        AuthenticationResult Cashier, OperationalUsers Users, ReconciliationCatalog Catalog, CashShiftDto Shift);
}
