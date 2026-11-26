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
    [Theory, Trait("Category", "Integration")]
    [InlineData("expenses", "GASTO")]
    [InlineData("cash-drops", "SANGRIA")]
    public async Task ManualOutflowCorrection_RejectsInsufficientCash_ThenCancelsWithoutReplacement(
        string route, string category)
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var original = await ReadSuccessAsync<CashMovementDto>(await client.PostAsJsonAsync(
            $"/api/cash/movements/{route}", new CreateCashMovementRequest(30m, "Salida incorrecta", null)),
            HttpStatusCode.Created);
        Assert.Equal("EGRESO", original.Type);
        Assert.Equal(category, original.Category);
        Assert.Equal(70m, (await ReconcileAsync(client)).PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);

        var state = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        var version = state.CurrentShift!.Version;
        var path = $"/api/cash/movements/{original.Id}/correction";
        var key = $"cancel-{route}-after-rejection";
        var excessive = new CorrectCashMovementRequest(version, "La salida correcta excede el efectivo",
            "GASTO", 150m, "Salida excesiva");
        Assert.Equal(HttpStatusCode.Conflict, (await PostCorrectionAsync(client, path, key, excessive)).StatusCode);

        var afterRejection = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"),
            HttpStatusCode.OK);
        Assert.Equal(version, afterRejection.CurrentShift!.Version);
        Assert.Equal(70m, (await ReconcileAsync(client)).PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.Equal(2, await db.MovimientoCaja.CountAsync(x => x.TurnoCajaId == scenario.Shift.Id));
            Assert.Equal(0, await db.EventoAuditoria.CountAsync(x =>
                x.Accion == "CASHMOVEMENTCORRECTED" && x.EntidadId == original.Id));
        }

        // La solicitud rechazada no reserva la clave: una corrección válida puede reutilizarla.
        var cancel = new CorrectCashMovementRequest(version, "El movimiento no debía registrarse");
        var corrected = await ReadSuccessAsync<CashMovementCorrectionDto>(await PostCorrectionAsync(
            client, path, key, cancel), HttpStatusCode.OK);
        Assert.Equal(original.Id, corrected.OriginalMovementId);
        Assert.Null(corrected.ReplacementMovementId);
        Assert.Equal(100m, corrected.ExpectedCashAfter);
        Assert.Equal(corrected, await ReadSuccessAsync<CashMovementCorrectionDto>(await PostCorrectionAsync(
            client, path, key, cancel), HttpStatusCode.OK));
        Assert.Equal(100m, (await ReconcileAsync(client)).PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);

        await using var finalScope = _factory!.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var movements = await finalDb.MovimientoCaja.AsNoTracking()
            .Where(x => x.TurnoCajaId == scenario.Shift.Id).ToArrayAsync();
        Assert.Equal(3, movements.Length);
        Assert.Equal(30m, Assert.Single(movements, x => x.Id == original.Id).Valor);
        var reversal = Assert.Single(movements, x => x.Id == corrected.ReversalMovementId);
        Assert.Equal("REVERSO", reversal.Tipo);
        Assert.Equal("CORRECCION_MANUAL", reversal.CategoriaMovimiento);
        Assert.Equal(30m, reversal.Valor);
        Assert.Equal(original.Id, reversal.MovimientoRevertidoId);
        Assert.Single(movements, x => x.CorrelacionId == corrected.CorrelationId);
        Assert.Equal(1, await finalDb.EventoAuditoria.CountAsync(x =>
            x.Accion == "CASHMOVEMENTCORRECTED" && x.EntidadId == original.Id));
    }
}
