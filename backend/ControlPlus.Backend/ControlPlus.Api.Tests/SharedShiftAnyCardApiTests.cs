using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Sales.Contracts;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>
/// Shared shift (decision of 20 November 2026): any employee's card opens the terminal's session, whoever is logged in,
/// and the sale is recorded in the card owner's name.
/// </summary>
public sealed partial class SalesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task SharedShift_CashierCardOnAdministratorScreen_SellsInCashierName()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);

        // The Administrator is logged in on the terminal and the cashier scans her own card there.
        var cashierCard = await IssueCredentialAsync(client, setup.Admin.AccessToken, setup.Users.Cashier.Id);
        var session = await StartSessionAsync(client, setup.Admin.AccessToken, cashierCard);
        Assert.Equal(setup.Users.Cashier.Id, session.UserId);

        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        Assert.Equal(session.Id, state.OwnOperatorSession?.Id);
        Assert.False(string.IsNullOrWhiteSpace(state.ActiveOperatorName));
        Assert.Equal("CAJERO", state.ActiveOperatorRole);

        // Still logged in as the Administrator, the sale belongs to the cashier and uses up her session.
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "shared-any-card-sale",
            new ConfirmSaleRequest(null, [new(setup.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 1000m, 1000m, null)])), HttpStatusCode.Created);
        Assert.Equal(setup.Users.Cashier.Id, sale.UserId);
        Assert.Equal(session.Id, sale.OperatorSessionId);

        // A scrambled card is still refused.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/cash/operator-sessions",
            new StartOperatorSessionRequest("00000000000000"))).StatusCode);
    }
}

/// <summary>
/// Pruebas del usuario del 25 de noviembre de 2026: en turno compartido quien opera también se identifica escribiendo su
/// usuario y contraseña. La sesión queda unida a su carné vigente; sin carné vigente no se abre.
/// </summary>
public sealed partial class SalesApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task SharedShift_OperatorTypesUserAndPassword_OpensSessionWithOwnCard()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, setup.Admin.AccessToken);
        SetBearer(client, setup.Admin.AccessToken);

        // Sin carné vigente no hay sesión: la base exige un carné en cada sesión de operador.
        var withoutCard = await client.PostAsJsonAsync("/api/cash/operator-sessions",
            new StartOperatorSessionRequest(null, CashierUser, CashierPassword));
        Assert.Equal(HttpStatusCode.Conflict, withoutCard.StatusCode);
        Assert.Contains("no tiene carné vigente", await withoutCard.Content.ReadAsStringAsync());

        var card = await IssueCredentialAsync(client, setup.Admin.AccessToken, setup.Users.Cashier.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/cash/operator-sessions",
            new StartOperatorSessionRequest(null, CashierUser, "contraseña-errada"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/cash/operator-sessions",
            new StartOperatorSessionRequest(card, CashierUser, CashierPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/cash/operator-sessions",
            new StartOperatorSessionRequest(null, CashierUser, null))).StatusCode);

        var session = await ReadAsync<OperatorSessionDto>(await client.PostAsJsonAsync("/api/cash/operator-sessions",
            new StartOperatorSessionRequest(null, CashierUser, CashierPassword)), HttpStatusCode.Created);
        Assert.Equal(setup.Users.Cashier.Id, session.UserId);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        Assert.Equal(session.Id, state.OwnOperatorSession?.Id);
        Assert.Equal("CAJERO", state.ActiveOperatorRole);

        // La venta queda a nombre de quien escribió su usuario y contraseña.
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "shared-password-session-sale",
            new ConfirmSaleRequest(null, [new(setup.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 1000m, 1000m, null)])), HttpStatusCode.Created);
        Assert.Equal(setup.Users.Cashier.Id, sale.UserId);
        Assert.Equal(session.Id, sale.OperatorSessionId);
    }
}
