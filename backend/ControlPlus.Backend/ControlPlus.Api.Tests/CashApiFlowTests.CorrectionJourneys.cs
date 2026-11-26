using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task ManualMovementCorrection_ReplacesIncomeOnce_AndKeepsTheOriginal()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var original = await ReadSuccessAsync<CashMovementDto>(await client.PostAsJsonAsync(
            "/api/cash/movements/incomes", new CreateCashMovementRequest(40m, "Ingreso equivocado", null)),
            HttpStatusCode.Created);
        var state = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        var request = new CorrectCashMovementRequest(state.CurrentShift!.Version, "El valor real era 20",
            "INGRESO", 20m, "Ingreso corregido");
        var path = $"/api/cash/movements/{original.Id}/correction";

        using (var supervisor = AuthenticatedClient(scenario.Supervisor.AccessToken))
            Assert.Equal(HttpStatusCode.BadRequest, (await PostCorrectionAsync(supervisor, path,
                "manual-income-without-authorizer", request)).StatusCode);

        var corrected = await ReadSuccessAsync<CashMovementCorrectionDto>(await PostCorrectionAsync(
            client, path, "manual-income-correction", request), HttpStatusCode.OK);
        Assert.Equal(original.Id, corrected.OriginalMovementId);
        Assert.NotNull(corrected.ReplacementMovementId);
        Assert.Equal(120m, corrected.ExpectedCashAfter);

        var replay = await ReadSuccessAsync<CashMovementCorrectionDto>(await PostCorrectionAsync(
            client, path, "manual-income-correction", request), HttpStatusCode.OK);
        Assert.Equal(corrected, replay);
        Assert.Equal(HttpStatusCode.Conflict, (await PostCorrectionAsync(client, path,
            "manual-income-correction", request with { ReplacementAmount = 21m })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostCorrectionAsync(client, path,
            "manual-income-correction-stale", request)).StatusCode);

        var reconciliation = await ReconcileAsync(client);
        Assert.Equal(120m, reconciliation.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var movements = await db.MovimientoCaja.AsNoTracking()
            .Where(x => x.Id == original.Id || x.CorrelacionId == corrected.CorrelationId).ToArrayAsync();
        Assert.Equal(3, movements.Length);
        Assert.Equal(40m, Assert.Single(movements, x => x.Id == original.Id).Valor);
        var reversal = Assert.Single(movements, x => x.Id == corrected.ReversalMovementId);
        Assert.Equal(original.Id, reversal.MovimientoRevertidoId);
        var replacement = Assert.Single(movements, x => x.Id == corrected.ReplacementMovementId);
        Assert.Equal(20m, replacement.Valor);
        Assert.Equal("Ingreso corregido", replacement.Concepto);
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x =>
            x.Accion == "CASHMOVEMENTCORRECTED" && x.EntidadId == original.Id));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ClosureReview_AppendsOneReview_WithoutChangingTheClosedShift()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(await ReconcileAsync(client))),
            HttpStatusCode.OK);
        var closed = await ReadSuccessAsync<CashShiftDto[]>(await client.GetAsync("/api/cash/closed-shifts"),
            HttpStatusCode.OK);
        var shift = Assert.Single(closed, x => x.Id == scenario.Shift.Id);
        Assert.Equal("CERRADA", shift.State);
        Assert.Equal(100m, shift.CashCounted);
        var request = new ReviewCashClosureRequest(shift.Version, 90m, "El conteo físico se revisó");
        var path = $"/api/cash/shifts/{shift.Id}/reviews";

        using (var supervisor = AuthenticatedClient(scenario.Supervisor.AccessToken))
            Assert.Equal(HttpStatusCode.Forbidden, (await PostCorrectionAsync(supervisor, path,
                "supervisor-closed-shift-review", request)).StatusCode);

        var review = await ReadSuccessAsync<CashClosureReviewDto>(await PostCorrectionAsync(
            client, path, "closed-shift-review", request), HttpStatusCode.OK);
        Assert.Equal(100m, review.OriginalCount);
        Assert.Equal(90m, review.ReviewedCount);
        Assert.Equal(-10m, review.ReviewedDifference);
        Assert.Equal(review, await ReadSuccessAsync<CashClosureReviewDto>(await PostCorrectionAsync(
            client, path, "closed-shift-review", request), HttpStatusCode.OK));

        var history = await ReadSuccessAsync<CashClosureReviewDto[]>(await client.GetAsync(path), HttpStatusCode.OK);
        Assert.Equal(review, Assert.Single(history));
        var after = await ReadSuccessAsync<CashShiftDto[]>(await client.GetAsync("/api/cash/closed-shifts"),
            HttpStatusCode.OK);
        var unchanged = Assert.Single(after, x => x.Id == shift.Id);
        Assert.Equal(shift.Version, unchanged.Version);
        Assert.Equal(shift.CashCounted, unchanged.CashCounted);
        Assert.Equal(shift.CashDifference, unchanged.CashDifference);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.Set<RevisionCierreCaja>().CountAsync(x => x.TurnoCajaId == shift.Id));
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x =>
            x.Accion == "CASHCLOSUREREVIEWED" && x.EntidadId == shift.Id));
    }

    private static async Task<HttpResponseMessage> PostCorrectionAsync(HttpClient client, string path,
        string key, object body)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }
}
