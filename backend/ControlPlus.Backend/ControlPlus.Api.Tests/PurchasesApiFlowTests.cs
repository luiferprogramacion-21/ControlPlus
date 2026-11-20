using ControlPlus.Tests;
using System.Collections.Concurrent;
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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class PurchasesApiFlowTests : IAsyncLifetime
{
    private const string AdminName = "purchases.admin.test";
    private const string SupervisorName = "purchases.supervisor.test";
    private const string CashierName = "purchases.cashier.test";
    private readonly string _masterKey = Secret(48);
    private readonly string _jwtKey = Secret(64);
    private readonly string _adminPassword = $"Purchases-Admin-{Secret(32)}!";
    private readonly string _supervisorPassword = $"Purchases-Supervisor-{Secret(32)}!";
    private readonly string _cashierPassword = $"Purchases-Cashier-{Secret(32)}!";
    private readonly ConcurrentQueue<string> _capturedLogs = new();
    private int _categorySequence;
    private LocalPostgresDatabase _postgres = NewPostgres();
    private WebApplicationFactory<Program>? _factory;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PurchaseOrder_PartialReceiptsReplayAndAutomaticCompletion_AreAtomic()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor de pedido parcial");
        var first = await ProductAsync(client, supplier.Id, "ORD-PART-A", null);
        var second = await ProductAsync(client, supplier.Id, "ORD-PART-B", null);
        // The installation created the PED and CMP consecutives by itself.
        var orderNumbering = await ReadJsonAsync(
            await client.GetAsync("/api/purchase-orders/numbering"), HttpStatusCode.OK);
        Assert.Equal("PED", orderNumbering.GetProperty("prefix").GetString());
        Assert.Equal(1, orderNumbering.GetProperty("nextNumber").GetInt64());
        var body = new { supplierId = supplier.Id, lines = new[]
        {
            new { productId = first.Id, requestedQuantity = 4, estimatedUnitCost = 10m },
            new { productId = second.Id, requestedQuantity = 2, estimatedUnitCost = 12m }
        } };
        var order = await ReadJsonAsync(await client.PostAsJsonAsync("/api/purchase-orders", body), HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();
        var orderedProduct = await ReadAsync<ProductDto>(
            await client.GetAsync($"/api/products/{first.Id}"), HttpStatusCode.OK);
        using (var blocked = await client.DeleteAsync(
            $"/api/products/{first.Id}?expectedVersion={orderedProduct.Version}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("solo puede inactivarse", await blocked.Content.ReadAsStringAsync());
        }
        Assert.Equal("PED-PURCHASES1-1", order.GetProperty("visibleNumber").GetString());
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchase-orders", body), HttpStatusCode.Conflict);
        var sent = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{orderId}/send",
            new { expectedVersion = 1 }), HttpStatusCode.OK);
        Assert.Equal("ENVIADO", sent.GetProperty("status").GetString());
        var byProduct = sent.GetProperty("lines").EnumerateArray()
            .ToDictionary(x => x.GetProperty("productId").GetGuid(), x => x.GetProperty("id").GetGuid());
        var firstReceipt = new { expectedOrderVersion = 2, invoiceDate = "2026-10-16",
            vatMode = "NO_DISCRIMINADO", lines = new[]
            { new { orderLineId = byProduct[first.Id], productId = first.Id, quantity = 2, unitCost = 10m } } };
        var partial = await ReadJsonAsync(await ReceiveOrderAsync(client, orderId, "partial-1", firstReceipt), HttpStatusCode.Created);
        Assert.Equal("RECIBIDO_PARCIAL", partial.GetProperty("orderStatus").GetString());
        Assert.Equal(3, partial.GetProperty("orderVersion").GetInt64());
        Assert.Equal("CONFIRMADA", partial.GetProperty("purchase").GetProperty("status").GetString());
        var beforeReplay = await StateAsync(first.Id);
        var replay = await ReadJsonAsync(await ReceiveOrderAsync(client, orderId, "partial-1", firstReceipt), HttpStatusCode.Created);
        Assert.Equal(partial.GetRawText(), replay.GetRawText());
        Assert.Equal(beforeReplay, await StateAsync(first.Id));
        var secondReceipt = new { expectedOrderVersion = 3, invoiceDate = "2026-10-16",
            vatMode = "NO_DISCRIMINADO", lines = new[]
            {
                new { orderLineId = byProduct[first.Id], productId = first.Id, quantity = 2, unitCost = 10m },
                new { orderLineId = byProduct[second.Id], productId = second.Id, quantity = 2, unitCost = 12m }
            } };
        var completed = await ReadJsonAsync(await ReceiveOrderAsync(client, orderId, "partial-2", secondReceipt), HttpStatusCode.Created);
        Assert.Equal("COMPLETADO", completed.GetProperty("orderStatus").GetString());
        Assert.Equal(4, completed.GetProperty("orderVersion").GetInt64());
        await ProblemAsync(await ReceiveOrderAsync(client, orderId, "partial-3", secondReceipt), HttpStatusCode.Conflict);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persisted = await db.PedidoCompra.AsNoTracking().SingleAsync(x => x.Id == orderId);
        Assert.Equal("COMPLETADO", persisted.Estado);
        Assert.Equal(users.Admin.User.Id, persisted.UsuarioCierreId);
        Assert.Equal(2, await db.Compra.CountAsync(x => x.PedidoCompraId == orderId));
        Assert.Equal(3, await db.DetalleCompra.CountAsync(x => x.Compra.PedidoCompraId == orderId));
        Assert.Equal(3, await db.MovimientoInventario.CountAsync(x => x.Compra != null && x.Compra.PedidoCompraId == orderId));
        Assert.Equal(2, (await db.ConsecutivoDocumento.SingleAsync(x => x.TipoDocumento == "COMPRA")).UltimoNumero);
    }

    private static async Task<HttpResponseMessage> ReceiveOrderAsync(HttpClient client, Guid orderId,
        string key, object request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/purchase-orders/{orderId}/receipts")
            { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task PurchaseOrder_ExceptionRequiresCurrentAdministratorAndEarlyClosePreservesPendingHistory(
        bool administratorCloses)
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor de excepciones");
        var requested = await ProductAsync(client, supplier.Id, "ORD-REQ", null);
        var substitute = await ProductAsync(client, supplier.Id, "ORD-SUB", null);
        var pending = await ProductAsync(client, supplier.Id, "ORD-PENDING", null);
        var reasons = await ReadJsonAsync(await client.PostAsync(
            "/api/purchase-orders/reasons/close/initialize", null), HttpStatusCode.OK);
        var closeReason = reasons.EnumerateArray().Single(x => x.GetProperty("code").GetString() == "PENDIENTE_NO_REQUERIDO")
            .GetProperty("id").GetGuid();
        var order = await ReadJsonAsync(await client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = supplier.Id, lines = new[]
            {
                new { productId = requested.Id, requestedQuantity = 2 },
                new { productId = pending.Id, requestedQuantity = 2 }
            }
        }), HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();
        var sent = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{orderId}/send",
            new { expectedVersion = 1 }), HttpStatusCode.OK);
        var lineId = sent.GetProperty("lines").EnumerateArray().Single(x =>
            x.GetProperty("productId").GetGuid() == requested.Id).GetProperty("id").GetGuid();
        var before = await StateAsync(substitute.Id);
        var invalid = new { expectedOrderVersion = 2, invoiceDate = "2026-10-16", vatMode = "NO_DISCRIMINADO",
            lines = new[] { new { orderLineId = lineId, productId = substitute.Id, quantity = 3, unitCost = 9m } },
            authorization = new { userName = AdminName, password = _adminPassword + "incorrecto" } };
        await ProblemAsync(await ReceiveOrderAsync(client, orderId, "excess-wrong", invalid), HttpStatusCode.Unauthorized);
        Assert.Equal(before, await StateAsync(substitute.Id));
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.Equal(0, await db.Compra.CountAsync(x => x.PedidoCompraId == orderId));
            Assert.Equal(0, (await db.ConsecutivoDocumento.SingleAsync(x => x.TipoDocumento == "COMPRA")).UltimoNumero);
        }
        var valid = new { expectedOrderVersion = 2, invoiceDate = "2026-10-16", vatMode = "NO_DISCRIMINADO",
            lines = new[] { new { orderLineId = lineId, productId = substitute.Id, quantity = 3, unitCost = 9m } } };
        var receipt = await ReadJsonAsync(await ReceiveOrderAsync(client, orderId, "excess-valid", valid), HttpStatusCode.Created);
        Assert.Equal("RECIBIDO_PARCIAL", receipt.GetProperty("orderStatus").GetString());
        Assert.Equal(3, (await StateAsync(substitute.Id)).Stock - before.Stock);
        var lineAfter = (await ReadJsonAsync(await client.GetAsync($"/api/purchase-orders/{orderId}"), HttpStatusCode.OK))
            .GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == lineId);
        Assert.Equal(3, lineAfter.GetProperty("receivedQuantity").GetInt32());
        Assert.Equal(1, lineAfter.GetProperty("excessQuantity").GetInt32());
        await ProblemAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{orderId}/cancel",
            new { expectedVersion = 3, reasonId = Guid.NewGuid(),
                authorization = new { userName = AdminName, password = _adminPassword } }), HttpStatusCode.Conflict);
        Bearer(client, administratorCloses ? users.Admin.Token.AccessToken : users.Supervisor.Token.AccessToken);
        var closeResponse = administratorCloses
            ? await client.PostAsJsonAsync($"/api/purchase-orders/{orderId}/close",
                new { expectedVersion = 3, reasonId = closeReason, observation = "Resto no requerido" })
            : await client.PostAsJsonAsync($"/api/purchase-orders/{orderId}/close",
                new { expectedVersion = 3, reasonId = closeReason, observation = "Resto no requerido",
                    authorization = new { userName = AdminName, password = _adminPassword } });
        var closed = await ReadJsonAsync(closeResponse, HttpStatusCode.OK);
        Assert.Equal("CERRADO_CON_PENDIENTES", closed.GetProperty("status").GetString());
        await using var finalScope = _factory!.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var purchase = await finalDb.Compra.AsNoTracking().SingleAsync(x => x.PedidoCompraId == orderId);
        var detail = await finalDb.DetalleCompra.AsNoTracking().SingleAsync(x => x.CompraId == purchase.Id);
        var storedLine = await finalDb.DetallePedidoCompra.AsNoTracking().SingleAsync(x => x.Id == lineId);
        var storedOrder = await finalDb.PedidoCompra.AsNoTracking().SingleAsync(x => x.Id == orderId);
        Assert.True(detail.EsSustituto);
        Assert.Equal(storedLine.AutorizacionExcesoId, detail.AutorizacionExcepcionId);
        Assert.NotNull(detail.AutorizacionExcepcionId);
        Assert.NotNull(storedOrder.AutorizacionCierreId);
        var closeAuthorization = await finalDb.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == storedOrder.AutorizacionCierreId);
        Assert.Equal(administratorCloses, closeAuthorization.UsuarioSolicitanteId ==
            closeAuthorization.UsuarioAutorizadorId);
        Assert.Equal(closeReason, storedOrder.MotivoCierreId);
        Assert.Equal(2, await finalDb.AutorizacionOperacion.CountAsync(x =>
            x.TipoOperacion == "EXCEPCION_RECEPCION_COMPRA" || x.TipoOperacion == "CIERRE_PEDIDO_PENDIENTE"));
        var ownReceiptAuthorization = await finalDb.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.TipoOperacion == "EXCEPCION_RECEPCION_COMPRA");
        Assert.Equal(ownReceiptAuthorization.UsuarioSolicitanteId, ownReceiptAuthorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", ownReceiptAuthorization.MetodoAutenticacion);
        Assert.Null(ownReceiptAuthorization.CredencialAutorizadorId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PurchaseOrder_CancelRequiresReasonAndAdministratorAndCannotUndoReceivedStock()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor de pedido cancelado");
        var product = await ProductAsync(client, supplier.Id, "ORD-CANCEL", null);
        var reasons = await ReadJsonAsync(await client.PostAsync(
            "/api/purchase-orders/reasons/cancel/initialize", null), HttpStatusCode.OK);
        var reasonId = reasons.EnumerateArray().Single(x => x.GetProperty("code").GetString() == "ERROR_PEDIDO")
            .GetProperty("id").GetGuid();
        var order = await ReadJsonAsync(await client.PostAsJsonAsync("/api/purchase-orders",
            new { supplierId = supplier.Id, lines = new[] { new { productId = product.Id, requestedQuantity = 3 } } }),
            HttpStatusCode.Created);
        var id = order.GetProperty("id").GetGuid();
        var before = await StateAsync(product.Id);
        await ProblemAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{id}/cancel",
            new { expectedVersion = 1, reasonId, authorization = new { userName = AdminName,
                password = _adminPassword + "bad" } }), HttpStatusCode.Unauthorized);
        Assert.Equal("BORRADOR", (await ReadJsonAsync(await client.GetAsync($"/api/purchase-orders/{id}"),
            HttpStatusCode.OK)).GetProperty("status").GetString());
        var canceled = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{id}/cancel",
            new { expectedVersion = 1, reasonId }), HttpStatusCode.OK);
        Assert.Equal("CANCELADO", canceled.GetProperty("status").GetString());
        Assert.Equal(before, await StateAsync(product.Id));
        await ProblemAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{id}/send",
            new { expectedVersion = 2 }), HttpStatusCode.Conflict);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var stored = await db.PedidoCompra.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(reasonId, stored.MotivoCancelacionId);
        Assert.NotNull(stored.AutorizacionCancelacionId);
        var ownCancelAuthorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.Id == stored.AutorizacionCancelacionId);
        Assert.Equal(ownCancelAuthorization.UsuarioSolicitanteId, ownCancelAuthorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", ownCancelAuthorization.MetodoAutenticacion);
        Assert.Null(ownCancelAuthorization.CredencialAutorizadorId);
        Assert.Equal(0, await db.Compra.CountAsync(x => x.PedidoCompraId == id));
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var migrations = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
            await migrations.Database.MigrateAsync();

        _factory = CreateFactory();
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new CaptureLogProvider(_capturedLogs));
            });
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-purchases-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-purchases-client");
            builder.UseSetting("Jwt:SigningKey", _jwtKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "240");
            builder.UseSetting("Jwt:ClockSkewSeconds", "0");
        });

    private static LocalPostgresDatabase NewPostgres() => new LocalPostgresDatabase();

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
            Assert.Equal(0, await db.ConsecutivoDocumento.Where(x => x.TipoDocumento == "COMPRA")
                .Select(x => x.UltimoNumero).SingleAsync());
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

        // The installation created the CMP consecutive by itself.
        var numbering = await ReadJsonAsync(await client.GetAsync("/api/document-numbering/purchases"),
            HttpStatusCode.OK);
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
        var purchasedProduct = await ReadAsync<ProductDto>(
            await client.GetAsync($"/api/products/{zeroProduct.Id}"), HttpStatusCode.OK);
        using (var blocked = await client.DeleteAsync(
            $"/api/products/{zeroProduct.Id}?expectedVersion={purchasedProduct.Version}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("solo puede inactivarse", await blocked.Content.ReadAsStringAsync());
        }
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
    public async Task RetailPriceChange_RequiresAdministratorAndSensitivePermission_AndIsAudited()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor con cambio de precio");
        var product = await ProductAsync(client, supplier.Id, "PUR-RETAIL-PRICE", null);
        var sensitivePermission = await PermissionIdAsync(client, PermissionCodes.ProductsSensitiveUpdate);
        await SetPermissionAsync(client, users.Supervisor.User.Id, sensitivePermission, true);
        var supervisor = await LoginAsync(client, SupervisorName, _supervisorPassword);

        Bearer(client, supervisor.AccessToken);
        await ProblemAsync(await client.PostAsJsonAsync("/api/purchases", Body(supplier.Id,
            "PRICE-DENIED", "NO_DISCRIMINADO", new Line(product.Id, 1, 10m, null, 1200m))),
            HttpStatusCode.Forbidden);

        Bearer(client, users.Admin.Token.AccessToken);
        var draft = await CreateDraftAsync(client, Body(supplier.Id, "PRICE-ADMIN", "NO_DISCRIMINADO",
            new Line(product.Id, 2, 10m, null, 1200m)));
        var draftLine = Assert.Single(draft.GetProperty("lines").EnumerateArray());
        Assert.Equal(1000m, draftLine.GetProperty("previousRetailPrice").GetDecimal());
        Assert.Equal(1200m, draftLine.GetProperty("newRetailPrice").GetDecimal());

        Bearer(client, supervisor.AccessToken);
        await ProblemAsync(await ConfirmAsync(client, draft.GetProperty("id").GetGuid(),
            "price-supervisor-confirm", draft.GetProperty("version").GetInt64()), HttpStatusCode.Forbidden);

        Bearer(client, users.Admin.Token.AccessToken);
        var confirmed = await ReadJsonAsync(await ConfirmAsync(client, draft.GetProperty("id").GetGuid(),
            "price-admin-confirm", draft.GetProperty("version").GetInt64()), HttpStatusCode.OK);
        var confirmedLine = Assert.Single(confirmed.GetProperty("lines").EnumerateArray());
        Assert.Equal(1000m, confirmedLine.GetProperty("previousRetailPrice").GetDecimal());
        Assert.Equal(1200m, confirmedLine.GetProperty("newRetailPrice").GetDecimal());
        Assert.Equal(1200m, (await ReadAsync<ProductDto>(await client.GetAsync($"/api/products/{product.Id}"),
            HttpStatusCode.OK)).RetailPrice);

        var order = await ReadJsonAsync(await client.PostAsJsonAsync("/api/purchase-orders", new
        {
            supplierId = supplier.Id,
            lines = new[] { new { productId = product.Id, requestedQuantity = 1, estimatedUnitCost = 11m } }
        }), HttpStatusCode.Created);
        var sent = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/purchase-orders/{order.GetProperty("id").GetGuid()}/send",
            new { expectedVersion = order.GetProperty("version").GetInt64() }), HttpStatusCode.OK);
        var orderLineId = Assert.Single(sent.GetProperty("lines").EnumerateArray()).GetProperty("id").GetGuid();
        var receipt = new
        {
            expectedOrderVersion = sent.GetProperty("version").GetInt64(),
            invoiceDate = "2026-11-18",
            vatMode = "NO_DISCRIMINADO",
            lines = new[] { new { orderLineId, productId = product.Id, quantity = 1, unitCost = 11m,
                newRetailPrice = 1400m } }
        };

        Bearer(client, supervisor.AccessToken);
        await ProblemAsync(await ReceiveOrderAsync(client, order.GetProperty("id").GetGuid(),
            "price-supervisor-receipt", receipt), HttpStatusCode.Forbidden);

        Bearer(client, users.Admin.Token.AccessToken);
        var received = await ReadJsonAsync(await ReceiveOrderAsync(client, order.GetProperty("id").GetGuid(),
            "price-admin-receipt", receipt), HttpStatusCode.Created);
        var receiptLine = Assert.Single(received.GetProperty("purchase").GetProperty("lines").EnumerateArray());
        Assert.Equal(1200m, receiptLine.GetProperty("previousRetailPrice").GetDecimal());
        Assert.Equal(1400m, receiptLine.GetProperty("newRetailPrice").GetDecimal());
        Assert.Equal(1400m, (await ReadAsync<ProductDto>(await client.GetAsync($"/api/products/{product.Id}"),
            HttpStatusCode.OK)).RetailPrice);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        // DatosNuevos es jsonb: PostgreSQL no admite LIKE sobre jsonb, así que el texto se busca en memoria.
        var audits = (await db.EventoAuditoria.AsNoTracking()
                .Where(x => x.EntidadId == product.Id && x.Accion == "PRODUCTUPDATED").ToArrayAsync())
            .Where(x => x.DatosNuevos != null && x.DatosNuevos.Contains("PURCHASE_RECEIPT", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, audits.Length);
        Assert.All(audits, audit =>
            Assert.Contains("retailPrice", audit.DatosNuevos!, StringComparison.Ordinal));
        Assert.Contains(audits, audit => audit.DatosNuevos!.Contains(
            order.GetProperty("id").GetGuid().ToString(), StringComparison.OrdinalIgnoreCase));
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
        // The installation created it by itself: it is there to be read, adjusted and never created by hand.
        var created = await ReadJsonAsync(await client.GetAsync("/api/document-numbering/purchases"),
            HttpStatusCode.OK);
        await ProblemAsync(await client.PostAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 1, documentType = "COMPRA" }), HttpStatusCode.BadRequest);
        Assert.Equal(0, created.GetProperty("lastNumber").GetInt64());
        Assert.Equal(1, created.GetProperty("nextNumber").GetInt64());
        Assert.True(created.GetProperty("canCorrect").GetBoolean());
        await ProblemAsync(await client.PostAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 1 }), HttpStatusCode.Conflict);
        var changed = await ReadJsonAsync(await client.PutAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 2, version = created.GetProperty("version").GetInt64() }), HttpStatusCode.OK);
        Assert.Equal(2, changed.GetProperty("nextNumber").GetInt64());
        Assert.True(changed.GetProperty("canCorrect").GetBoolean());
        await ProblemAsync(await client.PutAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 3, version = created.GetProperty("version").GetInt64() }), HttpStatusCode.Conflict);

        var supplier = await SupplierAsync(client, "Proveedor de consecutivo");
        var product = await ProductAsync(client, supplier.Id, "PUR-NUMBERING", null);
        var draft = await CreateDraftAsync(client, Body(supplier.Id, null, "NO_DISCRIMINADO",
            new Line(product.Id, 1, 100m)));
        _ = await ReadJsonAsync(await ConfirmAsync(client, draft.GetProperty("id").GetGuid(),
            "purchase-numbering-first-confirmation", draft.GetProperty("version").GetInt64()), HttpStatusCode.OK);
        var afterPurchase = await ReadJsonAsync(await client.GetAsync("/api/document-numbering/purchases"),
            HttpStatusCode.OK);
        Assert.False(afterPurchase.GetProperty("canCorrect").GetBoolean());
        await ProblemAsync(await client.PutAsJsonAsync("/api/document-numbering/purchases",
            new { prefix = "CMP", firstNumber = 3, version = afterPurchase.GetProperty("version").GetInt64() }),
            HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.DeleteAsync("/api/document-numbering/purchases")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_ReversesAllLinesAndRestoresNullableCost_WithoutChangingDocument()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor de anulación");
        var first = await ProductAsync(client, supplier.Id, "PUR-CANCEL-A", null);
        var second = await ProductAsync(client, supplier.Id, "PUR-CANCEL-B", null);
        await SeedStockAsync(first.Id, 4, 8m);
        var draft = await CreateDraftAsync(client, Body(supplier.Id, null, "NO_DISCRIMINADO",
            new Line(first.Id, 2, 10m), new Line(second.Id, 3, 12m)));
        var id = draft.GetProperty("id").GetGuid();
        var confirmed = await ReadJsonAsync(await ConfirmAsync(client, id, "cancel-fixture-confirm", 1), HttpStatusCode.OK);
        var beforeFirst = await StateAsync(first.Id);
        var beforeSecond = await StateAsync(second.Id);
        var initialized = await ReadJsonAsync(await client.PostAsync("/api/purchases/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        var reason = initialized.EnumerateArray().Single(x => x.GetProperty("code").GetString() == "ERROR_REGISTRO").GetProperty("id").GetGuid();
        var again = await ReadJsonAsync(await client.PostAsync("/api/purchases/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        Assert.Equal(initialized.GetRawText(), again.GetRawText());
        var beforePurchase = await PurchasePersistenceAsync(id, first.Id, second.Id);
        // El Administrador anula sin segundo autorizador: sus propias credenciales no son las de un rol
        // estrictamente superior (regla del 5 de noviembre) y el servicio las rechaza con 401.
        var request = new { expectedVersion = 2, reasonId = reason, observation = "Registro equivocado" };
        using var invalid = new HttpRequestMessage(HttpMethod.Post, $"/api/purchases/{id}/cancel")
        { Content = JsonContent.Create(new { request.expectedVersion, request.reasonId,
            request.observation, stock = 0 }) };
        invalid.Headers.Add("Idempotency-Key", "cancel-invalid-json");
        await ProblemAsync(await client.SendAsync(invalid), HttpStatusCode.BadRequest);
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-invalid-authorization",
            new { request.expectedVersion, request.reasonId, request.observation,
                authorization = new { userName = AdminName, password = "" } }),
            HttpStatusCode.BadRequest, "validation.failed");
        Assert.Equal(beforePurchase, await PurchasePersistenceAsync(id, first.Id, second.Id));
        using var call = new HttpRequestMessage(HttpMethod.Post, $"/api/purchases/{id}/cancel")
        { Content = JsonContent.Create(request) };
        call.Headers.Add("Idempotency-Key", "cancel-valid");
        var cancelled = await ReadJsonAsync(await client.SendAsync(call), HttpStatusCode.OK);
        Assert.Equal("ANULADA", cancelled.GetProperty("status").GetString());
        Assert.Equal(confirmed.GetProperty("visibleNumber").GetString(), cancelled.GetProperty("visibleNumber").GetString());
        Assert.Equal(confirmed.GetProperty("total").GetDecimal(), cancelled.GetProperty("total").GetDecimal());
        Assert.Equal(confirmed.GetProperty("lines").GetRawText(), cancelled.GetProperty("lines").GetRawText());
        Assert.Equal(confirmed.GetProperty("prefix").GetString(), cancelled.GetProperty("prefix").GetString());
        Assert.Equal(confirmed.GetProperty("series").GetString(), cancelled.GetProperty("series").GetString());
        Assert.Equal(confirmed.GetProperty("consecutive").GetInt64(), cancelled.GetProperty("consecutive").GetInt64());
        Assert.Equal(3, cancelled.GetProperty("version").GetInt64());
        Assert.Equal(4, (await StateAsync(first.Id)).Stock);
        Assert.Equal(8m, (await StateAsync(first.Id)).AverageCost);
        Assert.Equal(0, (await StateAsync(second.Id)).Stock);
        Assert.Null((await StateAsync(second.Id)).AverageCost);
        var committedState = await PurchasePersistenceAsync(id, first.Id, second.Id);
        var repeated = await ReadJsonAsync(await PostCancelAsync(client, id, "cancel-valid",
            new { request.expectedVersion, request.reasonId, request.observation }), HttpStatusCode.OK);
        Assert.Equal(cancelled.GetRawText(), repeated.GetRawText());
        Assert.Equal(committedState, await PurchasePersistenceAsync(id, first.Id, second.Id));
        Assert.Equal(beforeFirst.Movements + 1, (await StateAsync(first.Id)).Movements);
        Assert.Equal(beforeSecond.Movements + 1, (await StateAsync(second.Id)).Movements);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var originals = await db.MovimientoInventario.AsNoTracking().Where(x => x.CompraId == id).ToArrayAsync();
        var originalIds = originals.Select(x => x.Id).ToArray();
        var reversals = await db.MovimientoInventario.AsNoTracking()
            .Where(x => x.MovimientoRevertidoId != null && originalIds.Contains(x.MovimientoRevertidoId.Value)).ToArrayAsync();
        Assert.Equal(2, reversals.Length);
        var details = await db.DetalleCompra.AsNoTracking().Where(x => x.CompraId == id).ToArrayAsync();
        Assert.Equal(8m, details.Single(x => x.ProductoId == first.Id).CostoPromedioAnterior);
        Assert.Null(details.Single(x => x.ProductoId == second.Id).CostoPromedioAnterior);
        Assert.Equal(details.Single(x => x.ProductoId == first.Id).CostoPromedioAnterior,
            (await StateAsync(first.Id)).AverageCost);
        Assert.Equal(details.Single(x => x.ProductoId == second.Id).CostoPromedioAnterior,
            (await StateAsync(second.Id)).AverageCost);
        var purchase = await db.Compra.AsNoTracking().SingleAsync(x => x.Id == id);
        var authorization = await db.AutorizacionOperacion.AsNoTracking().SingleAsync(x => x.Id == purchase.AutorizacionAnulacionId);
        var operation = await db.OperacionIdempotente.AsNoTracking().SingleAsync(x => x.ClaveIdempotencia == "cancel-valid");
        var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x => x.EntidadId == id && x.Accion == "PURCHASECANCELLED");
        Assert.Equal("UTILIZADA", authorization.Estado);
        Assert.Equal(users.Admin.User.Id, authorization.UsuarioAutorizadorId);
        Assert.Equal(reason, purchase.MotivoAnulacionId);
        Assert.Equal(users.Admin.User.Id, purchase.UsuarioAnulacionId);
        Assert.NotNull(purchase.FechaAnulacion);
        Assert.Equal("ANULAR_COMPRA", operation.TipoOperacion);
        Assert.Equal(users.Admin.User.Id, operation.UsuarioId);
        Assert.Equal(authorization.CorrelacionId, operation.CorrelacionId);
        Assert.Equal(operation.CorrelacionId, audit.CorrelacionId);
        Assert.Equal(2, reversals.Select(x => x.MovimientoRevertidoId).Distinct().Count());
        Assert.All(reversals, x =>
        {
            var original = originals.Single(o => o.Id == x.MovimientoRevertidoId);
            var detail = details.Single(d => d.ProductoId == x.ProductoId);
            Assert.Equal("ANULACION_COMPRA", x.TipoMovimiento);
            Assert.Null(x.CompraId);
            Assert.Null(x.VentaId);
            Assert.Equal(-detail.Cantidad, x.CantidadStock);
            Assert.Equal(original.StockPosterior, x.StockAnterior);
            Assert.Equal(original.StockAnterior, x.StockPosterior);
            Assert.Equal(detail.CostoPromedioResultante, x.CostoPromedioAnterior);
            Assert.Equal(detail.CostoPromedioAnterior, x.CostoPromedioPosterior);
            Assert.Equal(authorization.Id, x.AutorizacionOperacionId);
            Assert.Null(x.MotivoOperacionId);
            Assert.Equal(operation.CorrelacionId, x.CorrelacionId);
        });
        Assert.DoesNotContain(_adminPassword, string.Join('|', operation.Resultado, operation.HashSolicitud,
            audit.DatosAnteriores, audit.DatosNuevos, audit.Motivo, authorization.DescripcionOperacion));
        Assert.DoesNotContain("Registro equivocado", string.Join('|', audit.DatosAnteriores, audit.DatosNuevos, audit.Motivo));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_AdministratorWithoutSecondAuthorizer_ReplaysWithoutCredentials()
    {
        using var client = Client();
        var setup = await PrepareCancellationAsync(client, "PUR-AUTH-SAME");
        var product = await ProductAsync(client, setup.Supplier.Id, "PUR-AUTH-SAME-P", null);
        var confirmed = await CreateConfirmedAsync(client, setup.Supplier.Id, product.Id, "auth-same-confirm");
        var id = confirmed.GetProperty("id").GetGuid();
        var version = confirmed.GetProperty("version").GetInt64();
        var before = await PurchasePersistenceAsync(id, product.Id);
        var withoutCredentials = new { expectedVersion = version, reasonId = setup.ReasonId,
            observation = "  observación repetible  " };

        var cancelled = await ReadJsonAsync(await PostCancelAsync(client, id, "auth-same-cancel",
            withoutCredentials), HttpStatusCode.OK);
        var after = await PurchasePersistenceAsync(id, product.Id);
        var replay = await ReadJsonAsync(await PostCancelAsync(client, id, "auth-same-cancel", new
        {
            withoutCredentials.expectedVersion, withoutCredentials.reasonId,
            observation = "observación repetible"
        }), HttpStatusCode.OK);
        Assert.Equal(cancelled.GetRawText(), replay.GetRawText());
        Assert.Equal(after, await PurchasePersistenceAsync(id, product.Id));

        await ProblemCodeAsync(await PostCancelAsync(client, id, "auth-same-cancel", new
        {
            withoutCredentials.expectedVersion, withoutCredentials.reasonId,
            observation = "otra observación"
        }), HttpStatusCode.Conflict, "resource.conflict");
        Assert.Equal(after, await PurchasePersistenceAsync(id, product.Id));

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var operation = await db.OperacionIdempotente.AsNoTracking().SingleAsync(x => x.ClaveIdempotencia == "auth-same-cancel");
        var authorization = await db.AutorizacionOperacion.AsNoTracking()
            .SingleAsync(x => x.TipoOperacion == "ANULAR_COMPRA");
        Assert.Equal(authorization.UsuarioSolicitanteId, authorization.UsuarioAutorizadorId);
        Assert.Equal("PASSWORD", authorization.MetodoAutenticacion);
        Assert.Null(authorization.CredencialAutorizadorId);
        Assert.Contains("sin segundo autorizador", authorization.DescripcionOperacion);
        Assert.Equal(1, await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "ANULAR_COMPRA"));
        Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.EntidadId == id && x.Accion == "PURCHASECANCELLED"));
        Assert.DoesNotContain(_adminPassword, operation.Resultado ?? "");
        Assert.DoesNotContain(_adminPassword, operation.HashSolicitud);
        Assert.DoesNotContain(_adminPassword, await AllSecurityPersistenceTextAsync(db));
        Assert.DoesNotContain(_adminPassword, string.Join('\n', _capturedLogs));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_UsesCurrentAdministratorRoleAndEffectivePermission_AlsoForReplay()
    {
        using var client = Client();
        var setup = await PrepareCancellationAsync(client, "PUR-PERMISSION");
        var product = await ProductAsync(client, setup.Supplier.Id, "PUR-PERMISSION-P", null);
        var confirmed = await CreateConfirmedAsync(client, setup.Supplier.Id, product.Id, "permission-confirm");
        var id = confirmed.GetProperty("id").GetGuid();
        var version = confirmed.GetProperty("version").GetInt64();
        var cancelPermission = await PermissionIdAsync(client, PermissionCodes.PurchasesCancel);
        var request = new { expectedVersion = version, reasonId = setup.ReasonId };
        var before = await PurchasePersistenceAsync(id, product.Id);

        Bearer(client, setup.Users.Supervisor.Token.AccessToken);
        await ProblemCodeAsync(await PostCancelAsync(client, id, "permission-supervisor", request),
            HttpStatusCode.Forbidden, "authorization.forbidden");
        Bearer(client, setup.Users.Admin.Token.AccessToken);
        await SetPermissionAsync(client, setup.Users.Supervisor.User.Id, cancelPermission, true);
        var supervisor = await LoginAsync(client, SupervisorName, _supervisorPassword);
        Bearer(client, supervisor.AccessToken);
        await ProblemCodeAsync(await PostCancelAsync(client, id, "permission-supervisor-grant", request),
            HttpStatusCode.Forbidden, "authorization.forbidden");
        Bearer(client, setup.Users.Cashier.Token.AccessToken);
        await ProblemCodeAsync(await PostCancelAsync(client, id, "permission-cashier", request),
            HttpStatusCode.Forbidden, "authorization.forbidden");
        client.DefaultRequestHeaders.Authorization = null;
        await ProblemAsync(await PostCancelAsync(client, id, "permission-anonymous", request), HttpStatusCode.Unauthorized);
        Assert.Equal(before, await PurchasePersistenceAsync(id, product.Id));

        Bearer(client, setup.Users.Admin.Token.AccessToken);
        var completed = await ReadJsonAsync(await PostCancelAsync(client, id, "permission-admin", request), HttpStatusCode.OK);
        var after = await PurchasePersistenceAsync(id, product.Id);
        await SetPermissionAsync(client, setup.Users.Admin.User.Id, cancelPermission, false);
        var revokedAdmin = await LoginAsync(client, AdminName, _adminPassword);
        Bearer(client, revokedAdmin.AccessToken);
        await ProblemCodeAsync(await PostCancelAsync(client, id, "permission-admin",
            new { expectedVersion = version, reasonId = setup.ReasonId }),
            HttpStatusCode.Forbidden, "authorization.forbidden");
        Assert.Equal(after, await PurchasePersistenceAsync(id, product.Id));
        Assert.Equal("ANULADA", completed.GetProperty("status").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_RejectsCredentialsOfAnotherAdministrator_WithoutLeaksOrEffects()
    {
        using var client = Client();
        var setup = await PrepareCancellationAsync(client, "PUR-AUTH-OTHER");
        var second = await AdditionalAdministratorAsync(client);
        var product = await ProductAsync(client, setup.Supplier.Id, "PUR-AUTH-OTHER-P", null);
        var confirmed = await CreateConfirmedAsync(client, setup.Supplier.Id, product.Id, "auth-other-confirm");
        var id = confirmed.GetProperty("id").GetGuid();
        var version = confirmed.GetProperty("version").GetInt64();
        var before = await PurchasePersistenceAsync(id, product.Id);
        var invalidPassword = $"Invalid-Cancel-{Secret(24)}!";

        // Anular es una operación de Administrador, el rol más alto: ninguna credencial enviada puede ser de un
        // rol estrictamente superior (regla del 5 de noviembre). Ni la contraseña incorrecta ni la correcta de
        // otro Administrador activo se aceptan, y la respuesta es idéntica y sin datos sensibles.
        foreach (var password in new[] { invalidPassword, second.Password })
        {
            var rejected = await PostCancelAsync(client, id, "auth-other-cancel", new
            {
                expectedVersion = version, reasonId = setup.ReasonId,
                authorization = new { userName = second.User.User.UserName, password }
            });
            var rejectedBody = await rejected.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
            using (var problem = JsonDocument.Parse(rejectedBody))
                Assert.Equal("authentication.unauthorized", problem.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain(invalidPassword, rejectedBody);
            Assert.DoesNotContain(second.Password, rejectedBody);
            Assert.DoesNotContain(_adminPassword, rejectedBody);
            Assert.Equal(before, await PurchasePersistenceAsync(id, product.Id));
        }

        // La anulación válida del Administrador es la propia, sin segundo autorizador.
        var cancelled = await ReadJsonAsync(await PostCancelAsync(client, id, "auth-other-cancel", new
        {
            expectedVersion = version, reasonId = setup.ReasonId
        }), HttpStatusCode.OK);
        Assert.Equal("ANULADA", cancelled.GetProperty("status").GetString());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var purchase = await db.Compra.AsNoTracking().SingleAsync(x => x.Id == id);
        var authorization = await db.AutorizacionOperacion.AsNoTracking().SingleAsync(x => x.Id == purchase.AutorizacionAnulacionId);
        Assert.Equal(setup.Users.Admin.User.Id, authorization.UsuarioSolicitanteId);
        Assert.Equal(setup.Users.Admin.User.Id, authorization.UsuarioAutorizadorId);
        Assert.NotEqual(second.User.User.Id, authorization.UsuarioAutorizadorId);
        Assert.Equal("UTILIZADA", authorization.Estado);
        var allPersisted = await AllSecurityPersistenceTextAsync(db);
        foreach (var credential in new[] { invalidPassword, second.Password, _adminPassword })
        {
            Assert.DoesNotContain(credential, allPersisted);
            Assert.DoesNotContain(credential, string.Join('\n', _capturedLogs));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CancellationReasons_InitializeOnlyMissingRowsAndStopWithoutChangesOnConflict()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        client.DefaultRequestHeaders.Authorization = null;
        await ProblemAsync(await client.GetAsync("/api/purchases/cancellation-reasons"), HttpStatusCode.Unauthorized);
        Bearer(client, users.Supervisor.Token.AccessToken);
        await ProblemCodeAsync(await client.GetAsync("/api/purchases/cancellation-reasons"),
            HttpStatusCode.Forbidden, "authorization.forbidden");
        await ProblemCodeAsync(await client.PostAsync("/api/purchases/cancellation-reasons/initialize", null),
            HttpStatusCode.Forbidden, "authorization.forbidden");
        Bearer(client, users.Admin.Token.AccessToken);
        // The installation created the approved catalog by itself: it is already complete when it is read.
        var before = await ReadJsonAsync(await client.GetAsync("/api/purchases/cancellation-reasons"), HttpStatusCode.OK);
        Assert.Equal(new[] { "ERROR_REGISTRO", "COMPRA_DUPLICADA", "DOCUMENTO_PROVEEDOR_ANULADO",
            "MERCANCIA_NO_RECIBIDA", "OTRO" },
            before.EnumerateArray().Select(x => x.GetProperty("code").GetString()));
        Assert.All(before.EnumerateArray(), x => Assert.Equal('7', x.GetProperty("id").GetGuid().ToString("D")[14]));
        // The explicit initialization is an idempotent repair: with nothing missing it returns the same catalog.
        var initialized = await ReadJsonAsync(await client.PostAsync("/api/purchases/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        Assert.Equal(before.GetRawText(), initialized.GetRawText());
        Assert.Equal(initialized.GetRawText(), (await ReadJsonAsync(
            await client.PostAsync("/api/purchases/cancellation-reasons/initialize", null), HttpStatusCode.OK)).GetRawText());
        Assert.Equal(initialized.GetRawText(), (await ReadJsonAsync(
            await client.GetAsync("/api/purchases/cancellation-reasons"), HttpStatusCode.OK)).GetRawText());

        Guid retainedId;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var retained = await db.MotivoOperacion.SingleAsync(x => x.Codigo == "ERROR_REGISTRO" && x.TipoOperacion == "ANULACION_COMPRA");
            retainedId = retained.Id;
            db.MotivoOperacion.Remove(await db.MotivoOperacion.SingleAsync(x => x.Codigo == "OTRO" && x.TipoOperacion == "ANULACION_COMPRA"));
            await db.SaveChangesAsync();
        }
        var repaired = await ReadJsonAsync(await client.PostAsync("/api/purchases/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        Assert.Equal(5, repaired.GetArrayLength());
        Assert.Equal(retainedId, repaired.EnumerateArray().Single(x => x.GetProperty("code").GetString() == "ERROR_REGISTRO").GetProperty("id").GetGuid());
        Assert.Equal('7', repaired.EnumerateArray().Single(x => x.GetProperty("code").GetString() == "OTRO").GetProperty("id").GetGuid().ToString("D")[14]);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var incompatible = await db.MotivoOperacion.SingleAsync(x => x.Codigo == "ERROR_REGISTRO" && x.TipoOperacion == "ANULACION_COMPRA");
            incompatible.Nombre = "Nombre incompatible";
            db.MotivoOperacion.Remove(await db.MotivoOperacion.SingleAsync(x => x.Codigo == "OTRO" && x.TipoOperacion == "ANULACION_COMPRA"));
            await db.SaveChangesAsync();
        }
        var beforeConflict = await CancellationCatalogPersistenceAsync();
        await ProblemCodeAsync(await client.PostAsync("/api/purchases/cancellation-reasons/initialize", null),
            HttpStatusCode.Conflict, "resource.conflict");
        Assert.Equal(beforeConflict, await CancellationCatalogPersistenceAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_RejectsInvalidJsonKeysReasonsVersionsAndStatesWithoutEffects()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, "Proveedor cancelación inválida");
        var product = await ProductAsync(client, supplier.Id, "PUR-CANCEL-INVALID", null);
        var confirmed = await CreateConfirmedAsync(client, supplier.Id, product.Id, "cancel-invalid-confirm");
        var id = confirmed.GetProperty("id").GetGuid();
        var version = confirmed.GetProperty("version").GetInt64();
        var draft = await CreateDraftAsync(client, Body(supplier.Id, null, "NO_DISCRIMINADO",
            new Line(product.Id, 1, 7m)));
        var before = await PurchasePersistenceAsync(id, product.Id);
        var unknownReason = Guid.NewGuid();
        var reasonCount = await CancellationReasonCountAsync();
        Assert.Equal(ApprovedReasonCatalogs.PurchaseCancellation.Length, reasonCount);
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-unknown-reason", new
        {
            expectedVersion = version, reasonId = unknownReason,
            authorization = new { userName = AdminName, password = _adminPassword }
        }), HttpStatusCode.NotFound, "resource.not_found");
        Assert.Equal(reasonCount, await CancellationReasonCountAsync());
        var reasons = await ReadJsonAsync(await client.PostAsync("/api/purchases/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        var reason = reasons.EnumerateArray().Single(x => x.GetProperty("code").GetString() == "ERROR_REGISTRO").GetProperty("id").GetGuid();
        var other = reasons.EnumerateArray().Single(x => x.GetProperty("code").GetString() == "OTRO").GetProperty("id").GetGuid();
        var validAuth = new { userName = AdminName, password = _adminPassword };
        foreach (var field in new[] { "status", "purchaseId", "version", "authorizationId", "stock", "audit" })
        {
            var body = new Dictionary<string, object?>
            {
                ["expectedVersion"] = version, ["reasonId"] = reason,
                ["authorization"] = validAuth, [field] = "server-owned"
            };
            await ProblemCodeAsync(await PostCancelAsync(client, id, $"cancel-unknown-{field}", body),
                HttpStatusCode.BadRequest, "validation.failed");
        }
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-nested-unknown", new
        {
            expectedVersion = version, reasonId = reason,
            authorization = new { userName = AdminName, password = _adminPassword, role = "ADMINISTRADOR" }
        }), HttpStatusCode.BadRequest, "validation.failed");
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-missing-version", new
        {
            reasonId = reason, authorization = validAuth
        }), HttpStatusCode.BadRequest, "validation.failed");
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-missing-reason", new
        {
            expectedVersion = version, authorization = validAuth
        }), HttpStatusCode.BadRequest, "validation.failed");
        await ProblemCodeAsync(await PostCancelAsync(client, id, null, new
        {
            expectedVersion = version, reasonId = reason, authorization = validAuth
        }), HttpStatusCode.BadRequest, "validation.failed");
        await ProblemCodeAsync(await PostCancelAsync(client, id, "invalid key with spaces", new
        {
            expectedVersion = version, reasonId = reason, authorization = validAuth
        }), HttpStatusCode.BadRequest, "validation.failed");
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-otro-blank", new
        {
            expectedVersion = version, reasonId = other, observation = " \t ", authorization = validAuth
        }), HttpStatusCode.BadRequest, "validation.failed");
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-no-reason", new
        {
            expectedVersion = version, reasonId = Guid.NewGuid(), authorization = validAuth
        }), HttpStatusCode.NotFound, "resource.not_found");
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-stale", new
        {
            expectedVersion = version - 1, reasonId = reason, authorization = validAuth
        }), HttpStatusCode.Conflict, "concurrency.conflict");
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-invalid-confirm", new
        {
            expectedVersion = version, reasonId = reason, authorization = validAuth
        }), HttpStatusCode.Conflict, "resource.conflict");
        await ProblemCodeAsync(await PostCancelAsync(client, draft.GetProperty("id").GetGuid(), "cancel-draft", new
        {
            expectedVersion = draft.GetProperty("version").GetInt64(), reasonId = reason, authorization = validAuth
        }), HttpStatusCode.Conflict, "resource.conflict");

        Guid foreignReason;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            foreignReason = await db.MotivoOperacion.AsNoTracking().Where(x => x.TipoOperacion != "ANULACION_COMPRA")
                .Select(x => x.Id).FirstAsync();
            var inactive = await db.MotivoOperacion.SingleAsync(x => x.Id == reason);
            inactive.Activo = false;
            await db.SaveChangesAsync();
        }
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-inactive-reason", new
        {
            expectedVersion = version, reasonId = reason, authorization = validAuth
        }), HttpStatusCode.NotFound, "resource.not_found");
        await ProblemCodeAsync(await PostCancelAsync(client, id, "cancel-foreign-reason", new
        {
            expectedVersion = version, reasonId = foreignReason, authorization = validAuth
        }), HttpStatusCode.NotFound, "resource.not_found");
        Assert.Equal(before, await PurchasePersistenceAsync(id, product.Id));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"/api/purchases/{id}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync("/api/purchases/cancellation-reasons")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task Cancel_RejectsLaterMovementEvenWhenDateAndIdSortBeforeOriginal(bool sameDate)
    {
        using var client = Client();
        var setup = await PrepareCancellationAsync(client, "PUR-LATER-MOVE");
        var product = await ProductAsync(client, setup.Supplier.Id, "PUR-LATER-MOVE-P", null);
        var first = await CreateConfirmedAsync(client, setup.Supplier.Id, product.Id, "later-first-confirm");
        var firstId = first.GetProperty("id").GetGuid();
        var committed = await StateAsync(product.Id);
        var second = await CreateConfirmedAsync(client, setup.Supplier.Id, product.Id, "later-second-confirm", quantity: 1);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var original = await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.CompraId == firstId);
            var later = await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.CompraId == second.GetProperty("id").GetGuid());
            Assert.True(later.OrdenRegistro > original.OrdenRegistro);
            var earlierId = Guid.Parse("00000000-0000-7000-8000-000000000001");
            var chosenDate = sameDate ? original.FechaHora : original.FechaHora.AddMinutes(-1);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE compras.movimiento_inventario SET id = {earlierId}, fecha_hora = {chosenDate} WHERE id = {later.Id}");
            Assert.True(earlierId.CompareTo(original.Id) < 0);
        }
        await SeedStockAsync(product.Id, committed.Stock, committed.AverageCost);
        var stateBefore = await PurchasePersistenceAsync(firstId, product.Id);
        await ProblemCodeAsync(await PostCancelAsync(client, firstId, "later-cancel", new
        {
            expectedVersion = first.GetProperty("version").GetInt64(), reasonId = setup.ReasonId,
            authorization = new { userName = AdminName, password = _adminPassword }
        }), HttpStatusCode.Conflict, "resource.conflict");
        Assert.Equal(stateBefore, await PurchasePersistenceAsync(firstId, product.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_RejectsReservedStockAndInconsistentStockOrCostWithoutEffects()
    {
        using var client = Client();
        var setup = await PrepareCancellationAsync(client, "PUR-STOCK-GUARDS");
        var reservedProduct = await ProductAsync(client, setup.Supplier.Id, "PUR-RESERVED", null);
        var stockProduct = await ProductAsync(client, setup.Supplier.Id, "PUR-STOCK-MISMATCH", null);
        var costProduct = await ProductAsync(client, setup.Supplier.Id, "PUR-COST-MISMATCH", null);
        await SeedStockAsync(reservedProduct.Id, 4, 8m);
        var reserved = await CreateConfirmedAsync(client, setup.Supplier.Id, reservedProduct.Id, "reserved-confirm");
        var mismatchedStock = await CreateConfirmedAsync(client, setup.Supplier.Id, stockProduct.Id, "stock-mismatch-confirm");
        var mismatchedCost = await CreateConfirmedAsync(client, setup.Supplier.Id, costProduct.Id, "cost-mismatch-confirm");
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            (await db.Producto.SingleAsync(x => x.Id == reservedProduct.Id)).StockReservado = 5;
            (await db.Producto.SingleAsync(x => x.Id == stockProduct.Id)).StockActual += 1;
            (await db.Producto.SingleAsync(x => x.Id == costProduct.Id)).CostoPromedio += 1m;
            await db.SaveChangesAsync();
        }
        foreach (var (purchase, productId, key) in new[]
        {
            (reserved, reservedProduct.Id, "reserved-cancel"),
            (mismatchedStock, stockProduct.Id, "stock-mismatch-cancel"),
            (mismatchedCost, costProduct.Id, "cost-mismatch-cancel")
        })
        {
            var id = purchase.GetProperty("id").GetGuid();
            var before = await PurchasePersistenceAsync(id, productId);
            await ProblemCodeAsync(await PostCancelAsync(client, id, key, new
            {
                expectedVersion = purchase.GetProperty("version").GetInt64(), reasonId = setup.ReasonId,
                authorization = new { userName = AdminName, password = _adminPassword }
            }), HttpStatusCode.Conflict, "resource.conflict");
            Assert.Equal(before, await PurchasePersistenceAsync(id, productId));
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_ConcurrentSameAndDifferentKeysCommitOneReversalAuthorizationAndAudit()
    {
        using var setupClient = Client();
        using var firstClient = Client();
        using var secondClient = Client();
        var setup = await PrepareCancellationAsync(setupClient, "PUR-CANCEL-RACES");
        Bearer(firstClient, setup.Users.Admin.Token.AccessToken);
        Bearer(secondClient, setup.Users.Admin.Token.AccessToken);
        var sameProduct = await ProductAsync(setupClient, setup.Supplier.Id, "PUR-CANCEL-SAME-KEY", null);
        var differentProduct = await ProductAsync(setupClient, setup.Supplier.Id, "PUR-CANCEL-DIFF-KEY", null);
        var sameOriginalCost = (await StateAsync(sameProduct.Id)).AverageCost;
        var differentOriginalCost = (await StateAsync(differentProduct.Id)).AverageCost;
        var same = await CreateConfirmedAsync(setupClient, setup.Supplier.Id, sameProduct.Id, "race-same-confirm");
        var different = await CreateConfirmedAsync(setupClient, setup.Supplier.Id, differentProduct.Id, "race-different-confirm");
        var sameId = same.GetProperty("id").GetGuid();
        var sameVersion = same.GetProperty("version").GetInt64();
        var sameBefore = await StateAsync(sameProduct.Id);
        var differentBefore = await StateAsync(differentProduct.Id);
        var sameExpectedCost = await AssertCancellationCostBaselineAsync(sameId, sameProduct.Id,
            sameOriginalCost, sameBefore.AverageCost);
        var differentId = different.GetProperty("id").GetGuid();
        var differentExpectedCost = await AssertCancellationCostBaselineAsync(differentId, differentProduct.Id,
            differentOriginalCost, differentBefore.AverageCost);
        var sameBody = new { expectedVersion = sameVersion, reasonId = setup.ReasonId };
        var sameResponses = await Task.WhenAll(
            PostCancelAsync(firstClient, sameId, "race-same-cancel", sameBody),
            PostCancelAsync(secondClient, sameId, "race-same-cancel", sameBody));
        var sameResults = await Task.WhenAll(sameResponses.Select(x => ReadJsonAsync(x, HttpStatusCode.OK)));
        Assert.Equal(sameResults[0].GetRawText(), sameResults[1].GetRawText());
        Assert.Equal("ANULADA", sameResults[0].GetProperty("status").GetString());
        Assert.Equal(sameVersion + 1, sameResults[0].GetProperty("version").GetInt64());
        await AssertRaceCancellationAsync(sameId, sameProduct.Id, sameVersion, "race-same-cancel",
            sameBefore, sameExpectedCost);

        var differentVersion = different.GetProperty("version").GetInt64();
        var differentBody = new { expectedVersion = differentVersion, reasonId = setup.ReasonId };
        var responses = await Task.WhenAll(
            PostCancelAsync(firstClient, differentId, "race-different-a", differentBody),
            PostCancelAsync(secondClient, differentId, "race-different-b", differentBody));
        var winner = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        var winnerKey = ReferenceEquals(winner, responses[0]) ? "race-different-a" : "race-different-b";
        var winnerResult = await ReadJsonAsync(winner, HttpStatusCode.OK);
        Assert.Equal("ANULADA", winnerResult.GetProperty("status").GetString());
        Assert.Equal(differentVersion + 1, winnerResult.GetProperty("version").GetInt64());
        await ProblemCodeAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict),
            HttpStatusCode.Conflict, "concurrency.conflict");
        await AssertRaceCancellationAsync(differentId, differentProduct.Id, differentVersion, winnerKey,
            differentBefore, differentExpectedCost);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(2, await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "ANULAR_COMPRA"));
        Assert.Equal(2, await db.AutorizacionOperacion.CountAsync(x => x.TipoOperacion == "ANULAR_COMPRA" && x.Estado == "UTILIZADA"));
        Assert.Equal(2, await db.OperacionIdempotente.CountAsync(x => x.TipoOperacion == "ANULAR_COMPRA"));
        Assert.Equal(2, await db.MovimientoInventario.CountAsync(x => x.TipoMovimiento == "ANULACION_COMPRA"));
        Assert.Equal(2, await db.EventoAuditoria.CountAsync(x => x.Accion == "PURCHASECANCELLED"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_LateWriteAndDeferredCommitFailuresRollbackEverythingAndPermitSameKeyRetry()
    {
        using var client = Client();
        var setup = await PrepareCancellationAsync(client, "PUR-CANCEL-ROLLBACK");
        var product = await ProductAsync(client, setup.Supplier.Id, "PUR-CANCEL-ROLLBACK-P", null);
        var first = await CreateConfirmedAsync(client, setup.Supplier.Id, product.Id, "rollback-first-confirm");
        var firstId = first.GetProperty("id").GetGuid();
        var firstBody = new { expectedVersion = first.GetProperty("version").GetInt64(), reasonId = setup.ReasonId };
        var beforeLateFailure = await PurchasePersistenceAsync(firstId, product.Id);
        await InstallCancellationFailureAsync(firstId, deferred: false);
        await ProblemAsync(await PostCancelAsync(client, firstId, "rollback-late", firstBody), HttpStatusCode.Conflict);
        Assert.True(await CancellationFailureObservedAsync());
        Assert.Equal(beforeLateFailure, await PurchasePersistenceAsync(firstId, product.Id));
        await RemoveCancellationFailureAsync();
        var recovered = await ReadJsonAsync(await PostCancelAsync(client, firstId, "rollback-late", firstBody), HttpStatusCode.OK);
        Assert.Equal("ANULADA", recovered.GetProperty("status").GetString());
        await AssertOneCancellationAsync(firstId, product.Id);

        var secondProduct = await ProductAsync(client, setup.Supplier.Id, "PUR-CANCEL-DEFERRED-P", null);
        var second = await CreateConfirmedAsync(client, setup.Supplier.Id, secondProduct.Id, "rollback-second-confirm");
        var secondId = second.GetProperty("id").GetGuid();
        var secondBody = new { expectedVersion = second.GetProperty("version").GetInt64(), reasonId = setup.ReasonId };
        var beforeCommitFailure = await PurchasePersistenceAsync(secondId, secondProduct.Id);
        await InstallCancellationFailureAsync(secondId, deferred: true);
        await ProblemAsync(await PostCancelAsync(client, secondId, "rollback-deferred", secondBody), HttpStatusCode.Conflict);
        Assert.True(await CancellationFailureObservedAsync());
        Assert.Equal(beforeCommitFailure, await PurchasePersistenceAsync(secondId, secondProduct.Id));
        await RemoveCancellationFailureAsync();
        var afterRetry = await ReadJsonAsync(await PostCancelAsync(client, secondId, "rollback-deferred", secondBody), HttpStatusCode.OK);
        Assert.Equal("ANULADA", afterRetry.GetProperty("status").GetString());
        await AssertOneCancellationAsync(secondId, secondProduct.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Cancel_HistoricalPurchaseFromPreviousMigration_RejectsWithoutAnyEffects()
    {
        using var client = Client();
        var setup = await PrepareCancellationAsync(client, "Proveedor histórico anterior a orden");
        var product = await ProductAsync(client, setup.Supplier.Id, "PUR-HIST-NULL", null);
        var confirmed = await CreateConfirmedAsync(client, setup.Supplier.Id, product.Id,
            "historical-null-order-confirm");
        var purchaseId = confirmed.GetProperty("id").GetGuid();
        var version = confirmed.GetProperty("version").GetInt64();
        // Una compra anterior a 20261014010000 no tiene orden_registro. El esquema actual lo asigna siempre
        // y lo protege como inmutable, así que se simula dejándolo nulo con el trigger de protección
        // desactivado solo durante esta sentencia (la prueba es dueña de sus tablas y, al ser una sola
        // sentencia múltiple, un fallo revierte también la desactivación).
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                ALTER TABLE compras.movimiento_inventario
                    DISABLE TRIGGER trg_movimiento_inventario_proteger_orden_registro;
                UPDATE compras.movimiento_inventario SET orden_registro = NULL WHERE compra_id = @purchase;
                ALTER TABLE compras.movimiento_inventario
                    ENABLE TRIGGER trg_movimiento_inventario_proteger_orden_registro;
                """, connection);
            command.Parameters.AddWithValue("purchase", purchaseId);
            await command.ExecuteNonQueryAsync();
        }
        Guid movementId;
        await using (var movementScope = _factory!.Services.CreateAsyncScope())
        {
            var movementDb = movementScope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            movementId = (await movementDb.MovimientoInventario.AsNoTracking()
                .FirstAsync(x => x.CompraId == purchaseId)).Id;
        }

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.Null((await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.Id == movementId)).OrdenRegistro);
        }
        var before = await PurchasePersistenceAsync(purchaseId, product.Id);
        var beforeState = await StateAsync(product.Id);
        long auditCount;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            auditCount = await db.EventoAuditoria.LongCountAsync();
        }
        var response = await PostCancelAsync(client, purchaseId, "historical-null-order", new
        {
            expectedVersion = version, reasonId = setup.ReasonId
        });
        await ProblemCodeAsync(response, HttpStatusCode.Conflict, "resource.conflict");
        Assert.Equal(before, await PurchasePersistenceAsync(purchaseId, product.Id));
        Assert.Equal(beforeState, await StateAsync(product.Id));
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.Equal(auditCount, await db.EventoAuditoria.LongCountAsync());
            Assert.Equal("CONFIRMADA", (await db.Compra.AsNoTracking().SingleAsync(x => x.Id == purchaseId)).Estado);
            Assert.Empty(await db.MovimientoInventario.AsNoTracking()
                .Where(x => x.TipoMovimiento == "ANULACION_COMPRA").ToArrayAsync());
            Assert.Empty(await db.AutorizacionOperacion.AsNoTracking()
                .Where(x => x.TipoOperacion == "ANULAR_COMPRA").ToArrayAsync());
            Assert.Empty(await db.OperacionIdempotente.AsNoTracking()
                .Where(x => x.ClaveIdempotencia == "historical-null-order").ToArrayAsync());
        }
        var freshLogin = await LoginAsync(client, AdminName, _adminPassword);
        Bearer(client, freshLogin.AccessToken);
        var stillConfirmed = await ReadJsonAsync(await client.GetAsync($"/api/purchases/{purchaseId}"), HttpStatusCode.OK);
        Assert.Equal("CONFIRMADA", stillConfirmed.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("revoked")]
    [Trait("Category", "Integration")]
    public async Task Cancel_RejectsInactiveOrIndividuallyRevokedAdministratorWithoutEffects(string scenario)
    {
        using var client = Client();
        var setup = await PrepareCancellationAsync(client, $"Proveedor autorización {scenario}");
        var authorizer = await AdditionalAdministratorAsync(client);
        var product = await ProductAsync(client, setup.Supplier.Id, $"PUR-AUTH-{scenario.ToUpperInvariant()}", null);
        var confirmed = await CreateConfirmedAsync(client, setup.Supplier.Id, product.Id, $"auth-{scenario}-confirm");
        var purchaseId = confirmed.GetProperty("id").GetGuid();
        if (scenario == "inactive")
        {
            await using var scope = _factory!.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var user = await db.Usuario.SingleAsync(x => x.Id == authorizer.User.User.Id);
            user.Activo = false;
            await db.SaveChangesAsync();
        }
        else
        {
            var permission = await PermissionIdAsync(client, PermissionCodes.PurchasesCancel);
            await SetPermissionAsync(client, authorizer.User.User.Id, permission, false);
        }
        var before = await PurchasePersistenceAsync(purchaseId, product.Id);
        var beforeState = await StateAsync(product.Id);
        long auditCount;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            auditCount = await db.EventoAuditoria.LongCountAsync();
        }
        var response = await PostCancelAsync(client, purchaseId, $"auth-{scenario}-cancel", new
        {
            expectedVersion = confirmed.GetProperty("version").GetInt64(), reasonId = setup.ReasonId,
            authorization = new { userName = authorizer.User.User.UserName, password = authorizer.Password }
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using (var problem = JsonDocument.Parse(body))
            Assert.Equal("authentication.unauthorized", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(authorizer.Password, body);
        Assert.Equal(before, await PurchasePersistenceAsync(purchaseId, product.Id));
        Assert.Equal(beforeState, await StateAsync(product.Id));
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            Assert.Equal(auditCount, await db.EventoAuditoria.LongCountAsync());
            Assert.DoesNotContain(authorizer.Password, await AllSecurityPersistenceTextAsync(db));
        }
        Assert.DoesNotContain(authorizer.Password, string.Join('\n', _capturedLogs));
    }

    private async Task<CancellationSetup> PrepareCancellationAsync(HttpClient client, string supplierName)
    {
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await SupplierAsync(client, supplierName);
        var reasons = await ReadJsonAsync(await client.PostAsync("/api/purchases/cancellation-reasons/initialize", null), HttpStatusCode.OK);
        return new CancellationSetup(users, supplier, reasons.EnumerateArray()
            .Single(x => x.GetProperty("code").GetString() == "ERROR_REGISTRO").GetProperty("id").GetGuid());
    }

    private async Task<AdditionalAdmin> AdditionalAdministratorAsync(HttpClient client)
    {
        var roles = await ReadAsync<PagedResult<RoleDto>>(await client.GetAsync("/api/roles?pageSize=100"), HttpStatusCode.OK);
        var administratorRole = roles.Items.Single(x => x.Code == RoleCodes.Administrator).Id;
        var password = $"Purchases-Second-Admin-{Secret(32)}!";
        const string name = "purchases.second.admin.test";
        var user = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(name, "Segundo Administrador Compras", password, [administratorRole])), HttpStatusCode.Created);
        var token = await LoginAsync(client, name, password);
        Bearer(client, (await LoginAsync(client, AdminName, _adminPassword)).AccessToken);
        return new AdditionalAdmin(new AuthUser(token, user), password);
    }

    private async Task<JsonElement> CreateConfirmedAsync(HttpClient client, Guid supplierId, Guid productId,
        string key, int quantity = 2)
    {
        var draft = await CreateDraftAsync(client, Body(supplierId, null, "NO_DISCRIMINADO",
            new Line(productId, quantity, 10m)));
        return await ReadJsonAsync(await ConfirmAsync(client, draft.GetProperty("id").GetGuid(), key,
            draft.GetProperty("version").GetInt64()), HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> PostCancelAsync(HttpClient client, Guid purchaseId,
        string? key, object body)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/purchases/{purchaseId}/cancel")
        { Content = JsonContent.Create(body) };
        if (key is not null) message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private async Task<int> CancellationReasonCountAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        return await db.MotivoOperacion.CountAsync(x => x.TipoOperacion == "ANULACION_COMPRA");
    }

    private async Task<string> CancellationCatalogPersistenceAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var rows = await db.MotivoOperacion.AsNoTracking().Where(x => x.TipoOperacion == "ANULACION_COMPRA")
            .OrderBy(x => x.Codigo).Select(x => new
            {
                x.Id, x.EstablecimientoId, x.Codigo, x.Nombre, x.Descripcion,
                x.OrdenVisual, x.Activo, x.FechaCreacion, x.FechaModificacion, x.Version
            }).ToArrayAsync();
        return JsonSerializer.Serialize(rows);
    }

    private async Task<string> PurchasePersistenceAsync(Guid purchaseId, params Guid[] productIds)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var purchase = await db.Compra.AsNoTracking().Where(x => x.Id == purchaseId)
            .Select(x => new { x.Id, x.PedidoCompraId, x.Estado, x.Version, x.Prefijo, x.Serie,
                x.Consecutivo, x.NumeroDocumentoProveedor, x.FechaHoraConfirmacion, x.SubtotalBruto,
                x.ImpuestoTotal, x.Total, x.UsuarioAnulacionId, x.AutorizacionAnulacionId,
                x.MotivoAnulacionId, x.FechaAnulacion, x.ObservacionAnulacion }).SingleAsync();
        var details = await db.DetalleCompra.AsNoTracking().Where(x => x.CompraId == purchaseId)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.ProductoId, x.Cantidad,
                x.CostoUnitarioDocumento, x.SubtotalBruto, x.ValorIva, x.TotalLinea,
                x.CostoPromedioAnterior, x.CostoPromedioResultante }).ToArrayAsync();
        var products = await db.Producto.AsNoTracking().Where(x => productIds.Contains(x.Id))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.StockActual, x.StockReservado,
                x.CostoPromedio, x.Version }).ToArrayAsync();
        var movements = await db.MovimientoInventario.AsNoTracking().Where(x => productIds.Contains(x.ProductoId))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.ProductoId, x.CompraId,
                x.MovimientoRevertidoId, x.TipoMovimiento, x.CantidadStock, x.StockAnterior,
                x.StockPosterior, x.CostoPromedioAnterior, x.CostoPromedioPosterior,
                x.AutorizacionOperacionId, x.CorrelacionId }).ToArrayAsync();
        var authorizations = await db.AutorizacionOperacion.AsNoTracking()
            .Where(x => x.TipoOperacion == "ANULAR_COMPRA").OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.UsuarioSolicitanteId, x.UsuarioAutorizadorId,
                x.Estado, x.Resultado, x.CorrelacionId }).ToArrayAsync();
        var idempotency = await db.OperacionIdempotente.AsNoTracking()
            .Where(x => x.TipoOperacion == "ANULAR_COMPRA").OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ClaveIdempotencia, x.HashSolicitud, x.Resultado,
                x.EntidadId, x.UsuarioId, x.CorrelacionId }).ToArrayAsync();
        var audits = await db.EventoAuditoria.AsNoTracking()
            .Where(x => x.Accion == "PURCHASECANCELLED").OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.EntidadId, x.UsuarioId, x.UsuarioAutorizadorId,
                x.Resultado, x.Motivo, x.DatosAnteriores, x.DatosNuevos, x.CorrelacionId }).ToArrayAsync();
        return JsonSerializer.Serialize(new { purchase, details, products, movements, authorizations, idempotency, audits });
    }

    private async Task AssertOneCancellationAsync(Guid purchaseId, Guid productId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var purchase = await db.Compra.AsNoTracking().SingleAsync(x => x.Id == purchaseId);
        Assert.Equal("ANULADA", purchase.Estado);
        Assert.Equal(3, purchase.Version);
        Assert.NotNull(purchase.AutorizacionAnulacionId);
        var original = await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.CompraId == purchaseId && x.ProductoId == productId);
        var inverse = await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.MovimientoRevertidoId == original.Id);
        Assert.Equal("ANULACION_COMPRA", inverse.TipoMovimiento);
        Assert.Null(inverse.CompraId);
        Assert.Equal(-original.CantidadStock, inverse.CantidadStock);
        Assert.Equal(original.StockAnterior, inverse.StockPosterior);
        Assert.Equal(purchase.AutorizacionAnulacionId, inverse.AutorizacionOperacionId);
        var product = await db.Producto.AsNoTracking().SingleAsync(x => x.Id == productId);
        Assert.Equal(inverse.StockPosterior, product.StockActual);
        Assert.Equal(inverse.CostoPromedioPosterior, product.CostoPromedio);
        var authorization = await db.AutorizacionOperacion.AsNoTracking().SingleAsync(x => x.Id == purchase.AutorizacionAnulacionId);
        Assert.Equal("UTILIZADA", authorization.Estado);
        Assert.Single(await db.OperacionIdempotente.AsNoTracking()
            .Where(x => x.TipoOperacion == "ANULAR_COMPRA" && x.EntidadId == purchaseId).ToArrayAsync());
        Assert.Single(await db.EventoAuditoria.AsNoTracking()
            .Where(x => x.Accion == "PURCHASECANCELLED" && x.EntidadId == purchaseId).ToArrayAsync());
    }

    private async Task<decimal?> AssertCancellationCostBaselineAsync(Guid purchaseId, Guid productId,
        decimal? originalCost, decimal? confirmedCost)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var detail = await db.DetalleCompra.AsNoTracking().SingleAsync(x => x.CompraId == purchaseId && x.ProductoId == productId);
        Assert.Equal(originalCost, detail.CostoPromedioAnterior);
        Assert.Equal(confirmedCost, detail.CostoPromedioResultante);
        return detail.CostoPromedioAnterior;
    }

    private async Task AssertRaceCancellationAsync(Guid purchaseId, Guid productId, long expectedVersion,
        string winningKey, (int Stock, decimal? AverageCost, long Version, int Movements, long? LastNumber) before,
        decimal? expectedRestoredCost)
    {
        await AssertOneCancellationAsync(purchaseId, productId);
        var after = await StateAsync(productId);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var purchase = await db.Compra.AsNoTracking().SingleAsync(x => x.Id == purchaseId);
        var detail = await db.DetalleCompra.AsNoTracking().SingleAsync(x => x.CompraId == purchaseId && x.ProductoId == productId);
        var operation = await db.OperacionIdempotente.AsNoTracking().SingleAsync(x => x.TipoOperacion == "ANULAR_COMPRA" && x.EntidadId == purchaseId);
        var authorization = await db.AutorizacionOperacion.AsNoTracking().SingleAsync(x => x.Id == purchase.AutorizacionAnulacionId);
        var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x => x.Accion == "PURCHASECANCELLED" && x.EntidadId == purchaseId);
        var original = await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.CompraId == purchaseId && x.ProductoId == productId);
        var inverse = await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.MovimientoRevertidoId == original.Id);

        Assert.Equal(expectedVersion + 1, purchase.Version);
        Assert.Equal(before.Stock - detail.Cantidad, after.Stock);
        Assert.Equal(expectedRestoredCost, detail.CostoPromedioAnterior);
        Assert.Equal(detail.CostoPromedioAnterior, after.AverageCost);
        Assert.Equal(expectedRestoredCost, after.AverageCost);
        Assert.Equal(before.Movements + 1, after.Movements);
        Assert.Equal(before.LastNumber, after.LastNumber);
        Assert.Equal(winningKey, operation.ClaveIdempotencia);
        Assert.Equal(operation.CorrelacionId, authorization.CorrelacionId);
        Assert.Equal(operation.CorrelacionId, inverse.CorrelacionId);
        Assert.Equal(operation.CorrelacionId, audit.CorrelacionId);
        Assert.Equal("UTILIZADA", authorization.Estado);
        Assert.NotNull(operation.Resultado);
        using var saved = JsonDocument.Parse(operation.Resultado);
        Assert.Equal(purchaseId, saved.RootElement.GetProperty("Id").GetGuid());
        Assert.Equal(expectedVersion + 1, saved.RootElement.GetProperty("Version").GetInt64());
        Assert.Equal("ANULADA", saved.RootElement.GetProperty("Status").GetString());
    }

    private static async Task<string> AllSecurityPersistenceTextAsync(OfficialControlPlusDbContext db)
    {
        var authorizations = await db.AutorizacionOperacion.AsNoTracking()
            .Where(x => x.TipoOperacion == "ANULAR_COMPRA")
            .Select(x => new { x.DescripcionOperacion, x.Motivo, x.MetodoAutenticacion,
                x.Resultado, x.Estado }).ToArrayAsync();
        var operations = await db.OperacionIdempotente.AsNoTracking()
            .Where(x => x.TipoOperacion == "ANULAR_COMPRA")
            .Select(x => new { x.HashSolicitud, x.Resultado }).ToArrayAsync();
        var audits = await db.EventoAuditoria.AsNoTracking()
            .Select(x => new { x.Motivo, x.DatosAnteriores, x.DatosNuevos }).ToArrayAsync();
        return JsonSerializer.Serialize(new { authorizations, operations, audits });
    }

    private async Task InstallCancellationFailureAsync(Guid purchaseId, bool deferred)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var triggerKind = deferred ? "CONSTRAINT TRIGGER" : "TRIGGER";
        var deferClause = deferred ? "DEFERRABLE INITIALLY DEFERRED" : "";
        await using var command = new NpgsqlCommand($$"""
            CREATE SEQUENCE public.test_purchase_cancel_observed;
            CREATE FUNCTION public.test_fail_purchase_cancel() RETURNS trigger LANGUAGE plpgsql AS $trigger$
            BEGIN
              IF NEW.entidad_id = '{{purchaseId}}'::uuid AND NEW.accion = 'PURCHASECANCELLED' THEN
                PERFORM nextval('public.test_purchase_cancel_observed');
                RAISE EXCEPTION 'forced purchase cancellation failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $trigger$;
            CREATE {{triggerKind}} zz_test_fail_purchase_cancel AFTER INSERT ON auditoria.evento_auditoria
                {{deferClause}} FOR EACH ROW EXECUTE FUNCTION public.test_fail_purchase_cancel();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> CancellationFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT is_called FROM public.test_purchase_cancel_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task RemoveCancellationFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            DROP TRIGGER zz_test_fail_purchase_cancel ON auditoria.evento_auditoria;
            DROP FUNCTION public.test_fail_purchase_cancel();
            DROP SEQUENCE public.test_purchase_cancel_observed;
            """, connection);
        await command.ExecuteNonQueryAsync();
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
            new CreateCategoryRequest($"Categoría {code}", null, NextCategoryAbbreviation())), HttpStatusCode.Created);
        var units = await ReadAsync<MeasurementUnitDto[]>(await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        return await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, units.Single(x => x.Code == "UNIDAD").Id,
                supplierId, code, null, null, true, $"Producto {code}", null, 1000m,
                null, taxPercentage, 0)), HttpStatusCode.Created);
    }

    private string NextCategoryAbbreviation()
    {
        var sequence = Interlocked.Increment(ref _categorySequence);
        var letters = new char[7];
        for (var index = letters.Length - 1; index >= 0; index--)
        {
            letters[index] = (char)('A' + sequence % 26);
            sequence /= 26;
        }
        return "CMP" + new string(letters);
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

    private static async Task ProblemCodeAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var parsed = JsonDocument.Parse(body);
        Assert.Equal(code, parsed.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Postgres", body, StringComparison.OrdinalIgnoreCase);
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

    private sealed record Line(Guid ProductId, int Quantity, decimal UnitCost, decimal? VatPercentage = null,
        decimal? NewRetailPrice = null);
    private sealed record CreateBody(Guid SupplierId, string? SupplierDocumentNumber, DateOnly InvoiceDate,
        string VatMode, Line[] Lines);
    private sealed record UpdateBody(Guid SupplierId, string? SupplierDocumentNumber, DateOnly InvoiceDate,
        string VatMode, Line[] Lines, long ExpectedVersion);
    private sealed record AuthUser(AuthenticationResult Token, UserDto User);
    private sealed record Users(AuthUser Admin, AuthUser Supervisor, AuthUser Cashier);
    private sealed record AdditionalAdmin(AuthUser User, string Password);
    private sealed record CancellationSetup(Users Users, SupplierDto Supplier, Guid ReasonId);

    private sealed class CaptureLogProvider(ConcurrentQueue<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(messages);
        public void Dispose() { }

        private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + exception?.ToString());
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
