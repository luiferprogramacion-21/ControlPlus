using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    private const string DrawerHeader = "X-Cash-Drawer-Status";

    /// <summary>
    /// Cajón de dinero (decisión del 19 de noviembre de 2026): apagado no escribe nada; encendido abre una sola vez
    /// por venta en efectivo, nunca en repeticiones, reimpresiones ni pagos electrónicos, y dos ventas simultáneas
    /// no se bloquean en ciclo con el registro del cajón.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task CashDrawer_OpensOncePerCheckout_NotOnConfirmReprintOrElectronic_AndManualNeedsReason()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var profile = await ReadAsync<PrintProfileDto>(await client.PostAsJsonAsync(
            "/api/printing/profiles", new CreatePrintProfileRequest("Recibos con cajón", "COLA_CAJON", 32)),
            HttpStatusCode.Created);
        Assert.False(profile.DrawerEnabled);
        await ReadAsync<CashRegisterDto>(await client.PostAsJsonAsync(
            "/api/cash/register", new ConfigureCashRegisterRequest("SALES-CASH", "Caja aislada de Ventas")),
            HttpStatusCode.Created);
        await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(0m, "Turno con cajón", Mode: CashConstants.IndividualMode)),
            HttpStatusCode.Created);
        var request = CashRequest(scenario.Products.Standard, 1, 1_000m);

        Assert.Equal("DESACTIVADO", (await ReadAsync<CashDrawerOpenResultDto>(
            await CheckoutAsync(client, Guid.NewGuid().ToString()), HttpStatusCode.OK)).Status);

        var enabled = await ReadAsync<PrintProfileDto>(await client.PutAsJsonAsync(
            $"/api/printing/profiles/{profile.Id}/drawer",
            new UpdatePrintProfileDrawerRequest(true, 2, 100, 100, profile.Version)), HttpStatusCode.OK);
        Assert.True(enabled.DrawerEnabled);
        Assert.Equal(2, enabled.DrawerPin);

        // Decisión del 19 de noviembre de 2026: el cajón abre al pulsar «Cobrar», una vez por intento de cobro.
        var checkoutKey = Guid.NewGuid().ToString();
        var opened = await ReadAsync<CashDrawerOpenResultDto>(await CheckoutAsync(client, checkoutKey), HttpStatusCode.OK);
        Assert.Equal("ENVIADO", opened.Status);
        Assert.Equal(opened.AttemptId, (await ReadAsync<CashDrawerOpenResultDto>(
            await CheckoutAsync(client, checkoutKey), HttpStatusCode.OK)).AttemptId);
        // Confirmar la venta ya no abre el cajón otra vez.
        var first = await PostSaleAsync(client, "drawer-sale", request);
        var sale = await ReadAsync<SaleDto>(first, HttpStatusCode.Created);
        Assert.False(first.Headers.Contains(DrawerHeader));

        var electronic = await PostSaleAsync(client, "drawer-transfer", new ConfirmSaleRequest(
            null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
            [new("TRANSFERENCIA", 1_000m, null, "TRANSFER-DRAWER")]));
        Assert.Equal(HttpStatusCode.Created, electronic.StatusCode);
        Assert.False(electronic.Headers.Contains(DrawerHeader));

        using (var reprint = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{sale.Id}/reprint-requests"))
        {
            reprint.Headers.Add("Idempotency-Key", "drawer-reprint");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(reprint)).StatusCode);
        }

        using var raceA = CreateClient(scenario.Admin.AccessToken);
        using var raceB = CreateClient(scenario.Admin.AccessToken);
        var race = await Task.WhenAll(
            PostSaleAsync(raceA, "drawer-race-a", request),
            CheckoutAsync(raceB, Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Created, race[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, race[1].StatusCode);

        var sender = (FakeCashDrawerPulseSender)_factory!.Services.GetRequiredService<ICashDrawerPulseSender>();
        Assert.Equal(2, sender.Pulses.Count);
        Assert.All(sender.Pulses, pulse =>
        {
            Assert.Equal("COLA_CAJON", pulse.Queue);
            Assert.Equal(new byte[] { 0x1b, 0x70, 0, 50, 50 }, pulse.Bytes);
        });
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var attempts = await db.AperturaCajon.AsNoTracking().ToArrayAsync();
            Assert.Equal(2, attempts.Length);
            Assert.All(attempts, attempt =>
            {
                Assert.Equal("COBRO", attempt.TipoOperacion);
                Assert.Equal("ENVIADO", attempt.Estado);
                Assert.Equal(profile.Id, attempt.PerfilImpresoraId);
            });
            Assert.Contains(attempts, attempt => attempt.OperacionId == Guid.Parse(checkoutKey));
            Assert.DoesNotContain(attempts, attempt => attempt.OperacionId == sale.Id);
        }

        var status = await ReadAsync<CashDrawerStatusDto>(
            await client.GetAsync("/api/cash/drawer/status"), HttpStatusCode.OK);
        Assert.True(status.Enabled);
        Assert.True(status.CanOpen);
        var key = Guid.NewGuid().ToString();
        var manual = await ReadAsync<CashDrawerOpenResultDto>(
            await OpenDrawerAsync(client, key, "Entregar cambio"), HttpStatusCode.OK);
        Assert.Equal("ENVIADO", manual.Status);
        var manualReplay = await ReadAsync<CashDrawerOpenResultDto>(
            await OpenDrawerAsync(client, key, "Entregar cambio"), HttpStatusCode.OK);
        Assert.Equal(manual.AttemptId, manualReplay.AttemptId);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await OpenDrawerAsync(client, Guid.NewGuid().ToString(), "   ")).StatusCode);
        using var cashier = CreateClient(scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await OpenDrawerAsync(cashier, Guid.NewGuid().ToString(), "Sin permiso")).StatusCode);
        Assert.Equal(3, sender.Pulses.Count);
    }

    private static async Task<HttpResponseMessage> CheckoutAsync(HttpClient client, string idempotencyKey)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/cash/drawer/checkout");
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> OpenDrawerAsync(HttpClient client, string idempotencyKey, string reason)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/cash/drawer/open")
        {
            Content = JsonContent.Create(new OpenCashDrawerRequest(reason))
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private sealed class FakeCashDrawerPulseSender : ICashDrawerPulseSender
    {
        private readonly ConcurrentQueue<(string Queue, byte[] Bytes)> _pulses = new();

        public IReadOnlyList<(string Queue, byte[] Bytes)> Pulses => _pulses.ToArray();

        public Task SendPulseAsync(string queueName, CashDrawerPulse pulse,
            CancellationToken cancellationToken = default)
        {
            _pulses.Enqueue((queueName, pulse.ToEscPosBytes()));
            return Task.CompletedTask;
        }
    }
}
