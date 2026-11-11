using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task VatSetting_RequiresAdministratorValidatesVersionAndPersistsSaleBreakdown()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        SetBearer(client, setup.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/settings/vat")).StatusCode);

        SetBearer(client, setup.Admin.AccessToken);
        var initial = await ReadAsync<VatSettingDto>(await client.GetAsync("/api/settings/vat"), HttpStatusCode.OK);
        Assert.False(initial.Enabled);
        Assert.Null(initial.Percentage);
        Assert.Equal(0, initial.Version);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/settings/vat",
            new UpdateVatSettingRequest(true, null, 0)), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/settings/vat",
            new UpdateVatSettingRequest(true, 19.123m, 0)), HttpStatusCode.BadRequest);
        var enabled = await ReadAsync<VatSettingDto>(await client.PutAsJsonAsync("/api/settings/vat",
            new UpdateVatSettingRequest(true, 19m, 0)), HttpStatusCode.OK);
        Assert.Equal(19m, enabled.Percentage);
        Assert.Equal(1, enabled.Version);
        Assert.NotNull(enabled.UpdatedAtUtc);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/settings/vat",
            new UpdateVatSettingRequest(false, null, 0)), HttpStatusCode.Conflict);

        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "vat-setting-sale",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var audit = await db.EventoAuditoria.AsNoTracking()
            .SingleAsync(x => x.Accion == "VATSETTINGUPDATED");
        Assert.NotNull(audit.UsuarioId);
        Assert.Contains("PreviousEnabled", audit.DatosAnteriores ?? audit.DatosNuevos ?? "");
        var detail = await db.DetalleVenta.AsNoTracking().SingleAsync(x => x.VentaId == sale.Id);
        Assert.Equal(19m, detail.PorcentajeIva);
        Assert.Equal(decimal.Round(detail.SubtotalNeto / 1.19m, 0, MidpointRounding.AwayFromZero), detail.BaseGravable);
        Assert.Equal(detail.SubtotalNeto - detail.BaseGravable, detail.ValorIva);
        var receipt = await ReadAsync<SaleReceiptDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/receipt"), HttpStatusCode.OK);
        Assert.Equal(19m, receipt.VatPercentage);
        Assert.Equal(detail.BaseGravable, receipt.TaxableBaseTotal);
        Assert.Equal(detail.ValorIva, receipt.VatTotal);
        Assert.Equal(sale.Total, receipt.Total);
        Assert.Equal(1000m, receipt.Payments.Sum(x => x.AppliedAmount));
    }
}
