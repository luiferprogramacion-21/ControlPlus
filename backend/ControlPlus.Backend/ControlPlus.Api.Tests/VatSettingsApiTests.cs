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
    public async Task VatSetting_IsRemoved_CannotBeEnabled_AndSalesHaveNoBreakdown()
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
        // El IVA se quitó del sistema (18 de noviembre de 2026): no se puede encender.
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/settings/vat",
            new UpdateVatSettingRequest(true, 19m, 0)), HttpStatusCode.BadRequest);
        Assert.False((await ReadAsync<VatSettingDto>(await client.GetAsync("/api/settings/vat"), HttpStatusCode.OK)).Enabled);

        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        _ = await StartSessionAsync(client, setup.Admin.AccessToken,
            setup.Cashier.AccessToken, setup.Users.Cashier.Id);
        SetBearer(client, setup.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "vat-setting-sale",
            CashRequest(setup.Products.Standard, 1, 1000m)), HttpStatusCode.Created);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.False(await db.EventoAuditoria.AsNoTracking().AnyAsync(x => x.Accion == "VATSETTINGUPDATED"));
        var detail = await db.DetalleVenta.AsNoTracking().SingleAsync(x => x.VentaId == sale.Id);
        Assert.Equal(0m, detail.ValorIva ?? 0m);
        var header = await db.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id);
        Assert.Equal(0m, header.ImpuestoIncluidoTotal ?? 0m);
        var receipt = await ReadAsync<SaleReceiptDto>(await client.GetAsync(
            $"/api/sales/{sale.Id}/receipt"), HttpStatusCode.OK);
        Assert.Equal(0m, receipt.VatTotal ?? 0m);
        Assert.Equal(sale.Total, receipt.Total);
        Assert.Equal(1000m, receipt.Payments.Sum(x => x.AppliedAmount));
    }
}
