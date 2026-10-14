using ControlPlus.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Suppliers.Contracts;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class PurchasesApiFlowTests : IAsyncLifetime
{
    private const string AdminName = "purchases.admin.test";
    private const string SupervisorName = "purchases.supervisor.test";
    private const string CashierName = "purchases.cashier.test";
    private readonly string _masterKey = Secret(48);
    private readonly string _jwtKey = Secret(64);
    private readonly string _adminPassword = $"Purchases-Admin-{Secret(32)}!";
    private readonly string _supervisorPassword = $"Purchases-Supervisor-{Secret(32)}!";
    private readonly string _cashierPassword = $"Purchases-Cashier-{Secret(32)}!";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("controlplus_purchases_api_test")
        .WithUsername("controlplus_test")
        .WithPassword(Secret(32))
        .Build();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await PostgresAvailability.WaitAsync(_postgres);
        await using (var migrations = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
            await migrations.Database.MigrateAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-purchases-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-purchases-client");
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
    public async Task Draft_UsesOnlyEditableFields_AndNeverMovesInventoryOrConsumesNumber()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor de borrador");
        var product = await ProductAsync(client, supplier.Id, "PUR-DRAFT", null);
        var before = await StateAsync(product.Id);

        var draft = await CreateDraftAsync(client, Body(supplier.Id, null, "NO_DISCRIMINADO",
            new Line(product.Id, 2, 12.50m)));
        var id = draft.GetProperty("id").GetGuid();
        Assert.Equal('7', id.ToString("D")[14]);
        Assert.Equal("BORRADOR", draft.GetProperty("status").GetString());
        Assert.Equal(1, draft.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("prefix").ValueKind);
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("series").ValueKind);
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("consecutive").ValueKind);
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("supplierDocumentNumber").ValueKind);
        Assert.Single(draft.GetProperty("lines").EnumerateArray());
        Assert.Equal(before, await StateAsync(product.Id));

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var purchase = await db.Compra.AsNoTracking().SingleAsync(x => x.Id == id);
            var detail = await db.DetalleCompra.AsNoTracking().SingleAsync(x => x.CompraId == id);
            Assert.Null(purchase.PedidoCompraId);
            Assert.Null(detail.DetallePedidoCompraId);
            Assert.Equal(25m, detail.SubtotalBruto);
            Assert.Equal(12.50m, detail.CostoUnitarioDocumento);
            Assert.Equal(25m, purchase.Total);
            Assert.Equal(0, await db.ConsecutivoDocumento.CountAsync(x => x.TipoDocumento == "COMPRA"));
            Assert.Equal(0, await db.MovimientoInventario.CountAsync(x => x.CompraId == id));
            var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x => x.EntidadId == id);
            AssertAuditSanitized(audit.DatosAnteriores, audit.DatosNuevos, audit.Motivo);
        }

        var baseBody = Body(supplier.Id, null, "NO_DISCRIMINADO", new Line(product.Id, 1, 10m));
        foreach (var invalid in new[]
        {
            Body(Guid.Empty, null, "NO_DISCRIMINADO", new Line(product.Id, 1, 10m)),
            Body(supplier.Id, null, "NO_DISCRIMINADO", new Line(product.Id, 0, 10m)),
            Body(supplier.Id, null, "NO_DISCRIMINADO", new Line(product.Id, 1, 0m)),
            Body(supplier.Id, null, "NO_DISCRIMINADO", new Line(product.Id, 1, 10m), new Line(product.Id, 2, 10m))
        })
            await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", invalid), HttpStatusCode.BadRequest);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases",
            Body(Guid.NewGuid(), null, "NO_DISCRIMINADO", new Line(product.Id, 1, 10m))), HttpStatusCode.NotFound);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases",
            Body(supplier.Id, null, "NO_DISCRIMINADO", new Line(Guid.NewGuid(), 1, 10m))), HttpStatusCode.NotFound);

        foreach (var forbidden in new[] { "purchaseId", "orderId", "prefix", "series", "consecutive", "status", "discount", "discountAmount", "discountPercentage", "stock", "audit" })
        {
            var payload = new Dictionary<string, object?>
            {
                ["supplierId"] = supplier.Id,
                ["invoiceDate"] = "2026-10-13",
                ["vatMode"] = "NO_DISCRIMINADO",
                ["lines"] = new[] { new { productId = product.Id, quantity = 1, unitCost = 10m } },
                [forbidden] = "client-value"
            };
            await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", payload), HttpStatusCode.BadRequest);
        }
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", new
        {
            baseBody.SupplierId, baseBody.InvoiceDate, baseBody.VatMode,
            lines = new[] { new { productId = product.Id, quantity = 1, unitCost = 10m, discount = 1m } }
        }), HttpStatusCode.BadRequest);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", new
        {
            supplierId = supplier.Id, invoiceDate = "not-a-date", vatMode = "NO_DISCRIMINADO",
            lines = new[] { new { productId = product.Id, quantity = 1, unitCost = 10m } }
        }), HttpStatusCode.BadRequest);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/api/products/{product.Id}/deactivate", null)).StatusCode);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", baseBody), HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/api/products/{product.Id}/activate", null)).StatusCode);
        var supplierCurrent = await ReadAsync<SupplierDto>(await client.GetAsync($"/api/suppliers/{supplier.Id}"), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/status",
            new SetSupplierStatusRequest(false, supplierCurrent.Version))).StatusCode);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", baseBody), HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ListEditAndPermissions_RespectStablePagingVersionsRoleAndHybridOverrides()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor de listado");
        var otherSupplier = await SupplierAsync(client, "Otro proveedor");
        var product = await ProductAsync(client, supplier.Id, "PUR-LIST", null);
        var first = await CreateDraftAsync(client, Body(supplier.Id, "DOC-A", "NO_DISCRIMINADO",
            new Line(product.Id, 1, 10m), date: new DateOnly(2026, 10, 12)));
        var second = await CreateDraftAsync(client, Body(supplier.Id, "DOC-B", "NO_DISCRIMINADO",
            new Line(product.Id, 1, 11m), date: new DateOnly(2026, 10, 13)));
        _ = await CreateDraftAsync(client, Body(otherSupplier.Id, "DOC-C", "NO_DISCRIMINADO",
            new Line(product.Id, 1, 12m), date: new DateOnly(2026, 10, 14)));
        var firstId = first.GetProperty("id").GetGuid();
        var secondId = second.GetProperty("id").GetGuid();

        var filtered = await ReadJsonAsync(await client.GetAsync($"/api/purchases?supplierId={supplier.Id}&status=BORRADOR&page=1&pageSize=10"), HttpStatusCode.OK);
        Assert.Equal(new[] { secondId, firstId }, filtered.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()));
        var exact = await ReadJsonAsync(await client.GetAsync("/api/purchases?invoiceDate=2026-10-13&supplierDocumentNumber=DOC-B"), HttpStatusCode.OK);
        Assert.Equal(secondId, Assert.Single(exact.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var pageOne = await ReadJsonAsync(await client.GetAsync("/api/purchases?page=1&pageSize=1"), HttpStatusCode.OK);
        var pageTwo = await ReadJsonAsync(await client.GetAsync("/api/purchases?page=2&pageSize=1"), HttpStatusCode.OK);
        Assert.NotEqual(Assert.Single(pageOne.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid(),
            Assert.Single(pageTwo.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var repeatedPage = await ReadJsonAsync(await client.GetAsync("/api/purchases?page=1&pageSize=1"), HttpStatusCode.OK);
        Assert.Equal(pageOne.GetProperty("items").GetRawText(), repeatedPage.GetProperty("items").GetRawText());
        await ProblemAsync(await client.GetAsync("/api/purchases?page=2147483647&pageSize=100"), HttpStatusCode.BadRequest);
        await ProblemAsync(await client.GetAsync("/api/purchases?page=0"), HttpStatusCode.BadRequest);
        await ProblemAsync(await client.GetAsync("/api/purchases?pageSize=101"), HttpStatusCode.BadRequest);

        var updated = await ReadJsonAsync(await client.PutAsJsonAsync($"/api/purchases/{firstId}",
            MakeUpdateBody(supplier.Id, "DOC-A-EDIT", "NO_DISCRIMINADO", 1,
                new Line(product.Id, 3, 9m), date: new DateOnly(2026, 10, 12))), HttpStatusCode.OK);
        Assert.Equal(2, updated.GetProperty("version").GetInt64());
        await ProblemAsync(await client.PutAsJsonAsync($"/api/purchases/{firstId}",
            MakeUpdateBody(supplier.Id, "DOC-A-STALE", "NO_DISCRIMINADO", 1,
                new Line(product.Id, 3, 9m))), HttpStatusCode.Conflict);
        var persisted = await ReadJsonAsync(await client.GetAsync($"/api/purchases/{firstId}"), HttpStatusCode.OK);
        Assert.Equal("DOC-A-EDIT", persisted.GetProperty("supplierDocumentNumber").GetString());
        Assert.Equal(2, persisted.GetProperty("version").GetInt64());
        await ProblemAsync(await client.PutAsJsonAsync($"/api/purchases/{firstId}", new
        {
            supplierId = supplier.Id, invoiceDate = "2026-10-13", vatMode = "NO_DISCRIMINADO",
            lines = new[] { new { productId = product.Id, quantity = 1, unitCost = 1m } },
            expectedVersion = 2, orderId = Guid.NewGuid()
        }), HttpStatusCode.BadRequest);

        client.DefaultRequestHeaders.Authorization = null;
        await ProblemAsync(await client.GetAsync("/api/purchases"), HttpStatusCode.Unauthorized);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", Body(supplier.Id, null,
            "NO_DISCRIMINADO", new Line(product.Id, 1, 10m))), HttpStatusCode.Unauthorized);
        Bearer(client, users.Cashier.Token.AccessToken);
        await ProblemAsync(await client.GetAsync("/api/purchases"), HttpStatusCode.Forbidden);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", Body(supplier.Id, null,
            "NO_DISCRIMINADO", new Line(product.Id, 1, 10m))), HttpStatusCode.Forbidden);
        Bearer(client, users.Supervisor.Token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/purchases")).StatusCode);
        await CreateDraftAsync(client, Body(supplier.Id, "SUPERVISOR-DOC", "NO_DISCRIMINADO",
            new Line(product.Id, 1, 10m)));

        Bearer(client, users.Admin.Token.AccessToken);
        var receivePermission = await PermissionIdAsync(client, PermissionCodes.PurchasesReceive);
        await SetPermissionAsync(client, users.Supervisor.User.Id, receivePermission, false);
        var supervisor = await LoginAsync(client, SupervisorName, _supervisorPassword);
        Bearer(client, supervisor.AccessToken);
        await ProblemAsync(await client.GetAsync("/api/purchases"), HttpStatusCode.Forbidden);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", Body(supplier.Id, null,
            "NO_DISCRIMINADO", new Line(product.Id, 1, 10m))), HttpStatusCode.Forbidden);

        Bearer(client, users.Admin.Token.AccessToken);
        await SetPermissionAsync(client, users.Supervisor.User.Id, receivePermission, true);
        supervisor = await LoginAsync(client, SupervisorName, _supervisorPassword);
        Bearer(client, supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/purchases")).StatusCode);
        Bearer(client, users.Admin.Token.AccessToken);
        await SetPermissionAsync(client, users.Cashier.User.Id, receivePermission, true);
        var cashier = await LoginAsync(client, CashierName, _cashierPassword);
        Bearer(client, cashier.AccessToken);
        await ProblemAsync(await client.GetAsync("/api/purchases"), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"/api/purchases/{firstId}")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Confirm_ReceivesEveryProductOnce_UsesNetCostsAndTaxWithoutDoubleCounting()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor de recepción");
        var stockProduct = await ProductAsync(client, supplier.Id, "PUR-WEIGHTED", 19m);
        var zeroProduct = await ProductAsync(client, supplier.Id, "PUR-ZERO", 19m);
        var unknownAverageProduct = await ProductAsync(client, supplier.Id, "PUR-UNKNOWN-AVG", 19m);
        await SeedStockAsync(stockProduct.Id, 5, 10m);
        await SeedStockAsync(unknownAverageProduct.Id, 4, null);

        await ProblemAsync(await client.GetAsync("/api/document-numbering/purchases"), HttpStatusCode.NotFound);
        var numbering = await ReadJsonAsync(await client.PostAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = " cmp ", firstNumber = 1 }), HttpStatusCode.Created);
        Assert.Equal("COMPRA", numbering.GetProperty("documentType").GetString());
        Assert.Equal("CMP", numbering.GetProperty("prefix").GetString());
        Assert.Equal(0, numbering.GetProperty("lastNumber").GetInt64());
        var draft = await CreateDraftAsync(client, Body(supplier.Id, null, "SEPARADO",
            new Line(stockProduct.Id, 3, 12.50m, 19m),
            new Line(zeroProduct.Id, 2, 7m, 19m),
            new Line(unknownAverageProduct.Id, 2, 5m, 19m)));
        var id = draft.GetProperty("id").GetGuid();
        var before = await StateAsync(stockProduct.Id);
        await ProblemAsync(await ConfirmAsync(client, id, null, draft.GetProperty("version").GetInt64()), HttpStatusCode.BadRequest);
        await ProblemAsync(await ConfirmAsync(client, id, "purchase-confirm-missing-version", null), HttpStatusCode.BadRequest);
        Assert.Equal(before, await StateAsync(stockProduct.Id));

        const string key = "purchase-confirm-weighted";
        var confirmed = await ReadJsonAsync(await ConfirmAsync(client, id, key,
            draft.GetProperty("version").GetInt64()), HttpStatusCode.OK);
        Assert.Equal("CONFIRMADA", confirmed.GetProperty("status").GetString());
        Assert.Equal(1, confirmed.GetProperty("consecutive").GetInt64());
        Assert.Equal("CMP", confirmed.GetProperty("prefix").GetString());
        var internalDocument = confirmed.GetProperty("supplierDocumentNumber").GetString();
        Assert.StartsWith("CP-INTERNO-", internalDocument, StringComparison.Ordinal);
        Assert.Contains("CMP", internalDocument, StringComparison.Ordinal);
        Assert.Contains("1", internalDocument, StringComparison.Ordinal);
        Assert.True(internalDocument!.Length <= 100);
        var replay = await ReadJsonAsync(await ConfirmAsync(client, id, key,
            draft.GetProperty("version").GetInt64()), HttpStatusCode.OK);
        Assert.Equal(confirmed.GetProperty("id").GetGuid(), replay.GetProperty("id").GetGuid());
        Assert.Equal(confirmed.GetProperty("version").GetInt64(), replay.GetProperty("version").GetInt64());
        await ProblemAsync(await ConfirmAsync(client, id, key, confirmed.GetProperty("version").GetInt64()), HttpStatusCode.Conflict);
        await ProblemAsync(await ConfirmAsync(client, id, "purchase-confirm-second-key",
            draft.GetProperty("version").GetInt64()), HttpStatusCode.Conflict);
        await ProblemAsync(await client.PutAsJsonAsync($"/api/purchases/{id}", MakeUpdateBody(supplier.Id,
            "EDIT-CONFIRMED", "SEPARADO", confirmed.GetProperty("version").GetInt64(),
            new Line(stockProduct.Id, 1, 12.50m, 19m))), HttpStatusCode.Conflict);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var purchase = await db.Compra.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Null(purchase.PedidoCompraId);
            Assert.Equal(internalDocument, purchase.NumeroDocumentoProveedor);
            Assert.Equal(1, purchase.Consecutivo);
            var details = await db.DetalleCompra.AsNoTracking().Where(x => x.CompraId == id).ToArrayAsync();
            var movements = await db.MovimientoInventario.AsNoTracking().Where(x => x.CompraId == id).ToArrayAsync();
            Assert.Equal(3, details.Length);
            Assert.Equal(3, movements.Length);
            Assert.Equal(3, movements.Select(x => x.ProductoId).Distinct().Count());
            Assert.All(movements, x => Assert.Equal("COMPRA", x.TipoMovimiento));
            var weighted = details.Single(x => x.ProductoId == stockProduct.Id);
            var weightedMovement = movements.Single(x => x.ProductoId == stockProduct.Id);
            var weightedStock = await db.Producto.AsNoTracking().SingleAsync(x => x.Id == stockProduct.Id);
            Assert.Equal(37.50m, weighted.SubtotalBruto);
            Assert.Equal(7.125m, weighted.ValorIva);
            Assert.Equal(10m, weighted.CostoPromedioAnterior);
            Assert.Equal(11.8281m, weighted.CostoPromedioResultante);
            Assert.Equal(8, weightedStock.StockActual);
            Assert.Equal(11.8281m, weightedStock.CostoPromedio);
            Assert.Equal(5, weightedMovement.StockAnterior);
            Assert.Equal(8, weightedMovement.StockPosterior);
            Assert.Equal(weighted.CostoPromedioAnterior, weightedMovement.CostoPromedioAnterior);
            Assert.Equal(weighted.CostoPromedioResultante, weightedMovement.CostoPromedioPosterior);
            var zero = details.Single(x => x.ProductoId == zeroProduct.Id);
            Assert.Null(zero.CostoPromedioAnterior);
            Assert.Equal(8.33m, zero.CostoPromedioResultante);
            Assert.Equal(2, (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == zeroProduct.Id)).StockActual);
            var unknown = details.Single(x => x.ProductoId == unknownAverageProduct.Id);
            Assert.Null(unknown.CostoPromedioAnterior);
            Assert.Equal(5.95m, unknown.CostoPromedioResultante);
            Assert.Equal(6, (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == unknownAverageProduct.Id)).StockActual);
            Assert.Equal(1, await db.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "CONFIRMAR_COMPRA"));
            foreach (var audit in await db.EventoAuditoria.AsNoTracking().Where(x => x.EntidadId == id).ToArrayAsync())
                AssertAuditSanitized(audit.DatosAnteriores, audit.DatosNuevos, audit.Motivo);
        }

        var externalDraft = await CreateDraftAsync(client, Body(supplier.Id, " ext-001 ", "INCLUIDO",
            new Line(stockProduct.Id, 2, 11.90m, 19m)));
        var external = await ReadJsonAsync(await ConfirmAsync(client,
            externalDraft.GetProperty("id").GetGuid(), "purchase-confirm-included",
            externalDraft.GetProperty("version").GetInt64()), HttpStatusCode.OK);
        Assert.Equal("EXT-001", external.GetProperty("supplierDocumentNumber").GetString());
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var line = await db.DetalleCompra.AsNoTracking().SingleAsync(x => x.CompraId == external.GetProperty("id").GetGuid());
            Assert.Equal(23.80m, line.TotalLinea);
            Assert.Equal(11.8281m, line.CostoPromedioAnterior);
            Assert.Equal(11.8425m, line.CostoPromedioResultante);
            Assert.Equal(11.8425m, (await db.Producto.AsNoTracking().SingleAsync(x => x.Id == stockProduct.Id)).CostoPromedio);
        }
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", Body(supplier.Id,
            "EXT-001", "INCLUIDO", new Line(stockProduct.Id, 1, 11.90m, 19m))), HttpStatusCode.Conflict);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", Body(supplier.Id,
            internalDocument, "INCLUIDO", new Line(stockProduct.Id, 1, 11.90m, 19m))), HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentConfirmAndDeferredCommitFailure_PreserveSingleReceiptAndGaplessNumbering()
    {
        using var firstClient = Client();
        using var secondClient = Client();
        var users = await BootstrapAsync(firstClient);
        Bearer(firstClient, users.Admin.Token.AccessToken);
        Bearer(secondClient, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(firstClient, "Proveedor de concurrencia");
        var product = await ProductAsync(firstClient, supplier.Id, "PUR-RACE", null);
        _ = await ReadJsonAsync(await firstClient.PostAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 1 }), HttpStatusCode.Created);
        var draft = await CreateDraftAsync(firstClient, Body(supplier.Id, null, "NO_DISCRIMINADO",
            new Line(product.Id, 2, 10m)));
        var id = draft.GetProperty("id").GetGuid();
        var version = draft.GetProperty("version").GetInt64();

        var responses = await Task.WhenAll(
            ConfirmAsync(firstClient, id, "purchase-race-a", version),
            ConfirmAsync(secondClient, id, "purchase-race-b", version));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        await ProblemAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict);
        Assert.Equal(2, (await StateAsync(product.Id)).Stock);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.Equal(1, await db.MovimientoInventario.CountAsync(x => x.CompraId == id));
            Assert.Equal(1, await db.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "CONFIRMAR_COMPRA"));
        }

        var failedDraft = await CreateDraftAsync(firstClient, Body(supplier.Id, null, "NO_DISCRIMINADO",
            new Line(product.Id, 3, 20m)));
        var failedId = failedDraft.GetProperty("id").GetGuid();
        var before = await StateAsync(product.Id);
        await InstallDeferredCommitFailureAsync(failedId);
        await ProblemAsync(await ConfirmAsync(firstClient, failedId, "purchase-deferred-failure",
            failedDraft.GetProperty("version").GetInt64()), HttpStatusCode.Conflict);
        Assert.True(await DeferredFailureObservedAsync());
        Assert.Equal(before, await StateAsync(product.Id));
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var reverted = await db.Compra.AsNoTracking().SingleAsync(x => x.Id == failedId);
            Assert.Equal("BORRADOR", reverted.Estado);
            Assert.Null(reverted.Consecutivo);
            Assert.Null(reverted.NumeroDocumentoProveedor);
            Assert.Equal(0, await db.MovimientoInventario.CountAsync(x => x.CompraId == failedId));
            Assert.Equal(1, await db.ConsecutivoDocumento.Where(x => x.TipoDocumento == "COMPRA")
                .Select(x => x.UltimoNumero).SingleAsync());
        }
        await RemoveDeferredCommitFailureAsync();
        var recovered = await ReadJsonAsync(await ConfirmAsync(firstClient, failedId, "purchase-deferred-failure",
            failedDraft.GetProperty("version").GetInt64()), HttpStatusCode.OK);
        Assert.Equal(2, recovered.GetProperty("consecutive").GetInt64());
        Assert.Equal(before.Stock + 3, (await StateAsync(product.Id)).Stock);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PurchaseNumbering_RequiresManagePermissionAndRejectsLateOrUnknownMutations()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Supervisor.Token.AccessToken);
        await ProblemAsync(await client.GetAsync("/api/document-numbering/purchases"), HttpStatusCode.Forbidden);
        await ProblemAsync(await client.PostAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 1 }), HttpStatusCode.Forbidden);
        Bearer(client, users.Cashier.Token.AccessToken);
        await ProblemAsync(await client.GetAsync("/api/document-numbering/purchases"), HttpStatusCode.Forbidden);
        Bearer(client, users.Admin.Token.AccessToken);
        await ProblemAsync(await client.GetAsync("/api/document-numbering/purchases"), HttpStatusCode.NotFound);
        await ProblemAsync(await client.PostAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 1, documentType = "COMPRA" }), HttpStatusCode.BadRequest);
        var created = await ReadJsonAsync(await client.PostAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 1 }), HttpStatusCode.Created);
        Assert.Equal(0, created.GetProperty("lastNumber").GetInt64());
        Assert.Equal(1, created.GetProperty("nextNumber").GetInt64());
        await ProblemAsync(await client.PostAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 1 }), HttpStatusCode.Conflict);
        var changed = await ReadJsonAsync(await client.PutAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 2, version = created.GetProperty("version").GetInt64() }), HttpStatusCode.OK);
        Assert.Equal(2, changed.GetProperty("nextNumber").GetInt64());
        await ProblemAsync(await client.PutAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 3, version = created.GetProperty("version").GetInt64() }), HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.DeleteAsync("/api/document-numbering/purchases")).StatusCode);
    }

    private async Task<Users> BootstrapAsync(HttpClient client)
    {
        using var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest("Compras Test", "PURCHASES-TEST",
                "PURCHASES-INSTALL", "PURCHASES1", "PURCHASES-TERM", "Terminal", AdminName,
                "Administrador Compras", _adminPassword))
        };
        setup.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setup)).StatusCode);
        var admin = await LoginAsync(client, AdminName, _adminPassword);
        Bearer(client, admin.AccessToken);
        var adminUser = (await ReadAsync<PagedResult<UserDto>>(await client.GetAsync("/api/users?pageSize=100"), HttpStatusCode.OK))
            .Items.Single(x => x.UserName == AdminName);
        var roles = await ReadAsync<PagedResult<RoleDto>>(await client.GetAsync("/api/roles?pageSize=100"), HttpStatusCode.OK);
        var supervisorRole = roles.Items.Single(x => x.Code == RoleCodes.Supervisor).Id;
        var cashierRole = roles.Items.Single(x => x.Code == RoleCodes.Cashier).Id;
        var supervisorUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorName, "Supervisor Compras", _supervisorPassword, [supervisorRole])), HttpStatusCode.Created);
        var cashierUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierName, "Cajero Compras", _cashierPassword, [cashierRole])), HttpStatusCode.Created);
        return new Users(new AuthUser(admin, adminUser),
            new AuthUser(await LoginAsync(client, SupervisorName, _supervisorPassword), supervisorUser),
            new AuthUser(await LoginAsync(client, CashierName, _cashierPassword), cashierUser));
    }

    private async Task<SupplierDto> SupplierAsync(HttpClient client, string name) =>
        await ReadAsync<SupplierDto>(await client.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest(null, null, name, null, null, null, null)), HttpStatusCode.Created);

    private async Task<ProductDto> ProductAsync(HttpClient client, Guid supplierId, string code, decimal? taxPercentage)
    {
        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest($"Categoría {code}", null)), HttpStatusCode.Created);
        var units = await ReadAsync<MeasurementUnitDto[]>(await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        return await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, units.Single(x => x.Code == "UNIDAD").Id,
                supplierId, code, null, null, true, $"Producto {code}", null, 1000m,
                null, taxPercentage, 0)), HttpStatusCode.Created);
    }

    private async Task SeedStockAsync(Guid productId, int stock, decimal? averageCost)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await db.Producto.SingleAsync(x => x.Id == productId);
        product.StockActual = stock;
        product.CostoPromedio = averageCost;
        await db.SaveChangesAsync();
    }

    private async Task<(int Stock, decimal? AverageCost, long Version, int Movements, long? LastNumber)> StateAsync(Guid productId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await db.Producto.AsNoTracking().SingleAsync(x => x.Id == productId);
        var number = await db.ConsecutivoDocumento.AsNoTracking().Where(x => x.TipoDocumento == "COMPRA")
            .Select(x => (long?)x.UltimoNumero).SingleOrDefaultAsync();
        return (product.StockActual, product.CostoPromedio, product.Version,
            await db.MovimientoInventario.CountAsync(x => x.ProductoId == productId), number);
    }

    private async Task InstallDeferredCommitFailureAsync(Guid purchaseId)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE SEQUENCE auditoria.test_purchase_commit_observed;
            CREATE FUNCTION auditoria.test_fail_purchase_commit() RETURNS trigger LANGUAGE plpgsql AS $trigger$
            BEGIN
              IF NEW.entidad_id = '{{purchaseId}}'::uuid THEN
                PERFORM nextval('auditoria.test_purchase_commit_observed');
                RAISE EXCEPTION 'forced purchase commit failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $trigger$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_purchase_commit AFTER INSERT ON auditoria.evento_auditoria
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION auditoria.test_fail_purchase_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT is_called FROM auditoria.test_purchase_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task RemoveDeferredCommitFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            DROP TRIGGER zz_test_fail_purchase_commit ON auditoria.evento_auditoria;
            DROP FUNCTION auditoria.test_fail_purchase_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static CreateBody Body(Guid supplierId, string? document, string vatMode, Line first,
        Line? second = null, Line? third = null, DateOnly? date = null) =>
        new(supplierId, document, date ?? new DateOnly(2026, 10, 13), vatMode,
            new[] { first, second, third }.Where(x => x is not null).Cast<Line>().ToArray());

    private static UpdateBody MakeUpdateBody(Guid supplierId, string? document, string vatMode, long version,
        Line first, DateOnly? date = null) =>
        new(supplierId, document, date ?? new DateOnly(2026, 10, 13), vatMode, [first], version);

    private static async Task<JsonElement> CreateDraftAsync(HttpClient client, CreateBody body) =>
        await ReadJsonAsync(await client.PostAsJsonAsync("/api/purchases", body), HttpStatusCode.Created);

    private static async Task<HttpResponseMessage> ConfirmAsync(HttpClient client, Guid purchaseId, string? key, long? version)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/purchases/{purchaseId}/confirm")
        {
            Content = JsonContent.Create<object>(version.HasValue ? new { expectedVersion = version.Value } : new { })
        };
        if (key is not null) message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string name, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(name, password)), HttpStatusCode.OK);
    }

    private static async Task<Guid> PermissionIdAsync(HttpClient client, string code) =>
        (await ReadAsync<PagedResult<PermissionDto>>(await client.GetAsync("/api/permissions?pageSize=100"),
            HttpStatusCode.OK)).Items.Single(x => x.Code == code).Id;

    private static async Task SetPermissionAsync(HttpClient client, Guid userId, Guid permissionId, bool grant) =>
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{userId}/permissions/{permissionId}", new SetUserPermissionRequest(grant))).StatusCode);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var parsed = JsonDocument.Parse(body);
        return parsed.RootElement.Clone();
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var parsed = JsonDocument.Parse(body);
        Assert.True(parsed.RootElement.TryGetProperty("code", out _));
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertAuditSanitized(string? previous, string? current, string? reason)
    {
        var auditText = string.Join('|', previous, current, reason);
        Assert.DoesNotContain("EXT-001", auditText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CP-INTERNO-", auditText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Proveedor de", auditText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unitCost", auditText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("12.50", auditText, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient Client() => _factory!.CreateClient(new WebApplicationFactoryClientOptions
    { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static void Bearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string Secret(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private sealed record Line(Guid ProductId, int Quantity, decimal UnitCost, decimal? VatPercentage = null);
    private sealed record CreateBody(Guid SupplierId, string? SupplierDocumentNumber, DateOnly InvoiceDate,
        string VatMode, Line[] Lines);
    private sealed record UpdateBody(Guid SupplierId, string? SupplierDocumentNumber, DateOnly InvoiceDate,
        string VatMode, Line[] Lines, long ExpectedVersion);
    private sealed record AuthUser(AuthenticationResult Token, UserDto User);
    private sealed record Users(AuthUser Admin, AuthUser Supervisor, AuthUser Cashier);
}
