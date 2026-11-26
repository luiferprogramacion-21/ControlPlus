using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task SharedShiftCorrection_RequiresTheActorsActiveSession_AndAdministratorAuthorization()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m, shared: true);
        using var administrator = AuthenticatedClient(scenario.Administrator.AccessToken);
        using var supervisor = AuthenticatedClient(scenario.Supervisor.AccessToken);
        using var cashier = AuthenticatedClient(scenario.Cashier.AccessToken);
        var supervisorCredential = await ReadSuccessAsync<IssuedOperatorCredentialDto>(
            await administrator.PostAsJsonAsync(
                $"/api/cash/operator-credentials/{scenario.Users.Supervisor.Id}",
                new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        var cashierCredential = await ReadSuccessAsync<IssuedOperatorCredentialDto>(
            await administrator.PostAsJsonAsync(
                $"/api/cash/operator-credentials/{scenario.Users.Cashier.Id}",
                new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);

        var originalSession = await ReadSuccessAsync<OperatorSessionDto>(
            await supervisor.PostAsJsonAsync("/api/cash/operator-sessions",
                new StartOperatorSessionRequest(supervisorCredential.CredentialToken)), HttpStatusCode.Created);
        var original = await ReadSuccessAsync<CashMovementDto>(
            await supervisor.PostAsJsonAsync("/api/cash/movements/incomes",
                new CreateCashMovementRequest(40m, "Ingreso de turno compartido", originalSession.Id,
                    AdministratorMovementAuthorization)), HttpStatusCode.Created);
        var before = await ReconcileAsync(supervisor);
        Assert.Equal(140m, before.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        await ReadSuccessAsync<OperatorSessionDto>(await supervisor.PostAsync(
            $"/api/cash/operator-sessions/{originalSession.Id}/close", null), HttpStatusCode.OK);

        var foreignSession = await ReadSuccessAsync<OperatorSessionDto>(
            await cashier.PostAsJsonAsync("/api/cash/operator-sessions",
                new StartOperatorSessionRequest(cashierCredential.CredentialToken)), HttpStatusCode.Created);
        var path = $"/api/cash/movements/{original.Id}/correction";
        var request = new CorrectCashMovementRequest(before.ShiftVersion, "El ingreso real era de 20",
            "INGRESO", 20m, "Ingreso corregido", null,
            new CashCloseAuthorizationRequest(AdministratorUserName, AdministratorPassword));
        Assert.Equal(HttpStatusCode.Conflict, (await PostCorrectionAsync(supervisor, path,
            "shared-correction-no-session", request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostCorrectionAsync(supervisor, path,
            "shared-correction-foreign-session", request with { OperatorSessionId = foreignSession.Id })).StatusCode);
        var afterRejected = await ReconcileAsync(supervisor);
        Assert.Equal(before.ShiftVersion, afterRejected.ShiftVersion);
        Assert.Equal(before.TotalExpected, afterRejected.TotalExpected);
        Assert.Equal(before.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount,
            afterRejected.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);

        await ReadSuccessAsync<OperatorSessionDto>(await cashier.PostAsync(
            $"/api/cash/operator-sessions/{foreignSession.Id}/close", null), HttpStatusCode.OK);
        var ownSession = await ReadSuccessAsync<OperatorSessionDto>(
            await supervisor.PostAsJsonAsync("/api/cash/operator-sessions",
                new StartOperatorSessionRequest(supervisorCredential.CredentialToken)), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostCorrectionAsync(supervisor, path,
            "shared-correction-without-authorizer", request with
            { OperatorSessionId = ownSession.Id, Authorization = null })).StatusCode);
        var corrected = await ReadSuccessAsync<CashMovementCorrectionDto>(await PostCorrectionAsync(
            supervisor, path, "shared-correction-own-session", request with { OperatorSessionId = ownSession.Id }),
            HttpStatusCode.OK);
        Assert.Equal(120m, corrected.ExpectedCashAfter);
        Assert.Equal(120m, (await ReconcileAsync(supervisor)).PaymentMethods
            .Single(x => x.Code == "EFECTIVO").ExpectedAmount);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var movements = await db.MovimientoCaja.AsNoTracking()
            .Where(x => x.TurnoCajaId == scenario.Shift.Id).ToArrayAsync();
        Assert.Equal(4, movements.Length); // Fondo inicial, ingreso, reverso y reemplazo.
        Assert.Equal(originalSession.Id, Assert.Single(movements, x => x.Id == original.Id).SesionOperadorId);
        Assert.Equal(ownSession.Id, Assert.Single(movements, x => x.Id == corrected.ReversalMovementId).SesionOperadorId);
        Assert.Equal(ownSession.Id, Assert.Single(movements, x => x.Id == corrected.ReplacementMovementId).SesionOperadorId);
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x =>
            x.Accion == "CASHMOVEMENTCORRECTED" && x.EntidadId == original.Id));
    }
}
