using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task ClosureDifferenceReasons_OrderReadOnlyAndInactiveEstablishment()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var establishment = await db.Establecimiento.SingleAsync();
        foreach (var code in new[] { "ZZZ", "AAA" })
            db.MotivoOperacion.Add(new MotivoOperacion
            {
                Id = Guid.CreateVersion7(), EstablecimientoId = establishment.Id,
                TipoOperacion = CashConstants.CashDifferenceReasonType, Codigo = code,
                Nombre = "Motivo de orden " + code, Activo = true, OrdenVisual = 0,
                FechaCreacion = _clock.UtcNow.UtcDateTime, Version = 1
            });
        await db.SaveChangesAsync();
        var audits = await db.EventoAuditoria.CountAsync();
        var reasons = await db.MotivoOperacion.CountAsync();
        var versions = await db.MotivoOperacion.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Version).ToArrayAsync();
        for (var read = 0; read < 2; read++)
        {
            var response = await ReadSuccessAsync<CashClosureDifferenceReasonDto[]>(
                await client.GetAsync("/api/cash/closure-difference-reasons"), HttpStatusCode.OK);
            Assert.Equal(new[] { "AAA", "ZZZ", "CONTEO_DIFERENTE" }, response.Select(x => x.Code));
        }
        establishment.Activo = false;
        await db.SaveChangesAsync();
        Assert.Empty(await ReadSuccessAsync<CashClosureDifferenceReasonDto[]>(
            await client.GetAsync("/api/cash/closure-difference-reasons"), HttpStatusCode.OK));
        Assert.Equal(audits, await db.EventoAuditoria.CountAsync());
        Assert.Equal(reasons, await db.MotivoOperacion.CountAsync());
        Assert.Equal(versions, await db.MotivoOperacion.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Version).ToArrayAsync());
        Assert.Equal(scenario.Shift.Version, await db.TurnoCaja.AsNoTracking().Select(x => x.Version).SingleAsync());
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosureDifferenceReasons_HybridGrantRevocationAndSessionInvalidation(bool supervisor)
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var manager = AuthenticatedClient(scenario.Administrator.AccessToken);
        var userId = supervisor ? scenario.Users.Supervisor.Id : scenario.Users.Cashier.Id;
        var userName = supervisor ? SupervisorUserName : CashierUserName;
        var password = supervisor ? SupervisorPassword : CashierPassword;
        using var actor = AuthenticatedClient(supervisor ? scenario.Supervisor.AccessToken : scenario.Cashier.AccessToken);
        Assert.Equal(supervisor ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await actor.GetAsync("/api/cash/closure-difference-reasons")).StatusCode);
        var permissionId = await GetPermissionIdAsync(manager, PermissionCodes.CashShiftManage);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(
            $"/api/users/{userId}/permissions/{permissionId}", new SetUserPermissionRequest(true))).StatusCode);
        var granted = await LoginAsync(actor, userName, password);
        SetBearer(actor, granted.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await actor.GetAsync("/api/cash/closure-difference-reasons")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(
            $"/api/users/{userId}/permissions/{permissionId}", new SetUserPermissionRequest(false))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await actor.GetAsync("/api/cash/closure-difference-reasons")).StatusCode);
        var revoked = await LoginAsync(actor, userName, password);
        SetBearer(actor, revoked.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync("/api/cash/closure-difference-reasons")).StatusCode);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ClosureDifferenceReasons_DeactivatedAfterReadIsRejectedAtCloseWithoutFinancialWrites()
    {
        var scenario = await PrepareCorrectionScenarioAsync(100m);
        using var client = AuthenticatedClient(scenario.Administrator.AccessToken);
        var reason = Assert.Single(await ReadSuccessAsync<CashClosureDifferenceReasonDto[]>(
            await client.GetAsync("/api/cash/closure-difference-reasons"), HttpStatusCode.OK));
        var request = BalancedClose(await ReconcileAsync(client));
        request = request with
        {
            PaymentMethods = request.PaymentMethods.Select(x => new CountedPaymentMethodRequest(
                x.PaymentMethodId, x.CountedAmount + (x.PaymentMethodId == scenario.Catalog.PaymentMethodIds.First() ? 1m : 0m))).ToArray(),
            DifferenceReasonId = reason.Id,
            Authorization = null
        };
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var storedReason = await db.MotivoOperacion.SingleAsync(x => x.Id == reason.Id);
        storedReason.Activo = false;
        await db.SaveChangesAsync();
        var before = await CaptureFinancialStateAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            $"/api/cash/shifts/{scenario.Shift.Id}/close", request)).StatusCode);
        Assert.Equal(before, await CaptureFinancialStateAsync());
        Assert.Equal(CashConstants.OpenState, await db.TurnoCaja.AsNoTracking().Select(x => x.Estado).SingleAsync());
        Assert.Equal(0, await db.AutorizacionCierreTurno.CountAsync());
        Assert.Equal(0, await db.DetalleArqueoMedioPago.CountAsync());
    }
}
