using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Reports;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task DailyCashFlow_SeparatesOpeningManualMovementsAndMixedPayments()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment("EFECTIVO", 100m, 150m), new FinancialPayment("TRANSFERENCIA", 200m)]);
        foreach (var (route, amount) in new[] { ("incomes", 40m), ("expenses", 10m), ("cash-drops", 20m) })
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
                $"/api/cash/movements/{route}", new CreateCashMovementRequest(amount,
                    "Movimiento de reporte", null))).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDailyCashFlowRepository>();
        var installationId = await context.Caja.Select(x => x.InstalacionId).SingleAsync();
        var date = scenario.Shift.OperatingDate;
        var entries = await repository.GetEntriesAsync(installationId, date, date, default);
        Assert.Equal(100m, Assert.Single(entries, x => x.Origin == "APERTURA").Amount);
        Assert.Equal(100m, Assert.Single(entries, x => x.Origin == "VENTA_CONTADO" && x.Method == "EFECTIVO").Amount);
        Assert.Equal(200m, Assert.Single(entries, x => x.Origin == "VENTA_CONTADO" && x.Method == "TRANSFERENCIA").Amount);
        Assert.Equal(40m, Assert.Single(entries, x => x.Origin == "INGRESO_MANUAL").Amount);
        Assert.Equal(-10m, Assert.Single(entries, x => x.Origin == "GASTO_MANUAL").Amount);
        Assert.Equal(-20m, Assert.Single(entries, x => x.Origin == "SANGRIA_MANUAL").Amount);
        Assert.Equal(6, entries.Count);
        Assert.Empty(await repository.GetEntriesAsync(Guid.NewGuid(), date, date, default));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData("APLICADO")]
    [InlineData("ANULADO")]
    public async Task DailyCashFlow_ExplicitReversalKeepsOriginalAndBooksInverseOnReversalShift(string state)
    {
        var scenario = await PrepareCorrectionScenarioAsync(0m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var original = await SeedFinancialPaymentAsync(scenario, scenario.Shift.Id,
            [new FinancialPayment("TRANSFERENCIA", 75m)]);
        await ReadSuccessAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", BalancedClose(await ReconcileAsync(client))), HttpStatusCode.OK);
        var next = await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Turno de reverso de reporte")), HttpStatusCode.Created);
        await using (var dateScope = _factory!.Services.CreateAsyncScope())
        {
            var dateContext = dateScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            await dateContext.TurnoCaja.Where(x => x.Id == next.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FechaOperativa,
                    scenario.Shift.OperatingDate.AddDays(1)));
        }
        await SeedFinancialReversalAsync(scenario, next.Id, original, 75m, state);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDailyCashFlowRepository>();
        var installationId = await context.Caja.Select(x => x.InstalacionId).SingleAsync();
        var firstDate = await context.TurnoCaja.Where(x => x.Id == scenario.Shift.Id).Select(x => x.FechaOperativa).SingleAsync();
        var secondDate = await context.TurnoCaja.Where(x => x.Id == next.Id).Select(x => x.FechaOperativa).SingleAsync();
        var entries = await repository.GetEntriesAsync(installationId, firstDate, secondDate, default);
        Assert.Contains(entries, x => x.Date == firstDate && x.Origin == "VENTA_CONTADO" && x.Amount == 75m);
        Assert.Contains(entries, x => x.Date == secondDate && x.Origin == "REVERSO_VENTA_CONTADO" && x.Amount == -75m);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task DailyCashFlow_LayawayPaymentUsesItsMethodsAndNotCanonicalMovement()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        await SeedLayawayInitialPaymentAsync(scenario);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDailyCashFlowRepository>();
        var installationId = await context.Caja.Select(x => x.InstalacionId).SingleAsync();
        var date = scenario.Shift.OperatingDate;
        var entries = await repository.GetEntriesAsync(installationId, date, date, default);
        Assert.Equal(600m, entries.Where(x => x.Origin == "PAGO_APARTADO").Sum(x => x.Amount));
        Assert.Equal(3, entries.Count(x => x.Origin == "PAGO_APARTADO"));
        Assert.DoesNotContain(entries, x => x.Method is null && x.Origin != "APERTURA");
    }
}
