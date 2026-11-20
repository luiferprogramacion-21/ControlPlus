using ControlPlus.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Reports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests : IAsyncLifetime
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SaleReceipt_ReprintRequest_ReplayPreservesFinancialRowsAndAuditsOnce()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        _ = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "receipt-sale",
            new ConfirmSaleRequest(null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 1000m, 1000m, null)])), HttpStatusCode.Created);
        SetBearer(client, scenario.Admin.AccessToken);
        var soldProduct = await ReadAsync<ProductDto>(
            await client.GetAsync($"/api/products/{scenario.Products.Standard}"), HttpStatusCode.OK);
        using (var blocked = await client.DeleteAsync(
            $"/api/products/{soldProduct.Id}?expectedVersion={soldProduct.Version}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("solo puede inactivarse", await blocked.Content.ReadAsStringAsync());
        }
        SetBearer(client, scenario.Cashier.AccessToken);

        var noProfile = await ReadAsync<PrintStatusDto>(await client.GetAsync("/api/printing/status"), HttpStatusCode.OK);
        Assert.False(noProfile.HasActiveProfile);
        Assert.Null(noProfile.ProfileName);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var beforeSales = await db.Venta.CountAsync();
        var beforePayments = await db.PagoVenta.CountAsync();
        var beforeCash = await db.MovimientoCaja.CountAsync();
        var beforeStock = await db.MovimientoInventario.CountAsync();

        var url = $"/api/sales/{sale.Id}/receipt";
        var first = await ReadAsync<SaleReceiptDto>(await client.GetAsync(url), HttpStatusCode.OK);
        var second = await ReadAsync<SaleReceiptDto>(await client.GetAsync(url), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(sale.Total, first.Total);
        Assert.Equal(sale.VisibleNumber, first.VisibleNumber);

        var requestUrl = $"/api/sales/{sale.Id}/reprint-requests";
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        firstRequest.Headers.Add("Idempotency-Key", "receipt-reprint-1");
        var requested = await ReadAsync<SaleReprintRequestDto>(await client.SendAsync(firstRequest), HttpStatusCode.OK);
        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        replayRequest.Headers.Add("Idempotency-Key", "receipt-reprint-1");
        var replay = await ReadAsync<SaleReprintRequestDto>(await client.SendAsync(replayRequest), HttpStatusCode.OK);
        Assert.Equal(requested, replay);
        Assert.Equal("SOLICITADA", requested.Status);
        using var anotherRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        anotherRequest.Headers.Add("Idempotency-Key", "receipt-reprint-2");
        var another = await ReadAsync<SaleReprintRequestDto>(await client.SendAsync(anotherRequest), HttpStatusCode.OK);
        Assert.NotEqual(requested.RequestId, another.RequestId);
        Assert.Equal(2, await db.EventoAuditoria.CountAsync(x => x.Accion == "SALEREPRINTREQUESTED" && x.EntidadId == sale.Id));
        Assert.Equal(0, await db.TrabajoImpresion.CountAsync(x => x.DocumentoEmitido.EntidadId == sale.Id));
        Assert.Equal(beforeSales, await db.Venta.CountAsync());
        Assert.Equal(beforePayments, await db.PagoVenta.CountAsync());
        Assert.Equal(beforeCash, await db.MovimientoCaja.CountAsync());
        Assert.Equal(beforeStock, await db.MovimientoInventario.CountAsync());

        SetBearer(client, scenario.SecondCashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        using (var otherRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl))
        {
            otherRequest.Headers.Add("Idempotency-Key", "receipt-other-user");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(otherRequest)).StatusCode);
        }
        SetBearer(client, scenario.Admin.AccessToken);
        var reprintPermission = await GetPermissionIdAsync(client, PermissionCodes.SalesReprint);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Users.Cashier.Id}/permissions/{reprintPermission}",
            new SetUserPermissionRequest(false))).StatusCode);
        var revokedCashier = await LoginAsync(client, CashierUser, CashierPassword);
        SetBearer(client, revokedCashier.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        using (var deniedRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl))
        {
            deniedRequest.Headers.Add("Idempotency-Key", "receipt-revoked-user");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(deniedRequest)).StatusCode);
        }
        Assert.Equal(2, await db.EventoAuditoria.CountAsync(x => x.Accion == "SALEREPRINTREQUESTED" && x.EntidadId == sale.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DailySalesReport_CountsSaleHeadersOnceAndExcludesOtherInstallation()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        _ = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "report-cash-sale",
            new ConfirmSaleRequest(null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 400m, 400m, null),
                 new("TRANSFERENCIA", 600m, null, "REPORT-COMBINED-REFERENCE")])), HttpStatusCode.Created);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IFinancialSalesReportRepository>();
        var persisted = await context.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id);
        Assert.Equal(2, await context.PagoVenta.CountAsync(x => x.VentaId == sale.Id));
        var rows = await repository.GetDailySalesAsync(persisted.InstalacionId,
            persisted.FechaOperativa, persisted.FechaOperativa, default);
        var cash = Assert.Single(rows);
        Assert.Equal("CONTADO", cash.SaleType);
        Assert.Equal(1, cash.Transactions);
        Assert.Equal(persisted.Total, cash.Total);
        Assert.Empty(await repository.GetDailySalesAsync(Guid.NewGuid(),
            persisted.FechaOperativa, persisted.FechaOperativa, default));
        Assert.Empty(await repository.GetDailySalesAsync(persisted.InstalacionId,
            persisted.FechaOperativa.AddDays(1), persisted.FechaOperativa.AddDays(1), default));

        SetBearer(client, scenario.Supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/reports/sales/daily?from={persisted.FechaOperativa:yyyy-MM-dd}&to={persisted.FechaOperativa:yyyy-MM-dd}")).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        var report = await ReadAsync<FinancialSalesReport>(await client.GetAsync(
            $"/api/reports/sales/daily?from={persisted.FechaOperativa:yyyy-MM-dd}&to={persisted.FechaOperativa:yyyy-MM-dd}"), HttpStatusCode.OK);
        Assert.Equal(new SalesAmount(1, persisted.Total), report.Total);
        Assert.Equal(new SalesAmount(0, 0m), report.Credit);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DailyCashFlowReport_SplitsMixedPaymentByMethodAndExcludesLinkedMovement()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        _ = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "report-c2-mixed-sale",
            new ConfirmSaleRequest(null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 400m, 400m, null),
                 new("TRANSFERENCIA", 600m, null, "REPORT-C2-REFERENCE")])), HttpStatusCode.Created);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IDailyCashFlowRepository>();
        var persisted = await context.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id);
        var date = persisted.FechaOperativa;
        var entries = await repository.GetEntriesAsync(persisted.InstalacionId, date, date, default);
        Assert.Equal(400m, Assert.Single(entries, x => x.Origin == "VENTA_CONTADO" && x.Method == "EFECTIVO").Amount);
        Assert.Equal(600m, Assert.Single(entries, x => x.Origin == "VENTA_CONTADO" && x.Method == "TRANSFERENCIA").Amount);
        Assert.Equal(1000m, entries.Where(x => x.Method is not null).Sum(x => x.Amount));
        Assert.DoesNotContain(entries, x => x.Method is null && x.Origin != "APERTURA");
        Assert.Empty(await repository.GetEntriesAsync(Guid.NewGuid(), date, date, default));

        SetBearer(client, scenario.Supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/reports/cash-flow/daily?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}")).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        var report = await ReadAsync<DailyCashFlowReport>(await client.GetAsync(
            $"/api/reports/cash-flow/daily?from={date:yyyy-MM-dd}&to={date.AddDays(1):yyyy-MM-dd}"), HttpStatusCode.OK);
        Assert.Equal(1000m, report.PaymentTotal);
        Assert.Empty(report.Days[1].Payments);
        Assert.Equal(0m, report.Days[1].PaymentTotal);
    }
    [Fact]
    [Trait("Category", "Integration")]
    public async Task DashboardSummary_UsesActiveInstallationOperatingDateAndPersistedSourcesWithoutDoubleCounting()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var active = await db.Instalacion.Include(x => x.Establecimiento).SingleAsync(x => x.Activo);
            active.Establecimiento.ZonaHoraria = "Pacific/Kiritimati";
            await db.SaveChangesAsync();
        }
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "dashboard-mixed-sale",
            new ConfirmSaleRequest(null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 400m, 400m, null),
                 new("TRANSFERENCIA", 600m, null, "DASHBOARD-REFERENCE")])), HttpStatusCode.Created);

        var adminSession = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Admin.AccessToken,
            (await GetCurrentAdminIdAsync()).Id);
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            "/api/cash/movements/incomes", new CreateCashMovementRequest(25m, "Ingreso dashboard", adminSession.Id))).StatusCode);

        await using var verifyScope = _factory!.Services.CreateAsyncScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var installation = await context.Instalacion.AsNoTracking().Include(x => x.Establecimiento).SingleAsync(x => x.Activo);
        var persistedSale = await context.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id);
        var payments = await context.PagoVenta.AsNoTracking().Where(x => x.VentaId == sale.Id).ToArrayAsync();
        var shift = await context.TurnoCaja.AsNoTracking().SingleAsync(x => x.Id == persistedSale.TurnoCajaId);
        var linkedMovementId = Assert.Single(payments.Select(x => x.MovimientoCajaId).Distinct());
        Assert.NotNull(linkedMovementId);
        var linkedMovement = await context.MovimientoCaja.AsNoTracking().SingleAsync(x => x.Id == linkedMovementId);
        var manual = await context.MovimientoCaja.AsNoTracking().SingleAsync(x =>
            x.TurnoCajaId == shift.Id && x.Concepto == "Ingreso dashboard");
        Assert.Equal(installation.Id, persistedSale.InstalacionId);
        Assert.Equal(2, payments.Length);
        Assert.Equal("Pacific/Kiritimati", installation.Establecimiento.ZonaHoraria);
        var expectedDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(installation.Establecimiento.ZonaHoraria)).DateTime);
        Assert.Equal(expectedDate, shift.FechaOperativa);
        Assert.Equal(expectedDate, persistedSale.FechaOperativa);
        Assert.Equal(25m, manual.Valor);
        Assert.Equal(payments.Sum(x => x.ValorAplicado), linkedMovement.Valor);

        var response = await ReadAsync<DashboardSummary>(await client.GetAsync("/api/dashboard/summary"), HttpStatusCode.OK);
        Assert.Equal(expectedDate, response.OperatingDate);
        Assert.Equal(new SalesAmount(1, persistedSale.Total), response.CashSales);
        Assert.Equal(new SalesAmount(0, 0m), response.CreditSales);
        Assert.Equal(new SalesAmount(1, persistedSale.Total), response.TotalSales);
        Assert.Equal(shift.MontoInicial, response.OpeningBalance);
        Assert.Equal(payments.Sum(x => x.ValorAplicado), response.PaymentTotal);
        Assert.Equal(manual.Valor, response.ManualTotal);
        Assert.Equal(payments.Sum(x => x.ValorAplicado) + manual.Valor, response.NetCashFlow);

        // Comparación: ayer no hubo ventas, así que no hay porcentaje que calcular.
        Assert.Equal(expectedDate.AddDays(-1), response.Comparison.PreviousDate);
        Assert.Equal(0m, response.Comparison.PreviousTotal);
        Assert.Null(response.Comparison.PercentChange);

        // Caja actual: el mismo efectivo esperado del arqueo (apertura + cobro en efectivo + ingreso manual);
        // la parte pagada por transferencia no es efectivo.
        var cashMethodId = await context.MetodoPago.AsNoTracking()
            .Where(x => x.Codigo == "EFECTIVO").Select(x => x.Id).SingleAsync();
        var cashPaid = payments.Where(x => x.MetodoPagoId == cashMethodId).Sum(x => x.ValorAplicado);
        Assert.NotNull(response.CurrentCash);
        Assert.Equal(shift.MontoInicial + cashPaid + manual.Valor, response.CurrentCash!.ExpectedCash);
        Assert.Equal(new DateTimeOffset(DateTime.SpecifyKind(shift.FechaHoraApertura, DateTimeKind.Utc)),
            response.CurrentCash.OpenedAt);

        // Ventas por hora: la única venta cae en su hora local del establecimiento.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(installation.Establecimiento.ZonaHoraria);
        var saleHour = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(persistedSale.FechaHora, DateTimeKind.Utc), zone).Hour;
        Assert.Equal(persistedSale.Total, response.HourlySales.Sum(x => x.Total));
        Assert.Equal(1, response.HourlySales.Sum(x => x.Transactions));
        Assert.Contains(response.HourlySales, x => x.Hour == saleHour && x.Transactions == 1);

        // Actividad reciente: la venta con su número visible, sin cliente.
        var activity = Assert.Single(response.RecentActivity);
        Assert.Equal(sale.Id, activity.SaleId);
        Assert.Equal($"{persistedSale.Prefijo}-{persistedSale.Serie}-{persistedSale.Consecutivo}", activity.Number);
        Assert.Null(activity.CustomerName);
        Assert.Equal(persistedSale.Total, activity.Total);
        Assert.Equal(("CONTADO", "CONFIRMADA"), (activity.SaleType, activity.State));

        // Alertas: el stock bajo (disponible igual o menor al mínimo) se cuenta aparte, en memoria.
        var expectedLowStock = (await context.Producto.AsNoTracking().ToArrayAsync())
            .Count(p => p.Activo && p.StockActual - p.StockReservado <= p.StockMinimo);
        Assert.Equal(new DashboardAlerts(expectedLowStock, 0, 0, DashboardRules.UpcomingLayawayDays), response.Alerts);

        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.FinancialReportsRead);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Users.Supervisor.Id}/permissions/{permissionId}",
            new SetUserPermissionRequest(true))).StatusCode);
        var granted = await LoginAsync(client, SupervisorUser, SupervisorPassword);
        SetBearer(client, granted.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard/summary")).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Users.Supervisor.Id}/permissions/{permissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        var revoked = await LoginAsync(client, SupervisorUser, SupervisorPassword);
        SetBearer(client, revoked.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard/summary")).StatusCode);

        async Task<Usuario> GetCurrentAdminIdAsync()
        {
            await using var scope = _factory!.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>()
                .Usuario.AsNoTracking().SingleAsync(x => x.NombreUsuario == AdminUser);
        }
    }

    private const string AdminUser = "sales.admin.test";
    private const string AdminPassword = "Sales-Administrator-Test-2026!";
    private const string SupervisorUser = "sales.supervisor.test";
    private const string SupervisorPassword = "Sales-Supervisor-Test-2026!";
    private const string CashierUser = "sales.cashier.test";
    private const string CashierPassword = "Sales-Cashier-Test-2026!";
    private const string SecondCashierUser = "sales.cashier.two.test";
    private const string SecondCashierPassword = "Sales-Cashier-Two-Test-2026!";

    private static readonly string[] ExpectedAppliedMigrations =
    [
        "20260912010000_OfficialPhase4Baseline",
        "20260912011000_SeedApprovedSecurityCatalog",
        "20260912012000_HybridPermissionsV1",
        "20260913010000_SeedMeasurementUnitsV1",
        "20260913020000_CashRegisterModuleV1",
        "20260930010000_SeedInventoryAdjustmentReasonsV1",
        "20261005010000_CustomerSearchIndexesV1",
        "20261014010000_InventoryMovementRegistrationOrderV1",
        "20261020010000_LayawayDeliveryWithoutSaleV1",
        "20261021010000_LayawayCancellationOptionalReasonV1",
        "20261026010000_InitialProductInventoryMovementV1",
        "20261028010000_SaleReceiptDataSnapshotV1",
        "20261029010000_PermanentProductReferencesV1",
        "20261102010000_GrantProductCodeAccessV1",
        "20261102020000_CategoryProductReferencesV1",
        "20261104010000_AdminSelfSaleCancellationV1",
        "20261104020000_AdminSelfCashDifferenceV1",
        "20261105010000_SupervisorLayawayRefundDecisionV1",
        "20261106010000_ProductPhysicalDeletionPrivilegesV1",
        "20261110010000_BusinessVatSettingV1",
        "20261110020000_BusinessVatWholePesoSalesV1",
        "20261111010000_BusinessVatWholePesoLayawaysChangesV1",
        "20261112010000_LayawayMinimumInitialPaymentV1",
        "20261115010000_OptionalSalePaymentReferenceV1",
        "20261115020000_ConfigurableWholesaleMinimumV1",
        "20261116010000_CreditRiskControlsV1",
        "20261116020000_CreditInstallmentsV1",
        "20261119010000_CashDrawerV1",
        "20261119013000_HeldSalesV1",
        "20261119020000_OptionalEstablishmentIdentificationV1",
        "20261120010000_SaleCorrectionsV1",
        "20261120020000_LayawayCorrectionsV1",
        "20261120030000_CashInventoryCorrectionsV1",
        "20261120040000_PurchaseReceiptCorrectionsV1",
        "20261120050000_AdditionalSaleLinkV1"
    ];

    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtKey = RandomSecret(64);
    private readonly LocalPostgresDatabase _postgres = new LocalPostgresDatabase();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var context = new ControlPlusDbContext(
                         new DbContextOptionsBuilder<ControlPlusDbContext>()
                             .UseNpgsql(_postgres.GetConnectionString()).Options))
        {
            await context.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IPrintSpooler, FakePrintSpooler>();
                services.AddSingleton<ICashDrawerPulseSender, FakeCashDrawerPulseSender>();
            });
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-sales-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-sales-client");
            builder.UseSetting("Jwt:SigningKey", _jwtKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "240");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Sale_CreatesTheSalesNumberingOnFirstUse_WhenItDoesNotExist()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        // An installation created before the consecutives were automatic has no VENTA row.
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            context.ConsecutivoDocumento.Remove(await context.ConsecutivoDocumento.SingleAsync(x =>
                x.TipoDocumento == "VENTA"));
            await context.SaveChangesAsync();
        }
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        _ = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);

        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await PostSaleAsync(
            client, "missing-sales-numbering", CashRequest(scenario.Products.Standard, 1, 1_000m));

        Assert.Equal(HttpStatusCode.Created, sale.StatusCode);
        await using var verification = _factory!.Services.CreateAsyncScope();
        var db = verification.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var numbering = await db.ConsecutivoDocumento.AsNoTracking().SingleAsync(x => x.TipoDocumento == "VENTA");
        Assert.Equal("VTA", numbering.Prefijo);
        Assert.Equal(1, numbering.UltimoNumero);
        Assert.Equal(1, await db.Venta.CountAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SalesNumberingUpdate_IsRejected_AfterFirstConfirmedSale()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var beforeSale = await ReadAsync<SalesDocumentNumberingDto>(
            await client.GetAsync("/api/document-numbering/sales"), HttpStatusCode.OK);
        Assert.True(beforeSale.CanCorrect);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        _ = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);
        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await PostSaleAsync(
            client, "numbering-update-after-sale", CashRequest(scenario.Products.Standard, 1, 1_000m))).StatusCode);

        SetBearer(client, scenario.Admin.AccessToken);
        var numbering = await ReadAsync<SalesDocumentNumberingDto>(
            await client.GetAsync("/api/document-numbering/sales"), HttpStatusCode.OK);
        Assert.False(numbering.CanCorrect);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest("ALT", 10, numbering.Version))).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExhaustedNumbering_AllowsLongMaxValueOnce_ThenReturnsStableConflictWithoutAnyWrite()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var numbering = await ReadAsync<SalesDocumentNumberingDto>(
            await client.GetAsync("/api/document-numbering/sales"), HttpStatusCode.OK);
        var boundary = await ReadAsync<SalesDocumentNumberingDto>(await client.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest("VTA", long.MaxValue, numbering.Version)),
            HttpStatusCode.OK);
        Assert.Equal(long.MaxValue - 1, boundary.LastNumber);

        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        _ = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);
        SetBearer(client, scenario.Cashier.AccessToken);
        var lastSaleResponse = await PostSaleAsync(
            client, "last-available-number", CashRequest(scenario.Products.Standard, 1, 1_000m));
        Assert.Equal("application/json", lastSaleResponse.Content.Headers.ContentType?.MediaType);
        var lastSale = await ReadAsync<SaleDto>(lastSaleResponse, HttpStatusCode.Created);
        Assert.Equal($"VTA-SALESTEST1-{long.MaxValue}", lastSale.VisibleNumber);

        var secondSession = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        var before = await CaptureSalesStateAsync(scenario.Products.Standard, secondSession.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var rejected = await PostSaleAsync(
            client, "exhausted-numbering", CashRequest(scenario.Products.Standard, 1, 1_000m));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        var problemJson = await rejected.Content.ReadAsStringAsync();
        using (var problem = JsonDocument.Parse(problemJson))
        {
            Assert.Equal("numbering.exhausted", problem.RootElement.GetProperty("code").GetString());
            Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        }
        Assert.DoesNotContain("OverflowException", problemJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", problemJson, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", problemJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await CaptureSalesStateAsync(scenario.Products.Standard, secondSession.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NumberingUpdateConcurrentWithSale_NeverChangesSequenceAfterSaleConfirmation()
    {
        using var setupClient = CreateClient();
        using var updateClient = CreateClient();
        using var saleClient = CreateClient();
        var scenario = await BootstrapAsync(setupClient);
        await OpenSharedShiftAsync(setupClient, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(setupClient, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        _ = await StartSessionAsync(setupClient, scenario.Cashier.AccessToken, credential);
        SetBearer(setupClient, scenario.Admin.AccessToken);
        var numbering = await ReadAsync<SalesDocumentNumberingDto>(
            await setupClient.GetAsync("/api/document-numbering/sales"), HttpStatusCode.OK);
        SetBearer(updateClient, scenario.Admin.AccessToken);
        SetBearer(saleClient, scenario.Cashier.AccessToken);

        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        await LockActiveInstallationAsync(blocker, blockerTransaction);

        var updateTask = updateClient.PutAsJsonAsync(
            "/api/document-numbering/sales",
            new UpdateSalesDocumentNumberingRequest("RACE", 100, numbering.Version));
        var saleTask = PostSaleAsync(
            saleClient, "numbering-update-sale-race", CashRequest(scenario.Products.Standard, 1, 1_000m));
        await WaitForInstallationLockWaitersAsync(2);
        await blockerTransaction.CommitAsync();

        var updateResponse = await updateTask;
        var saleResponse = await saleTask;
        Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
        Assert.Contains(updateResponse.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persistedSale = await context.Venta.AsNoTracking().SingleAsync();
        var persistedNumbering = await context.ConsecutivoDocumento.AsNoTracking().SingleAsync(x =>
            x.TipoDocumento == "VENTA");
        Assert.Equal(persistedSale.Consecutivo, persistedNumbering.UltimoNumero);
        Assert.Equal(persistedSale.Prefijo, persistedNumbering.Prefijo);
        if (updateResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(100, persistedSale.Consecutivo);
            Assert.Equal("RACE", persistedSale.Prefijo);
            Assert.Equal(1, await context.EventoAuditoria.CountAsync(x =>
                x.Accion == "SALESDOCUMENTNUMBERINGUPDATED"));
        }
        else
        {
            Assert.Equal(1, persistedSale.Consecutivo);
            Assert.Equal("VTA", persistedSale.Prefijo);
            Assert.Equal(0, await context.EventoAuditoria.CountAsync(x =>
                x.Accion == "SALESDOCUMENTNUMBERINGUPDATED"));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SharedSale_ConsumesAndClosesItsSession_AndAuditsConsumptionWithoutOperatorChange()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        var session = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);

        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(
            client, "session-consumed-sale", CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Created);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persistedSale = await context.Venta.AsNoTracking().SingleAsync(x => x.Id == sale.Id);
        var persistedSession = await context.SesionOperador.AsNoTracking().SingleAsync(x => x.Id == session.Id);
        Assert.Equal(session.Id, persistedSale.SesionOperadorId);
        Assert.Equal("CERRADA", persistedSession.Estado);
        Assert.NotNull(persistedSession.FechaFin);
        Assert.Equal("CAMBIO_OPERADOR", persistedSession.MotivoCierre);

        var audit = await context.EventoAuditoria.AsNoTracking().SingleAsync(x =>
            x.EntidadId == session.Id && x.Accion == "OPERATORSESSIONCLOSED");
        Assert.Null(audit.Motivo);
        Assert.Contains("SessionConsumedBySale", audit.DatosNuevos ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("true", audit.DatosNuevos ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CAMBIO_OPERADOR", audit.DatosNuevos ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("cambio de operador", audit.DatosNuevos ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(x =>
            x.EntidadId == sale.Id && x.Accion == "SALECONFIRMED"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SharedSession_CannotBeReused_ButSameOperatorCanStartNewSessionAndSellAgain()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var credential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        var firstSession = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);

        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await PostSaleAsync(
            client, "first-session-sale", CashRequest(scenario.Products.Standard, 1, 1_000m))).StatusCode);
        await AssertProblemAsync(await PostSaleAsync(
            client, "reused-session-sale", CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Conflict);

        var secondSession = await StartSessionAsync(client, scenario.Cashier.AccessToken, credential);
        Assert.NotEqual(firstSession.Id, secondSession.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await PostSaleAsync(
            client, "second-session-sale", CashRequest(scenario.Products.Standard, 1, 1_000m))).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(2, await context.Venta.CountAsync(x => x.UsuarioId == scenario.Users.Cashier.Id));
        Assert.Equal(2, await context.SesionOperador.CountAsync(x =>
            x.UsuarioId == scenario.Users.Cashier.Id && x.Estado == "CERRADA"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SharedSale_RejectsMissingClosedAndOtherOperatorsSession()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);

        SetBearer(client, scenario.Cashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(
            client, "missing-session", CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Conflict);

        var cashierCredential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.Cashier.Id);
        var closedSession = await StartSessionAsync(client, scenario.Cashier.AccessToken, cashierCredential);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(
            $"/api/cash/operator-sessions/{closedSession.Id}/close", null)).StatusCode);
        await AssertProblemAsync(await PostSaleAsync(
            client, "closed-session", CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Conflict);

        var otherCredential = await IssueCredentialAsync(client, scenario.Admin.AccessToken, scenario.Users.SecondCashier.Id);
        _ = await StartSessionAsync(client, scenario.SecondCashier.AccessToken, otherCredential);
        SetBearer(client, scenario.Cashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(
            client, "other-operator-session", CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CashAndCombinedPayments_CalculateChange_AndPersistOneCanonicalCashMovement()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);

        SetBearer(client, scenario.Cashier.AccessToken);
        var combined = await ReadAsync<SaleDto>(await PostSaleAsync(client, "combined-payment", new ConfirmSaleRequest(
            null,
            [new(scenario.Products.Standard, 1, "MINORISTA")],
            null,
            null,
            [new("EFECTIVO", 400m, 450m, null), new("TRANSFERENCIA", 600m, null, "TRANSFER-COMBINED-REFERENCE")])),
            HttpStatusCode.Created);
        Assert.Equal(50m, combined.Change);
        Assert.Equal(2, combined.Payments.Count);
        await AssertCombinedPersistenceAsync(combined.Id, scenario.Products.Standard, expectedStock: 99);

        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var cash = await ReadAsync<SaleDto>(await PostSaleAsync(
            client, "cash-payment", CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Created);
        Assert.Equal("EFECTIVO", Assert.Single(cash.Payments).MethodCode);
        Assert.Equal(0m, cash.Change);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ElectronicPaymentReferences_AreOptional_ButReceivedAmountAndChangeRemainInvalid()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);

        await AssertProblemAsync(await PostSaleAsync(client, "transfer-change", new ConfirmSaleRequest(
            null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
            [new("TRANSFERENCIA", 1_000m, 1_050m, "TRANSFER-CHANGE-REJECTED")])), HttpStatusCode.BadRequest);
        var transfer = await ReadAsync<SaleDto>(await PostSaleAsync(client, "transfer-no-reference", new ConfirmSaleRequest(
            null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
            [new("TRANSFERENCIA", 1_000m, null, null)])), HttpStatusCode.Created);
        Assert.Null(Assert.Single(transfer.Payments).Reference);

        // En modalidad compartida cada venta confirmada consume la sesión del operador.
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);

        var nequiWithoutReference = await ReadAsync<SaleDto>(await PostSaleAsync(client, "nequi-no-reference", new ConfirmSaleRequest(
            null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
            [new("NEQUI", 1_000m, null, " ")])), HttpStatusCode.Created);
        Assert.Null(Assert.Single(nequiWithoutReference.Payments).Reference);

        // En modalidad compartida cada venta confirmada consume la sesión del operador.
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, "cash-no-received", new ConfirmSaleRequest(
            null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
            [new("EFECTIVO", 1_000m, null, null)])), HttpStatusCode.BadRequest);

        var nequi = await ReadAsync<SaleDto>(await PostSaleAsync(client, "valid-nequi", new ConfirmSaleRequest(
            null, [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
            [new("NEQUI", 1_000m, null, "NEQUI-TEST-REFERENCE")])), HttpStatusCode.Created);
        Assert.Equal("NEQUI", Assert.Single(nequi.Payments).MethodCode);
        Assert.Equal(0m, nequi.Change);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProductStockAndCostGuards_RejectInvalidSales_AndConsolidateRepeatedScans()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);

        await AssertProblemAsync(await PostSaleAsync(client, "unknown-cost", CashRequest(
            scenario.Products.UnknownCost, 1, 500m)), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await PostSaleAsync(client, "insufficient-stock", CashRequest(
            scenario.Products.LowStock, 2, 1_000m)), HttpStatusCode.BadRequest);
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(
            $"/api/products/{scenario.Products.Inactive}/deactivate", null)).StatusCode);
        SetBearer(client, scenario.Cashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, "inactive-product", CashRequest(
            scenario.Products.Inactive, 1, 500m)), HttpStatusCode.BadRequest);

        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "repeated-scan", new ConfirmSaleRequest(
            null,
            [new(scenario.Products.Standard, 1, "MINORISTA"), new(scenario.Products.Standard, 2, "MINORISTA")],
            null,
            null,
            [new("EFECTIVO", 3_000m, 3_000m, null)])), HttpStatusCode.Created);
        Assert.Single(sale.Lines);
        Assert.Equal(3, sale.Lines.Single().Quantity);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Discounts_RespectRoleLimitsCostFloorWholesaleRulesAndDeterministicDistribution()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);

        await AssertProblemAsync(await PostSaleAsync(client, "cashier-over-limit", DiscountRequest(
            scenario.Products.RoleDiscount, 6m, 940m, authorized: true)), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await PostSaleAsync(client, "under-cost", DiscountRequest(
            scenario.Products.UnderCost, 5m, 95m, authorized: true)), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await PostSaleAsync(client, "wholesale-discount", new ConfirmSaleRequest(
            null, [new(scenario.Products.Standard, 12, "MAYORISTA", new("PORCENTAJE", 5m))], null, "No permitido",
            [new("EFECTIVO", 10_260m, 10_260m, null)],
            Authorization: new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword))), HttpStatusCode.BadRequest);

        var distributed = await ReadAsync<SaleDto>(await PostSaleAsync(client, "general-discount", new ConfirmSaleRequest(
            null,
            [new(scenario.Products.ResidueA, 1, "MINORISTA"), new(scenario.Products.ResidueB, 1, "MINORISTA"),
                new(scenario.Products.ResidueC, 1, "MINORISTA")],
            new("PORCENTAJE", 5m),
            "Promoción controlada",
            [new("EFECTIVO", 304m, 304m, null)],
            Authorization: new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        Assert.Equal(16m, distributed.DiscountTotal);
        Assert.Equal(6m, distributed.Lines.Single(x => x.ProductId == scenario.Products.ResidueC).DiscountAmount);
        Assert.Equal(5m, distributed.Lines.Single(x => x.ProductId == scenario.Products.ResidueA).DiscountAmount);
        Assert.Equal(5m, distributed.Lines.Single(x => x.ProductId == scenario.Products.ResidueB).DiscountAmount);

        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Supervisor.AccessToken, scenario.Users.Supervisor.Id);
        SetBearer(client, scenario.Supervisor.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, "supervisor-over-limit", DiscountRequest(
            scenario.Products.RoleDiscount, 21m, 790m)), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.Created, (await PostSaleAsync(client, "supervisor-limit", DiscountRequest(
            scenario.Products.RoleDiscount, 20m, 800m))).StatusCode);

        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Admin.AccessToken, scenario.Admin.User.Id);
        SetBearer(client, scenario.Admin.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, "admin-over-limit", DiscountRequest(
            scenario.Products.RoleDiscount, 81m, 190m)), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.Created, (await PostSaleAsync(client, "admin-limit", DiscountRequest(
            scenario.Products.RoleDiscount, 80m, 200m))).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ShiftAndSessionRules_RejectMissingClosedAndForeignIndividualTurns()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Cashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, "no-shift", CashRequest(
            scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Conflict);

        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        await CloseCurrentShiftAsync(client, scenario.Admin.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, "closed-shift", CashRequest(
            scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Conflict);

        SetBearer(client, scenario.Admin.AccessToken);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        await ReadAsync<CashStateDto>(await client.PutAsJsonAsync(
            "/api/cash/shift-mode", new UpdateCashShiftModeRequest(CashConstants.IndividualMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        SetBearer(client, scenario.Supervisor.AccessToken);
        await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(0m, "Turno individual para pertenencia")), HttpStatusCode.Created);

        SetBearer(client, scenario.Cashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, "foreign-individual-shift", CashRequest(
            scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Forbidden);
        SetBearer(client, scenario.Supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await PostSaleAsync(client, "own-individual-shift", CashRequest(
            scenario.Products.Standard, 1, 1_000m))).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IdempotencyAndConcurrency_DoNotDuplicateSalesFinancialEffectsOrStock()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Admin.AccessToken, scenario.Admin.User.Id);

        using var idempotentA = CreateClient(scenario.Admin.AccessToken);
        using var idempotentB = CreateClient(scenario.Admin.AccessToken);
        var sameKeyResponses = await Task.WhenAll(
            PostSaleAsync(idempotentA, "concurrent-idempotent", CashRequest(scenario.Products.RoleDiscount, 1, 1_000m)),
            PostSaleAsync(idempotentB, "concurrent-idempotent", CashRequest(scenario.Products.RoleDiscount, 1, 1_000m)));
        Assert.All(sameKeyResponses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var sameKeySales = await Task.WhenAll(sameKeyResponses.Select(x => x.Content.ReadFromJsonAsync<SaleDto>()));
        var saleId = Assert.Single(sameKeySales.Select(x => x!.Id).Distinct());
        await AssertIdempotencyInventoryAndAuditAsync(
            saleId, scenario.Products.RoleDiscount, "REFERENCE-NOT-PRESENT", "concurrent-idempotent");

        await CloseCurrentShiftAsync(client, scenario.Admin.AccessToken);
        SetBearer(client, scenario.Admin.AccessToken);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        await ReadAsync<CashStateDto>(await client.PutAsJsonAsync(
            "/api/cash/shift-mode", new UpdateCashShiftModeRequest(CashConstants.IndividualMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(0m, "Turno para concurrencia de stock")), HttpStatusCode.Created);

        using var stockA = CreateClient(scenario.Admin.AccessToken);
        using var stockB = CreateClient(scenario.Admin.AccessToken);
        var stockResponses = await Task.WhenAll(
            PostSaleAsync(stockA, "stock-race-a", CashRequest(scenario.Products.LowStock, 1, 500m)),
            PostSaleAsync(stockB, "stock-race-b", CashRequest(scenario.Products.LowStock, 1, 500m)));
        Assert.Single(stockResponses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(stockResponses, x => x.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SalesVisibility_UsesCurrentHybridOwnAndAllPermissions()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "visibility-sale", CashRequest(
            scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sales/own?page=1&pageSize=20")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sales?page=1&pageSize=20")).StatusCode);

        SetBearer(client, scenario.SecondCashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/sales/{sale.Id}")).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        var listed = await ReadAsync<PagedResult<SaleSummaryDto>>(await client.GetAsync("/api/sales?page=1&pageSize=20"), HttpStatusCode.OK);
        var summary = Assert.Single(listed.Items, x => x.Id == sale.Id);
        Assert.Equal("CONTADO", summary.SaleType);
        Assert.Equal(["Efectivo"], summary.PaymentMethods);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/sales/{sale.Id}")).StatusCode);

        var ownReadPermission = await GetPermissionIdAsync(client, PermissionCodes.SalesOwnRead);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{scenario.Users.Cashier.Id}/permissions/{ownReadPermission}",
            new SetUserPermissionRequest(false))).StatusCode);
        var refreshedCashier = await LoginAsync(client, CashierUser, CashierPassword);
        SetBearer(client, refreshedCashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/sales/own")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConfirmSaleJson_RejectsUnknownRootLineDiscountAndPaymentPropertiesWithoutWrites()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var session = await StartSessionAsync(
            client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        var before = await CaptureSalesStateAsync(scenario.Products.Standard, session.Id);

        var payloads = new[]
        {
            (Json: JsonSerializer.Serialize(new
                {
                    customerId = (Guid?)null,
                    lines = new[] { new { productId = scenario.Products.Standard, quantity = 1, priceType = "MINORISTA" } },
                    generalDiscount = (object?)null,
                    discountReason = (string?)null,
                    payments = new[] { new { methodCode = "EFECTIVO", appliedAmount = 1_000m, receivedAmount = (decimal?)1_000m, reference = (string?)null } },
                    unknownRoot = true
                }), InvalidProperty: "unknownRoot"),
            (Json: JsonSerializer.Serialize(new
                {
                    customerId = (Guid?)null,
                    lines = new[] { new { productId = scenario.Products.Standard, quantity = 1, priceType = "MINORISTA", unknownLine = true } },
                    generalDiscount = (object?)null,
                    discountReason = (string?)null,
                    payments = new[] { new { methodCode = "EFECTIVO", appliedAmount = 1_000m, receivedAmount = (decimal?)1_000m, reference = (string?)null } }
                }), InvalidProperty: "unknownLine"),
            (Json: JsonSerializer.Serialize(new
                {
                    customerId = (Guid?)null,
                    lines = new[] { new { productId = scenario.Products.Standard, quantity = 1, priceType = "MINORISTA" } },
                    generalDiscount = new { type = "PORCENTAJE", value = 5m, unknownDiscount = true },
                    discountReason = "Prueba estricta",
                    payments = new[] { new { methodCode = "EFECTIVO", appliedAmount = 950m, receivedAmount = (decimal?)950m, reference = (string?)null } }
                }), InvalidProperty: "unknownDiscount"),
            (Json: JsonSerializer.Serialize(new
                {
                    customerId = (Guid?)null,
                    lines = new[] { new { productId = scenario.Products.Standard, quantity = 1, priceType = "MINORISTA" } },
                    generalDiscount = (object?)null,
                    discountReason = (string?)null,
                    payments = new[] { new { methodCode = "EFECTIVO", appliedAmount = 1_000m, receivedAmount = (decimal?)1_000m, reference = (string?)null, unknownPayment = true } }
                }), InvalidProperty: "unknownPayment")
        };

        SetBearer(client, scenario.Cashier.AccessToken);
        for (var index = 0; index < payloads.Length; index++)
        {
            var response = await PostRawSaleAsync(client, $"strict-json-{index}", payloads[index].Json);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            using var problem = JsonDocument.Parse(body);
            Assert.True(problem.RootElement.TryGetProperty("errors", out var errors), body);
            Assert.True(errors.EnumerateObject().Any(), body);
            Assert.Contains(payloads[index].InvalidProperty, errors.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(before, await CaptureSalesStateAsync(scenario.Products.Standard, session.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task RepeatedProduct_InvalidDiscountIsRejectedRegardlessOfLineOrder(bool invalidDiscountFirst)
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var session = await StartSessionAsync(
            client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        var before = await CaptureSalesStateAsync(scenario.Products.Standard, session.Id);
        var valid = new SaleLineRequest(scenario.Products.Standard, 1, "MINORISTA");
        var invalid = new SaleLineRequest(
            scenario.Products.Standard, 1, "MINORISTA", new SaleDiscountRequest("NO_EXISTE", 5m));
        var lines = invalidDiscountFirst ? new[] { invalid, valid } : new[] { valid, invalid };

        SetBearer(client, scenario.Cashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, $"invalid-repeat-{invalidDiscountFirst}", new ConfirmSaleRequest(
            null, lines, null, null, [new("EFECTIVO", 2_000m, 2_000m, null)])), HttpStatusCode.BadRequest);

        Assert.Equal(before, await CaptureSalesStateAsync(scenario.Products.Standard, session.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeferredCommitFailure_RollsBackEntireSaleAndPreservesStockSequenceAndSession()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var session = await StartSessionAsync(
            client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        await InstallDeferredSaleFailureAsync();
        var before = await CaptureSalesStateAsync(scenario.Products.Standard, session.Id);

        SetBearer(client, scenario.Cashier.AccessToken);
        var response = await PostSaleAsync(
            client, "forced-commit-rollback", CashRequest(scenario.Products.Standard, 1, 1_000m));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(await DeferredSaleFailureObservedAsync());
        Assert.Equal(before, await CaptureSalesStateAsync(scenario.Products.Standard, session.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CustomerValidation_RejectsMissingAndInactive_AndPersistsActiveCustomer()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var inactiveCustomer = await CreateCustomerAsync(scenario.Admin.User.Id, false, "INACTIVE");
        var activeCustomer = await CreateCustomerAsync(scenario.Admin.User.Id, true, "ACTIVE");
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var session = await StartSessionAsync(
            client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        var before = await CaptureSalesStateAsync(scenario.Products.Standard, session.Id);

        SetBearer(client, scenario.Cashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(client, "missing-customer", CashRequest(
            scenario.Products.Standard, 1, 1_000m, Guid.CreateVersion7())), HttpStatusCode.NotFound);
        await AssertProblemAsync(await PostSaleAsync(client, "inactive-customer", CashRequest(
            scenario.Products.Standard, 1, 1_000m, inactiveCustomer)), HttpStatusCode.NotFound);
        Assert.Equal(before, await CaptureSalesStateAsync(scenario.Products.Standard, session.Id));

        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "active-customer", CashRequest(
            scenario.Products.Standard, 1, 1_000m, activeCustomer)), HttpStatusCode.Created);
        Assert.Equal(activeCustomer, sale.CustomerId);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(activeCustomer, await context.Venta.Where(x => x.Id == sale.Id).Select(x => x.ClienteId).SingleAsync());

        SetBearer(client, scenario.Admin.AccessToken);
        var status = await ReadAsync<CustomerDto>(await client.PutAsJsonAsync(
            $"/api/customers/{activeCustomer}/status", new SetCustomerStatusRequest(false, 1)), HttpStatusCode.OK);
        Assert.False(status.IsActive);
        context.ChangeTracker.Clear();
        Assert.False(await context.Cliente.Where(x => x.Id == activeCustomer).Select(x => x.Activo).SingleAsync());
        Assert.Equal(activeCustomer, await context.Venta.Where(x => x.Id == sale.Id).Select(x => x.ClienteId).SingleAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PricingEdges_RejectZeroAndMixedDiscountScopes_AndAcceptFixedAndWholesaleSales()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);

        await AssertProblemAsync(await PostSaleAsync(client, "zero-total", CashRequest(
            scenario.Products.ZeroTotal, 1, 0m)), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await PostSaleAsync(client, "mixed-discount-scopes", new ConfirmSaleRequest(
            null,
            [new(scenario.Products.Standard, 1, "MINORISTA", new("PORCENTAJE", 1m))],
            new("PORCENTAJE", 1m),
            "Ámbitos incompatibles",
            [new("EFECTIVO", 980m, 980m, null)])), HttpStatusCode.BadRequest);

        var fixedDiscount = await ReadAsync<SaleDto>(await PostSaleAsync(client, "fixed-discount", new ConfirmSaleRequest(
            null,
            [new(scenario.Products.Standard, 1, "MINORISTA", new("VALOR_FIJO", 50m))],
            null,
            "Descuento fijo válido",
            [new("EFECTIVO", 950m, 950m, null)],
            Authorization: new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        Assert.Equal(50m, fixedDiscount.DiscountTotal);
        Assert.Equal("VALOR_FIJO", Assert.Single(fixedDiscount.Lines).DiscountType);

        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        var wholesale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "valid-wholesale", new ConfirmSaleRequest(
            null,
            [new(scenario.Products.Standard, 12, "MAYORISTA")],
            null,
            null,
            [new("EFECTIVO", 10_800m, 10_800m, null)],
            Authorization: new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword))), HttpStatusCode.Created);
        var wholesaleLine = Assert.Single(wholesale.Lines);
        Assert.Equal("MAYORISTA", wholesaleLine.PriceType);
        Assert.Equal(900m, wholesaleLine.UnitPrice);
        Assert.Equal(0m, wholesaleLine.DiscountAmount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task IdempotencyKey_RejectsDifferentContentAndDifferentUserWithoutAdditionalWrites()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        var session = await StartSessionAsync(
            client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        const string key = "idempotency-conflicts";
        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await PostSaleAsync(
            client, key, CashRequest(scenario.Products.Standard, 1, 1_000m))).StatusCode);
        var afterOriginal = await CaptureSalesStateAsync(scenario.Products.Standard, session.Id);

        await AssertProblemAsync(await PostSaleAsync(
            client, key, CashRequest(scenario.Products.Standard, 2, 2_000m)), HttpStatusCode.Conflict);
        SetBearer(client, scenario.SecondCashier.AccessToken);
        await AssertProblemAsync(await PostSaleAsync(
            client, key, CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Conflict);
        Assert.Equal(afterOriginal, await CaptureSalesStateAsync(scenario.Products.Standard, session.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConfirmedCombinedSale_ReconciliationUsesPaymentsWithoutDoubleCountingCashMovement()
    {
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Cashier.AccessToken, scenario.Users.Cashier.Id);
        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Created, (await PostSaleAsync(client, "reconciliation-sale", new ConfirmSaleRequest(
            null,
            [new(scenario.Products.Standard, 1, "MINORISTA")],
            null,
            null,
            [new("EFECTIVO", 400m, 450m, null), new("TRANSFERENCIA", 600m, null, "TRANSFER-RECONCILIATION")])))
            .StatusCode);

        SetBearer(client, scenario.Admin.AccessToken);
        var reconciliation = await ReadAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(1_000m, reconciliation.TotalExpected);
        Assert.Equal(400m, reconciliation.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        Assert.Equal(600m, reconciliation.PaymentMethods.Single(x => x.Code == "TRANSFERENCIA").ExpectedAmount);
        Assert.Equal(0m, reconciliation.PaymentMethods.Single(x => x.Code == "NEQUI").ExpectedAmount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OfficialSharedSessionSchemaAndEfModel_RemainUnchanged()
    {
        await using var migrationContext = new ControlPlusDbContext(
            new DbContextOptionsBuilder<ControlPlusDbContext>()
                .UseNpgsql(_postgres.GetConnectionString()).Options);
        Assert.Equal(ExpectedAppliedMigrations, await migrationContext.Database.GetAppliedMigrationsAsync());

        await using var officialContext = new OfficialControlPlusDbContext(
            new DbContextOptionsBuilder<OfficialControlPlusDbContext>()
                .UseNpgsql(_postgres.GetConnectionString()).Options);
        var saleType = officialContext.Model.FindEntityType(typeof(Venta));
        Assert.NotNull(saleType);
        var sessionIndex = Assert.Single(saleType.GetIndexes(), x =>
            x.GetDatabaseName() == "uq_venta_sesion_operador");
        Assert.True(sessionIndex.IsUnique);
        Assert.Equal("uq_venta_sesion_operador", sessionIndex.GetDatabaseName());
        var sessionForeignKey = Assert.Single(saleType.GetForeignKeys(), x =>
            x.Properties.Count == 1 && x.Properties[0].Name == nameof(Venta.SesionOperadorId));
        Assert.True(sessionForeignKey.IsUnique);

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'ventas'
                      AND indexname = 'uq_venta_sesion_operador'
                      AND indexdef ILIKE '%UNIQUE%'
                )
                AND EXISTS (
                    SELECT 1
                    FROM pg_proc p
                    JOIN pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = 'ventas'
                      AND p.proname = 'validar_cierre_sesion_venta'
                )
                AND EXISTS (
                    SELECT 1
                    FROM pg_trigger t
                    JOIN pg_class c ON c.oid = t.tgrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'ventas'
                      AND c.relname = 'venta'
                      AND NOT t.tgisinternal
                      AND t.tgdeferrable
                      AND pg_get_triggerdef(t.oid) ILIKE '%validar_cierre_sesion_venta%'
                );
            """;
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    private HttpClient CreateClient(string? accessToken = null)
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (accessToken is not null) SetBearer(client, accessToken);
        return client;
    }

    private async Task<Scenario> BootstrapAsync(HttpClient client)
    {
        await SetupAsync(client);
        var admin = await LoginAsync(client, AdminUser, AdminPassword);
        SetBearer(client, admin.AccessToken);
        var users = await CreateUsersAsync(client);
        var products = await CreateSalesCatalogAsync(client);
        var cashier = await LoginAsync(client, CashierUser, CashierPassword);
        var supervisor = await LoginAsync(client, SupervisorUser, SupervisorPassword);
        var secondCashier = await LoginAsync(client, SecondCashierUser, SecondCashierPassword);
        return new(admin, users, products, cashier, supervisor, secondCashier);
    }

    private async Task SetupAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                "Comercio ficticio de Ventas", "SALES-TEST", "SALES-INSTALLATION", "SALESTEST1",
                "SALES-TERMINAL", "Terminal ficticia de Ventas", AdminUser,
                "Administrador ficticio de Ventas", AdminPassword))
        };
        request.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
    }

    private async Task OpenSharedShiftAsync(HttpClient client, string adminToken)
    {
        SetBearer(client, adminToken);
        await ReadAsync<CashRegisterDto>(await client.PostAsJsonAsync(
            "/api/cash/register", new ConfigureCashRegisterRequest("SALES-CASH", "Caja aislada de Ventas")), HttpStatusCode.Created);
        var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        await ReadAsync<CashStateDto>(await client.PutAsJsonAsync(
            "/api/cash/shift-mode", new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync(
            "/api/cash/shifts", new OpenCashShiftRequest(0m, "Turno compartido para Ventas")), HttpStatusCode.Created);
    }

    private static async Task CloseCurrentShiftAsync(HttpClient client, string adminToken)
    {
        SetBearer(client, adminToken);
        var reconciliation = await ReadAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        var counts = reconciliation.PaymentMethods.Where(m => m.AffectsCash)
            .Select(x => new CountedPaymentMethodRequest(x.PaymentMethodId, x.ExpectedAmount)).ToArray();
        await ReadAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{reconciliation.ShiftId}/close",
            new CloseCashShiftRequest(reconciliation.ShiftVersion, counts, null, null, null)), HttpStatusCode.OK);
    }

    private async Task<Users> CreateUsersAsync(HttpClient client)
    {
        var supervisorRole = await GetRoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRole = await GetRoleIdAsync(client, RoleCodes.Cashier);
        var supervisor = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorUser, "Supervisor ficticio de Ventas", SupervisorPassword, [supervisorRole])), HttpStatusCode.Created);
        var cashier = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierUser, "Cajero ficticio de Ventas", CashierPassword, [cashierRole])), HttpStatusCode.Created);
        var second = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SecondCashierUser, "Segundo cajero ficticio", SecondCashierPassword, [cashierRole])), HttpStatusCode.Created);
        return new(supervisor, cashier, second);
    }

    private async Task<Products> CreateSalesCatalogAsync(HttpClient client)
    {
        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync(
            "/api/categories", new CreateCategoryRequest("Categoría aislada de Ventas", null, "VNT")), HttpStatusCode.Created);
        var units = await ReadAsync<IReadOnlyCollection<MeasurementUnitDto>>(
            await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        var unit = units.Single(x => x.Code == "UNIDAD");

        async Task<Guid> Create(string code, decimal retail, decimal? wholesale = null)
        {
            var value = await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
                new CreateProductRequest(category.Id, unit.Id, null, code, null, null, true,
                    $"Producto {code}", null, retail, wholesale, null, 0)), HttpStatusCode.Created);
            return value.Id;
        }

        var products = new Products(
            await Create("SALE-STANDARD", 1_000m, 900m),
            await Create("SALE-RES-A", 101m),
            await Create("SALE-RES-B", 102m),
            await Create("SALE-RES-C", 117m),
            await Create("SALE-NO-COST", 500m),
            await Create("SALE-LOW-STOCK", 500m),
            await Create("SALE-INACTIVE", 500m),
            await Create("SALE-UNDER-COST", 100m),
            await Create("SALE-ROLE", 1_000m),
            await Create("SALE-ZERO", 0m));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        // The VTA consecutive is created by the installation itself.
        var rows = await context.Producto.Where(x => new[]
        {
            products.Standard, products.ResidueA, products.ResidueB, products.ResidueC, products.UnknownCost,
            products.LowStock, products.Inactive, products.UnderCost, products.RoleDiscount, products.ZeroTotal
        }.Contains(x.Id)).ToArrayAsync();
        foreach (var product in rows)
        {
            product.StockActual = product.Id == products.LowStock ? 1 : 100;
            product.CostoPromedio = product.Id switch
            {
                var id when id == products.UnknownCost => null,
                var id when id == products.UnderCost => 98m,
                var id when id == products.RoleDiscount => 100m,
                var id when id == products.Standard => 500m,
                var id when id == products.ZeroTotal => 0m,
                _ => 10m
            };
        }
        await context.SaveChangesAsync();
        return products;
    }

    private static async Task<string> IssueCredentialAsync(HttpClient client, string adminToken, Guid userId)
    {
        SetBearer(client, adminToken);
        var credential = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{userId}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        return credential.CredentialToken;
    }

    private static async Task<OperatorSessionDto> StartSessionAsync(
        HttpClient client, string userToken, string credentialToken)
    {
        SetBearer(client, userToken);
        return await ReadAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(credentialToken)), HttpStatusCode.Created);
    }

    private static async Task<OperatorSessionDto> StartSessionAsync(
        HttpClient client, string adminToken, string userToken, Guid userId)
    {
        var credential = await IssueCredentialAsync(client, adminToken, userId);
        return await StartSessionAsync(client, userToken, credential);
    }

    private async Task AssertCombinedPersistenceAsync(Guid saleId, Guid productId, int expectedStock)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var payments = await context.PagoVenta.Where(x => x.VentaId == saleId).ToArrayAsync();
        Assert.Equal(2, payments.Length);
        Assert.Single(payments.Select(x => x.MovimientoCajaId).Distinct());
        var movementId = payments[0].MovimientoCajaId!.Value;
        var movement = await context.MovimientoCaja.SingleAsync(x => x.Id == movementId);
        Assert.Equal("VENTA", movement.CategoriaMovimiento);
        Assert.Equal(1_000m, movement.Valor);
        Assert.Equal(expectedStock, await context.Producto.Where(x => x.Id == productId)
            .Select(x => x.StockActual).SingleAsync());
        Assert.True(await context.MovimientoInventario.AnyAsync(x => x.VentaId == saleId && x.CantidadStock == -1));
    }

    private async Task AssertIdempotencyInventoryAndAuditAsync(
        Guid saleId, Guid productId, string paymentReference, string idempotencyKey)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await context.Venta.CountAsync(x => x.Id == saleId));
        Assert.Equal(1, await context.OperacionIdempotente.CountAsync(x => x.EntidadId == saleId));
        Assert.Equal(1, await context.MovimientoInventario.CountAsync(x => x.VentaId == saleId && x.ProductoId == productId));
        Assert.Equal(1, await context.EventoAuditoria.CountAsync(x => x.EntidadId == saleId && x.Accion == "SALECONFIRMED"));
        var payment = await context.PagoVenta.SingleAsync(x => x.VentaId == saleId);
        Assert.NotNull(payment.MovimientoCajaId);
        Assert.Equal(1, await context.MovimientoCaja.CountAsync(x => x.Id == payment.MovimientoCajaId));
        var auditRows = await context.EventoAuditoria.AsNoTracking()
            .Select(x => new { x.DatosAnteriores, x.DatosNuevos, x.Motivo })
            .ToArrayAsync();
        var auditText = string.Join('|', auditRows.Select(x =>
            (x.DatosAnteriores ?? string.Empty) + (x.DatosNuevos ?? string.Empty) + (x.Motivo ?? string.Empty)));
        Assert.DoesNotContain(paymentReference, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(idempotencyKey, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(CashierPassword, auditText, StringComparison.Ordinal);
    }

    private async Task<Guid> CreateCustomerAsync(Guid creatorId, bool active, string suffix)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var customer = new Cliente
        {
            Id = Guid.CreateVersion7(),
            TipoDocumento = "CC",
            NumeroDocumento = $"SALE-{suffix}-{Guid.NewGuid():N}"[..40],
            NombreCompleto = $"Cliente ficticio {suffix}",
            Direccion = "Dirección ficticia de prueba",
            Activo = active,
            UsuarioCreacionId = creatorId,
            FechaCreacion = DateTime.UtcNow,
            Version = 1
        };
        context.Cliente.Add(customer);
        await context.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<SalesPersistenceState> CaptureSalesStateAsync(Guid productId, Guid sessionId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var session = await context.SesionOperador.AsNoTracking().SingleAsync(x => x.Id == sessionId);
        return new SalesPersistenceState(
            await context.Venta.CountAsync(),
            await context.DetalleVenta.CountAsync(),
            await context.PagoVenta.CountAsync(),
            await context.MovimientoCaja.CountAsync(),
            await context.MovimientoInventario.CountAsync(),
            await context.EventoAuditoria.CountAsync(),
            await context.OperacionIdempotente.CountAsync(),
            await context.Producto.Where(x => x.Id == productId).Select(x => x.StockActual).SingleAsync(),
            await context.ConsecutivoDocumento.Where(x => x.TipoDocumento == "VENTA")
                .Select(x => x.UltimoNumero).SingleAsync(),
            session.Estado,
            session.FechaUltimoUso,
            session.FechaFin,
            session.MotivoCierre);
    }

    private async Task InstallDeferredSaleFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE ventas.test_sale_commit_observed;
            CREATE FUNCTION ventas.test_fail_sale_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF (SELECT count(*) FROM ventas.detalle_venta WHERE venta_id = NEW.id) = 1
                AND (SELECT count(*) FROM ventas.pago_venta WHERE venta_id = NEW.id) = 1
                AND EXISTS (
                  SELECT 1
                  FROM caja.movimiento_caja m
                  JOIN ventas.pago_venta p ON p.movimiento_caja_id = m.id
                  WHERE p.venta_id = NEW.id AND m.categoria_movimiento = 'VENTA'
                )
                AND EXISTS (SELECT 1 FROM compras.movimiento_inventario WHERE venta_id = NEW.id)
                AND EXISTS (SELECT 1 FROM auditoria.operacion_idempotente WHERE entidad_id = NEW.id)
                AND EXISTS (
                  SELECT 1 FROM auditoria.evento_auditoria
                  WHERE accion = 'SALECONFIRMED' AND entidad_id = NEW.id
                )
                AND EXISTS (
                  SELECT 1 FROM auditoria.evento_auditoria
                  WHERE accion = 'OPERATORSESSIONCLOSED' AND entidad_id = NEW.sesion_operador_id
                )
                AND EXISTS (
                  SELECT 1 FROM caja.sesion_operador
                  WHERE id = NEW.sesion_operador_id AND estado = 'CERRADA'
                )
                AND EXISTS (
                  SELECT 1
                  FROM catalogo.producto p
                  JOIN ventas.detalle_venta d ON d.producto_id = p.id
                  WHERE d.venta_id = NEW.id AND p.stock_actual = 99
                )
              THEN
                PERFORM nextval('ventas.test_sale_commit_observed');
              END IF;
              RAISE EXCEPTION 'Forced isolated sale commit failure' USING ERRCODE = '23514';
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_sale_commit
              AFTER INSERT ON ventas.venta DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION ventas.test_fail_sale_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredSaleFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT is_called FROM ventas.test_sale_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task LockActiveInstallationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM configuracion.instalacion
            WHERE activo
            ORDER BY fecha_instalacion, id
            LIMIT 1
            FOR UPDATE
            """, connection, transaction);
        Assert.NotNull(await command.ExecuteScalarAsync());
    }

    private async Task WaitForInstallationLockWaitersAsync(int expectedCount)
    {
        await using var observer = new NpgsqlConnection(_postgres.GetConnectionString());
        await observer.OpenAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT count(*)
                FROM pg_stat_activity
                WHERE datname = current_database()
                  AND wait_event_type = 'Lock'
                  AND query LIKE '%FROM configuracion.instalacion AS i%'
                  AND query LIKE '%FOR UPDATE%'
                """, observer);
            if ((long)(await command.ExecuteScalarAsync())! >= expectedCount) return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"No se observaron {expectedCount} conexiones esperando el bloqueo de instalación.");
    }

    private static ConfirmSaleRequest CashRequest(
        Guid productId, int quantity, decimal total, Guid? customerId = null) => new(
        customerId, [new(productId, quantity, "MINORISTA")], null, null,
        [new("EFECTIVO", total, total, null)]);

    private static ConfirmSaleRequest DiscountRequest(Guid productId, decimal percentage, decimal total, bool authorized = false) => new(
        null, [new(productId, 1, "MINORISTA", new("PORCENTAJE", percentage))], null,
        "Descuento de prueba", [new("EFECTIVO", total, total, null)],
        Authorization: authorized ? new SaleCreditAuthorizationRequest(SupervisorUser, SupervisorPassword) : null);

    private static async Task<HttpResponseMessage> PostSaleAsync(
        HttpClient client, string idempotencyKey, ConfirmSaleRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/sales")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> PostRawSaleAsync(
        HttpClient client, string idempotencyKey, string json)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/sales")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string userName, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(userName, password)), HttpStatusCode.OK);
    }

    private static async Task<Guid> GetRoleIdAsync(HttpClient client, string code)
    {
        var roles = await ReadAsync<PagedResult<RoleDto>>(
            await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        return roles.Items.Single(x => x.Code == code).Id;
    }

    private static async Task<Guid> GetPermissionIdAsync(HttpClient client, string code)
    {
        var permissions = await ReadAsync<PagedResult<PermissionDto>>(
            await client.GetAsync("/api/permissions?page=1&pageSize=100"), HttpStatusCode.OK);
        return permissions.Items.Single(x => x.Code == code).Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} ({expected}) but received {(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
        var value = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        return value;
    }

    private static Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return Task.CompletedTask;
    }

    private static void SetBearer(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private static string RandomSecret(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private sealed record Scenario(
        AuthenticationResult Admin,
        Users Users,
        Products Products,
        AuthenticationResult Cashier,
        AuthenticationResult Supervisor,
        AuthenticationResult SecondCashier);

    private sealed record Users(UserDto Supervisor, UserDto Cashier, UserDto SecondCashier);

    private sealed record SalesPersistenceState(
        int Sales,
        int Details,
        int Payments,
        int CashMovements,
        int InventoryMovements,
        int Audits,
        int IdempotencyOperations,
        int Stock,
        long Sequence,
        string SessionState,
        DateTime SessionLastUsed,
        DateTime? SessionEnded,
        string? SessionCloseReason);

    private sealed record Products(
        Guid Standard,
        Guid ResidueA,
        Guid ResidueB,
        Guid ResidueC,
        Guid UnknownCost,
        Guid LowStock,
        Guid Inactive,
        Guid UnderCost,
        Guid RoleDiscount,
        Guid ZeroTotal);
}
