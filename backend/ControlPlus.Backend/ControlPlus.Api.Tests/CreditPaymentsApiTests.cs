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
using ControlPlus.Application.Credits.Contracts;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Runs against a disposable PostgreSQL 17 database and the real HTTP pipeline.</summary>
public sealed class CreditPaymentsApiTests : IAsyncLifetime
{
    private const string AdminName = "credits.payments.admin.test";
    private const string SupervisorName = "credits.payments.supervisor.test";
    private const string CashierName = "credits.payments.cashier.test";
    private readonly string _masterKey = Secret(48);
    private readonly string _jwtKey = Secret(64);
    private readonly string _adminPassword = $"Credits-Admin-{Secret(24)}!";
    private readonly string _supervisorPassword = $"Credits-Supervisor-{Secret(24)}!";
    private readonly string _cashierPassword = $"Credits-Cashier-{Secret(24)}!";
    private readonly LocalPostgresDatabase _postgres = new LocalPostgresDatabase();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var migrations = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
            await migrations.Database.MigrateAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseSetting("ConnectionStrings:ControlPlusDb", _postgres.GetConnectionString());
            builder.UseSetting("Installation:MasterKey", _masterKey);
            builder.UseSetting("Jwt:Issuer", "controlplus-credit-payments-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-credit-payments-client");
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

    [Fact, Trait("Category", "Integration")]
    public async Task CashPartialAndFinalPayment_UpdateBalanceVersionAndState_WithoutChangingSaleOrStock()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var sale = await CreateCreditAsync(client, setup, "credit-cash-main");
        var creditId = sale.CreditId!.Value;
        var before = await SnapshotAsync(creditId, setup.ProductId);

        var first = await PayAsync(client, creditId, "credit-cash-partial",
            new ApplyCreditPaymentRequest(1, [new("EFECTIVO", 400m, 500m, null)]));
        var partial = await ReadAsync<CreditPaymentOperationDto>(first, HttpStatusCode.Created);
        Assert.Equal(600m, partial.Balance);
        Assert.Equal("PENDIENTE", partial.State);
        Assert.Equal(2, partial.Version);
        var cash = Assert.Single(partial.Payments);
        Assert.Equal("ABO-CRD1-1", cash.Number);
        Assert.Equal(100m, cash.Change);
        Assert.Equal(partial.CorrelationId, cash.CorrelationId);

        var final = await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, creditId, "credit-cash-final",
            new ApplyCreditPaymentRequest(partial.Version, [new("EFECTIVO", 600m, 600m, null)])), HttpStatusCode.Created);
        Assert.Equal(0m, final.Balance);
        Assert.Equal("PAGADO", final.State);
        Assert.Equal(3, final.Version);
        Assert.Equal("ABO-CRD1-2", Assert.Single(final.Payments).Number);
        Assert.Equal(HttpStatusCode.Conflict, (await PayAsync(client, creditId, "paid-credit-rejected",
            new ApplyCreditPaymentRequest(final.Version, [new("EFECTIVO", 1m, 1m, null)]))).StatusCode);

        var after = await SnapshotAsync(creditId, setup.ProductId);
        Assert.Equal(before.SaleRows, after.SaleRows);
        Assert.Equal(before.InventoryRows, after.InventoryRows);
        Assert.Equal(before.Stock, after.Stock);
        Assert.Equal(before.AuthorizationRows, after.AuthorizationRows);
        Assert.Equal(before.CashMovementRows + 2, after.CashMovementRows);
        Assert.Equal(before.PaymentRows + 2, after.PaymentRows);
        Assert.Equal(before.IdempotencyRows + 2, after.IdempotencyRows);
        Assert.Equal(2L, after.LastAbo);
        var history = await ReadAsync<PagedResult<CreditPaymentDto>>(await client.GetAsync(
            $"/api/credits/{creditId}/payments?pageSize=1&page=1"), HttpStatusCode.OK);
        Assert.Equal(2, history.TotalCount);
        Assert.Single(history.Items);
        Assert.Equal(1, history.PageSize);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CombinedPayment_PersistsOneAboPerMethodOneCorrelationOneMovement_AndReconciliationCountsOnce()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var sale = await CreateCreditAsync(client, setup, "credit-combined-sale");
        var creditId = sale.CreditId!.Value;
        var operation = await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, creditId,
            "credit-combined-payment", new ApplyCreditPaymentRequest(1,
            [
                new("EFECTIVO", 200m, 250m, null),
                new("TRANSFERENCIA", 300m, null, "TRX-PRIVATE-892"),
                new("NEQUI", 500m, null, "NEQUI-PRIVATE-573")
            ])), HttpStatusCode.Created);

        Assert.Equal("PAGADO", operation.State);
        Assert.Equal(0m, operation.Balance);
        Assert.Equal(3, operation.Payments.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, operation.Payments
            .Select(x => long.Parse(x.Number.Split('-')[^1])).OrderBy(x => x));
        Assert.All(operation.Payments, x => Assert.Equal(operation.CorrelationId, x.CorrelationId));
        Assert.Equal(50m, operation.Payments.Single(x => x.MethodCode == "EFECTIVO").Change);
        Assert.All(operation.Payments.Where(x => x.MethodCode != "EFECTIVO"), x => Assert.Equal(0m, x.Change));

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var rows = await db.PagoCredito.AsNoTracking().Where(x => x.CreditoId == creditId).ToArrayAsync();
            Assert.Equal(3, rows.Length);
            var movementId = Assert.Single(rows.Select(x => x.MovimientoCajaId).Distinct());
            Assert.NotNull(movementId);
            var movement = await db.MovimientoCaja.AsNoTracking().SingleAsync(x => x.Id == movementId);
            Assert.Equal("ABONO_CREDITO", movement.CategoriaMovimiento);
            Assert.Equal(1_000m, movement.Valor);
            var cashFlow = scope.ServiceProvider.GetRequiredService<ControlPlus.Application.Reports.IDailyCashFlowRepository>();
            var operatingDate = await db.TurnoCaja.Where(x => x.Id == rows[0].TurnoCajaId)
                .Select(x => x.FechaOperativa).SingleAsync();
            var entries = await cashFlow.GetEntriesAsync(rows[0].InstalacionId, operatingDate, operatingDate, default);
            Assert.Equal(1_000m, entries.Where(x => x.Origin == "ABONO_CREDITO").Sum(x => x.Amount));
            Assert.Equal(3, entries.Count(x => x.Origin == "ABONO_CREDITO"));
            Assert.Equal(operation.CorrelationId, movement.CorrelacionId);
            Assert.All(rows, x => Assert.Equal("ABO", x.Prefijo));
            Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.Accion == "CREDITPAYMENTAPPLIED" && x.EntidadId == creditId));
            var auditText = await ReadAuditTextAsync(db);
            foreach (var secret in new[] { "TRX-PRIVATE-892", "NEQUI-PRIVATE-573", "credit-combined-payment",
                         _adminPassword, _supervisorPassword, _jwtKey, "Dirección privada", "Correo privado" })
                Assert.DoesNotContain(secret, auditText, StringComparison.OrdinalIgnoreCase);
        }

        var reconciliation = await ReadAsync<CashReconciliationDto>(await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(1_000m, reconciliation.TotalExpected);
        Assert.Equal(200m, reconciliation.PaymentMethods.Single(x => x.Code == "EFECTIVO").ExpectedAmount);
        Assert.Equal(300m, reconciliation.PaymentMethods.Single(x => x.Code == "TRANSFERENCIA").ExpectedAmount);
        Assert.Equal(500m, reconciliation.PaymentMethods.Single(x => x.Code == "NEQUI").ExpectedAmount);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ElectronicReferencesAndAmountGuards_RejectWithoutAnyFinancialWrite()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var creditId = (await CreateCreditAsync(client, setup, "credit-invalid-payments")).CreditId!.Value;
        var before = await SnapshotAsync(creditId, setup.ProductId);
        var invalid = new[]
        {
            new ApplyCreditPaymentRequest(1, [new CreditPaymentMethodRequest("TRANSFERENCIA", 200m, null, null)]),
            new ApplyCreditPaymentRequest(1, [new CreditPaymentMethodRequest("NEQUI", 200m, null, " ")]),
            new ApplyCreditPaymentRequest(1, [new CreditPaymentMethodRequest("NEQUI", 200m, 200m, "N-1")]),
            new ApplyCreditPaymentRequest(1, [new CreditPaymentMethodRequest("EFECTIVO", 200m, 199m, null)]),
            new ApplyCreditPaymentRequest(1, [new CreditPaymentMethodRequest("EFECTIVO", 1_001m, 1_001m, null)]),
            new ApplyCreditPaymentRequest(1, [new CreditPaymentMethodRequest("EFECTIVO", 0m, 0m, null)])
        };
        foreach (var request in invalid)
        {
            await AssertProblemAsync(await PayAsync(client, creditId, Guid.NewGuid().ToString("N"), request),
                HttpStatusCode.BadRequest);
            Assert.Equal(before, await SnapshotAsync(creditId, setup.ProductId));
        }

        var transfer = await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, creditId,
            "credit-transfer", new ApplyCreditPaymentRequest(1,
                [new("TRANSFERENCIA", 400m, null, "TRANSFER-OK")])) , HttpStatusCode.Created);
        Assert.Equal(600m, transfer.Balance);
        var nequi = await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, creditId,
            "credit-nequi", new ApplyCreditPaymentRequest(transfer.Version,
                [new("NEQUI", 600m, null, "NEQUI-OK")])) , HttpStatusCode.Created);
        Assert.Equal(0m, nequi.Balance);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ReplaysConflictsAndTwoRealConcurrentPayments_NeverOverdrawOrDuplicate()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var creditId = (await CreateCreditAsync(client, setup, "credit-concurrency-sale")).CreditId!.Value;
        var request = new ApplyCreditPaymentRequest(1, [new CreditPaymentMethodRequest("EFECTIVO", 700m, 700m, null)]);
        var first = await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, creditId,
            "same-payment-key", request), HttpStatusCode.Created);
        var replay = await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, creditId,
            "same-payment-key", request), HttpStatusCode.Created);
        Assert.Equal(first.CreditId, replay.CreditId);
        Assert.Equal(first.Balance, replay.Balance);
        Assert.Equal(first.Version, replay.Version);
        Assert.Equal(first.CorrelationId, replay.CorrelationId);
        Assert.Equal(Assert.Single(first.Payments), Assert.Single(replay.Payments));
        await AssertProblemAsync(await PayAsync(client, creditId, "same-payment-key",
            new ApplyCreditPaymentRequest(2, [new("EFECTIVO", 1m, 1m, null)])), HttpStatusCode.Conflict);
        Assert.Equal(1, await PaymentCountAsync(creditId));

        using var firstClient = CreateClient(setup.Admin.AccessToken);
        using var secondClient = CreateClient(setup.Admin.AccessToken);
        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await LockInstallationAsync(blocker, transaction);
        var a = PayAsync(firstClient, creditId, "concurrent-overpay-a",
            new ApplyCreditPaymentRequest(2, [new("EFECTIVO", 300m, 300m, null)]));
        var b = PayAsync(secondClient, creditId, "concurrent-overpay-b",
            new ApplyCreditPaymentRequest(2, [new("EFECTIVO", 300m, 300m, null)]));
        await WaitForInstallationWaitersAsync(2);
        await transaction.CommitAsync();
        var outcomes = await Task.WhenAll(a, b);
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Conflict);
        var persisted = await ReadAsync<CreditDto>(await client.GetAsync($"/api/credits/{creditId}"), HttpStatusCode.OK);
        Assert.Equal(0m, persisted.Balance);
        Assert.Equal(2, await PaymentCountAsync(creditId));
        Assert.Equal(2L, (await SnapshotAsync(creditId, setup.ProductId)).LastAbo);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task MissingAndExhaustedAboNumbering_RejectWithoutFinancialEffects()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client, configureAbo: false);
        var creditId = (await CreateCreditAsync(client, setup, "credit-missing-abo-sale")).CreditId!.Value;
        var before = await SnapshotAsync(creditId, setup.ProductId);
        await AssertProblemAsync(await PayAsync(client, creditId, "missing-abo-payment",
            new ApplyCreditPaymentRequest(1, [new("EFECTIVO", 100m, 100m, null)])), HttpStatusCode.Conflict);
        Assert.Equal(before, await SnapshotAsync(creditId, setup.ProductId));

        await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync("/api/document-numbering/credit-payments",
            new CreateSalesDocumentNumberingRequest("ABO", long.MaxValue)), HttpStatusCode.Created);
        var last = await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, creditId, "last-abo-payment",
            new ApplyCreditPaymentRequest(1, [new("EFECTIVO", 100m, 100m, null)])), HttpStatusCode.Created);
        Assert.EndsWith($"-{long.MaxValue}", Assert.Single(last.Payments).Number, StringComparison.Ordinal);
        var afterLast = await SnapshotAsync(creditId, setup.ProductId);
        await AssertProblemAsync(await PayAsync(client, creditId, "exhausted-abo-payment",
            new ApplyCreditPaymentRequest(last.Version, [new("EFECTIVO", 100m, 100m, null)])), HttpStatusCode.Conflict);
        Assert.Equal(afterLast, await SnapshotAsync(creditId, setup.ProductId));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task DeferredCommitFailure_RestoresBalanceVersionPaymentsMovementNumberAndIdempotency()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var creditId = (await CreateCreditAsync(client, setup, "credit-deferred-sale")).CreditId!.Value;
        var before = await SnapshotAsync(creditId, setup.ProductId);
        await InstallDeferredPaymentFailureAsync();

        await AssertProblemAsync(await PayAsync(client, creditId, "deferred-payment-key",
            new ApplyCreditPaymentRequest(1, [new("EFECTIVO", 300m, 300m, null)])), HttpStatusCode.Conflict);
        Assert.True(await DeferredPaymentFailureObservedAsync());
        Assert.Equal(before, await SnapshotAsync(creditId, setup.ProductId));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task AboNumbering_IsServerOwnedStrictVersionedAndUneditableAfterFirstPayment()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client, configureAbo: false);
        await AssertProblemAsync(await client.GetAsync("/api/document-numbering/credit-payments"), HttpStatusCode.NotFound);
        foreach (var invalid in new[]
                 {
                     new CreateSalesDocumentNumberingRequest("VTA", 1),
                     new CreateSalesDocumentNumberingRequest("ABO", 0),
                     new CreateSalesDocumentNumberingRequest("ABCDEFGHIJK", 1)
                 })
            await AssertProblemAsync(await client.PostAsJsonAsync("/api/document-numbering/credit-payments", invalid),
                HttpStatusCode.BadRequest);
        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/credit-payments", new CreateSalesDocumentNumberingRequest(" abo ", 1)),
            HttpStatusCode.Created);
        Assert.Equal("PAGO_CREDITO", created.DocumentType);
        Assert.Equal("CRD1", created.Series);
        Assert.Equal("ABO", created.Prefix);
        Assert.Equal(0, created.LastNumber);
        Assert.Equal(1, created.NextNumber);
        Assert.Equal(created, await ReadAsync<SalesDocumentNumberingDto>(
            await client.GetAsync("/api/document-numbering/credit-payments"), HttpStatusCode.OK));
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/document-numbering/credit-payments",
            new CreateSalesDocumentNumberingRequest("ABO", 1)), HttpStatusCode.Conflict);

        var corrected = await ReadAsync<SalesDocumentNumberingDto>(await client.PutAsJsonAsync(
            "/api/document-numbering/credit-payments", new UpdateSalesDocumentNumberingRequest("ABO", 5, created.Version)),
            HttpStatusCode.OK);
        Assert.Equal(4, corrected.LastNumber);
        Assert.Equal(5, corrected.NextNumber);
        Assert.Equal(2, corrected.Version);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/document-numbering/credit-payments",
            new UpdateSalesDocumentNumberingRequest("ABO", 1, created.Version)), HttpStatusCode.Conflict);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/document-numbering/credit-payments",
            new UpdateSalesDocumentNumberingRequest("VTA", 1, corrected.Version)), HttpStatusCode.BadRequest);

        var creditId = (await CreateCreditAsync(client, setup, "credit-numbering-immutable-sale")).CreditId!.Value;
        await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, creditId, "credit-numbering-first-payment",
            new ApplyCreditPaymentRequest(1, [new("EFECTIVO", 100m, 100m, null)])), HttpStatusCode.Created);
        var afterPayment = await ReadAsync<SalesDocumentNumberingDto>(await client.GetAsync(
            "/api/document-numbering/credit-payments"), HttpStatusCode.OK);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/document-numbering/credit-payments",
            new UpdateSalesDocumentNumberingRequest("ABO", 1, afterPayment.Version)), HttpStatusCode.Conflict);
        var persisted = await ReadAsync<SalesDocumentNumberingDto>(await client.GetAsync(
            "/api/document-numbering/credit-payments"), HttpStatusCode.OK);
        Assert.Equal(5, persisted.LastNumber);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task AboNumbering_RejectsInternalJsonFieldsAndHonorsIndividualRevocation()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client, configureAbo: false);
        foreach (var field in new[] { "installationId", "series", "documentType", "lastNumber", "version", "unknownRoot" })
        {
            await AssertProblemAsync(await RawJsonAsync(client, HttpMethod.Post,
                "/api/document-numbering/credit-payments", new Dictionary<string, object?>
                { ["prefix"] = "ABO", ["firstNumber"] = 1, [field] = "client-value" }), HttpStatusCode.BadRequest);
        }
        var created = await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
            "/api/document-numbering/credit-payments", new CreateSalesDocumentNumberingRequest("ABO", 1)),
            HttpStatusCode.Created);
        foreach (var field in new[] { "installationId", "series", "documentType", "lastNumber", "persistedVersion", "unknownRoot" })
        {
            await AssertProblemAsync(await RawJsonAsync(client, HttpMethod.Put,
                "/api/document-numbering/credit-payments", new Dictionary<string, object?>
                { ["prefix"] = "ABO", ["firstNumber"] = 2, ["version"] = created.Version, [field] = "client-value" }),
                HttpStatusCode.BadRequest);
        }
        var permissionId = await PermissionIdAsync(client, PermissionCodes.NumberingManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{setup.Admin.User.Id}/permissions/{permissionId}", new SetUserPermissionRequest(false))).StatusCode);
        var revoked = await LoginAsync(client, AdminName, _adminPassword);
        SetBearer(client, revoked.AccessToken);
        await AssertProblemAsync(await client.GetAsync("/api/document-numbering/credit-payments"), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/document-numbering/credit-payments",
            new CreateSalesDocumentNumberingRequest("ABO", 1)), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await client.PutAsJsonAsync("/api/document-numbering/credit-payments",
            new UpdateSalesDocumentNumberingRequest("ABO", 2, created.Version)), HttpStatusCode.Forbidden);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var row = await db.ConsecutivoDocumento.AsNoTracking().SingleAsync(x => x.TipoDocumento == "PAGO_CREDITO");
        Assert.Equal(0, row.UltimoNumero);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CreditEndpoints_EnforceManageAndHistoryPermissions_StrictJsonAndPrivateProjection()
    {
        using var client = CreateClient();
        await AssertProblemAsync(await client.GetAsync("/api/credits"), HttpStatusCode.Unauthorized);
        await AssertProblemAsync(await client.GetAsync($"/api/credits/{Guid.CreateVersion7()}"), HttpStatusCode.Unauthorized);
        var setup = await BootstrapAsync(client);
        var sale = await CreateCreditAsync(client, setup, "credit-query-sale");
        var creditId = sale.CreditId!.Value;
        var list = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync("/api/credits"), HttpStatusCode.OK);
        Assert.Equal(20, list.PageSize);
        Assert.Contains(list.Items, x => x.Id == creditId);
        var detailResponse = await client.GetAsync($"/api/credits/{creditId}");
        var detail = await ReadAsync<CreditDto>(detailResponse, HttpStatusCode.OK);
        Assert.Equal(sale.Id, detail.SaleId);
        var payments = await ReadAsync<PagedResult<CreditPaymentDto>>(
            await client.GetAsync($"/api/credits/{creditId}/payments"), HttpStatusCode.OK);
        Assert.Empty(payments.Items);
        Assert.Equal(20, payments.PageSize);
        await AssertProblemAsync(await client.GetAsync($"/api/credits/{Guid.CreateVersion7()}"), HttpStatusCode.NotFound);
        await AssertProblemAsync(await client.GetAsync($"/api/credits/{Guid.CreateVersion7()}/payments"), HttpStatusCode.NotFound);
        await AssertProblemAsync(await client.GetAsync("/api/credits?pageSize=101"), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await client.GetAsync($"/api/credits/{creditId}/payments?pageSize=101"), HttpStatusCode.BadRequest);

        foreach (var field in new[] { "unknownRoot", "creditId", "balance", "version", "userId" })
            await AssertProblemAsync(await RawJsonAsync(client, HttpMethod.Post, $"/api/credits/{creditId}/payments",
                new Dictionary<string, object?>
                {
                    ["expectedVersion"] = 1,
                    ["payments"] = new[] { new { methodCode = "EFECTIVO", appliedAmount = 100, receivedAmount = 100 } },
                    [field] = "client-value"
                }, "strict-credit-json"), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await RawJsonAsync(client, HttpMethod.Post, $"/api/credits/{creditId}/payments",
            new Dictionary<string, object?>
            {
                ["expectedVersion"] = 1,
                ["payments"] = new[] { new { methodCode = "EFECTIVO", appliedAmount = 100, receivedAmount = 100,
                    reference = (string?)null, movementId = Guid.CreateVersion7() } }
            }, "strict-credit-nested-json"), HttpStatusCode.BadRequest);
        Assert.Equal(0, await PaymentCountAsync(creditId));

        foreach (var path in new[] { "/api/credits", $"/api/credits/{creditId}", $"/api/credits/{creditId}/payments" })
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(path, JsonContent.Create(new { }))).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(path)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.PatchAsync($"/api/credits/{creditId}", JsonContent.Create(new { }))).StatusCode);

        SetBearer(client, setup.Supervisor.AccessToken);
        var supervisorResponse = await client.GetAsync($"/api/credits/{creditId}");
        Assert.Equal(HttpStatusCode.OK, supervisorResponse.StatusCode);
        var supervisorBody = await supervisorResponse.Content.ReadAsStringAsync();
        AssertPrivateCreditJson(supervisorBody);
        SetBearer(client, setup.Cashier.AccessToken);
        var cashierResponse = await client.GetAsync($"/api/credits/{creditId}");
        Assert.Equal(HttpStatusCode.OK, cashierResponse.StatusCode);
        var cashierBody = await cashierResponse.Content.ReadAsStringAsync();
        AssertPrivateCreditJson(cashierBody);

        SetBearer(client, setup.Admin.AccessToken);
        var historyPermission = await PermissionIdAsync(client, PermissionCodes.CustomersHistoryRead);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{setup.Cashier.User.Id}/permissions/{historyPermission}",
            new SetUserPermissionRequest(false))).StatusCode);
        var cashierWithoutHistory = await LoginAsync(client, CashierName, _cashierPassword);
        SetBearer(client, cashierWithoutHistory.AccessToken);
        await AssertProblemAsync(await client.GetAsync($"/api/credits/{creditId}/payments"), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/credits/{creditId}")).StatusCode);

        SetBearer(client, setup.Admin.AccessToken);
        var managePermission = await PermissionIdAsync(client, PermissionCodes.CreditManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{setup.Cashier.User.Id}/permissions/{managePermission}",
            new SetUserPermissionRequest(false))).StatusCode);
        var cashierWithoutManage = await LoginAsync(client, CashierName, _cashierPassword);
        SetBearer(client, cashierWithoutManage.AccessToken);
        await AssertProblemAsync(await client.GetAsync("/api/credits"), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await client.GetAsync($"/api/credits/{creditId}"), HttpStatusCode.Forbidden);
        await AssertProblemAsync(await PayAsync(client, creditId, "forbidden-payment",
            new ApplyCreditPaymentRequest(1, [new("EFECTIVO", 100m, 100m, null)])), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Integration")]
    public async Task ExtremePageOffset_ReturnsValidationProblemWithoutWrites_AndKeepsAuthenticationUsable(
        bool paymentHistory)
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var sale = await CreateCreditAsync(client, setup, $"credit-extreme-page-{paymentHistory}");
        var creditId = sale.CreditId!.Value;
        var before = await SnapshotAsync(creditId, setup.ProductId);
        var path = paymentHistory
            ? $"/api/credits/{creditId}/payments?page=2147483647&pageSize=20"
            : "/api/credits?page=2147483647&pageSize=20";

        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using (var problem = JsonDocument.Parse(body))
            Assert.Equal("validation.failed", problem.RootElement.GetProperty("code").GetString());

        Assert.Equal(before, await SnapshotAsync(creditId, setup.ProductId));
        _ = await ReadAsync<CreditDto>(
            await client.GetAsync($"/api/credits/{creditId}"), HttpStatusCode.OK);
        Assert.Equal(before, await SnapshotAsync(creditId, setup.ProductId));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task CreditQueries_ApplyFiltersStableOrderAndEffectiveOverdueStateWithoutWrites()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var first = await CreateCreditAsync(client, setup, "credit-query-first");
        var second = await CreateCreditAsync(client, setup, "credit-query-second");
        var old = setup.Shift.OperatingDate.AddDays(-2);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var credit = await db.Credito.SingleAsync(x => x.Id == first.CreditId);
            credit.FechaOtorgamiento = old.AddDays(-1);
            credit.FechaVencimiento = old;
            await db.SaveChangesAsync();
        }
        var before = await SnapshotAsync(first.CreditId!.Value, setup.ProductId);
        var overdue = await ReadAsync<CreditDto>(await client.GetAsync($"/api/credits/{first.CreditId}"), HttpStatusCode.OK);
        Assert.Equal("VENCIDO", overdue.State);
        var overdueList = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync("/api/credits?state=VENCIDO"), HttpStatusCode.OK);
        Assert.Contains(overdueList.Items, x => x.Id == first.CreditId);
        Assert.DoesNotContain(overdueList.Items, x => x.Id == second.CreditId);
        var byCustomer = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync($"/api/credits?customerId={setup.CustomerId}"), HttpStatusCode.OK);
        Assert.Equal(2, byCustomer.TotalCount);
        var maximumPage = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync("/api/credits?pageSize=100"), HttpStatusCode.OK);
        Assert.Equal(100, maximumPage.PageSize);
        var dueRange = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync(
            $"/api/credits?dueFrom={old:yyyy-MM-dd}&dueTo={old:yyyy-MM-dd}"), HttpStatusCode.OK);
        Assert.Equal(first.CreditId, Assert.Single(dueRange.Items).Id);
        await AssertProblemAsync(await client.GetAsync(
            $"/api/credits?dueFrom={old.AddDays(1):yyyy-MM-dd}&dueTo={old:yyyy-MM-dd}"), HttpStatusCode.BadRequest);
        var byNumber = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync($"/api/credits?saleNumber={Uri.EscapeDataString(first.VisibleNumber)}"), HttpStatusCode.OK);
        Assert.Equal(first.CreditId, Assert.Single(byNumber.Items).Id);
        var prefix = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync("/api/credits?saleNumber=VTA"), HttpStatusCode.OK);
        Assert.Equal(2, prefix.TotalCount);
        var visiblePrefix = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync(
            "/api/credits?saleNumber=VTA-CRD1"), HttpStatusCode.OK);
        Assert.Equal(2, visiblePrefix.TotalCount);
        var page1 = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync("/api/credits?pageSize=1&page=1"), HttpStatusCode.OK);
        var page2 = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync("/api/credits?pageSize=1&page=2"), HttpStatusCode.OK);
        Assert.NotEqual(Assert.Single(page1.Items).Id, Assert.Single(page2.Items).Id);
        var repeat = await ReadAsync<PagedResult<CreditDto>>(await client.GetAsync("/api/credits?pageSize=1&page=1"), HttpStatusCode.OK);
        Assert.Equal(Assert.Single(page1.Items).Id, Assert.Single(repeat.Items).Id);
        Assert.Equal(before, await SnapshotAsync(first.CreditId!.Value, setup.ProductId));

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var disable = new NpgsqlCommand("SET enable_seqscan = off", connection))
            await disable.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand("EXPLAIN SELECT id FROM ventas.credito WHERE estado = 'PENDIENTE' AND fecha_vencimiento < CURRENT_DATE", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var planLines = new List<string>();
        while (await reader.ReadAsync()) planLines.Add(reader.GetString(0));
        var plan = string.Join('\n', planLines);
        Assert.Contains("ix_credito_estado_vencimiento", plan);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task AboNumbering_ConcurrentUpdatesWithSameVersion_HaveOneWinner()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var current = await ReadAsync<SalesDocumentNumberingDto>(await client.GetAsync(
            "/api/document-numbering/credit-payments"), HttpStatusCode.OK);
        using var firstClient = CreateClient(setup.Admin.AccessToken);
        using var secondClient = CreateClient(setup.Admin.AccessToken);
        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await LockInstallationAsync(blocker, transaction);
        var first = firstClient.PutAsJsonAsync("/api/document-numbering/credit-payments",
            new UpdateSalesDocumentNumberingRequest("ABO", 10, current.Version));
        var second = secondClient.PutAsJsonAsync("/api/document-numbering/credit-payments",
            new UpdateSalesDocumentNumberingRequest("ABO", 20, current.Version));
        await WaitForInstallationWaitersAsync(2);
        await transaction.CommitAsync();
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Conflict);
        var persisted = await ReadAsync<SalesDocumentNumberingDto>(await client.GetAsync(
            "/api/document-numbering/credit-payments"), HttpStatusCode.OK);
        Assert.Equal(2, persisted.Version);
        Assert.Contains(persisted.NextNumber, new long[] { 10, 20 });
    }

    [Fact, Trait("Category", "Integration")]
    public async Task OverdueAndInactiveCustomerStillAcceptPayment_WhileAnnulledAndStaleVersionReject()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var overdueSale = await CreateCreditAsync(client, setup, "credit-overdue-sale");
        var annulledSale = await CreateCreditAsync(client, setup, "credit-annulled-sale");
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var overdue = await db.Credito.SingleAsync(x => x.Id == overdueSale.CreditId);
            overdue.FechaOtorgamiento = setup.Shift.OperatingDate.AddDays(-3);
            overdue.FechaVencimiento = setup.Shift.OperatingDate.AddDays(-2);
            var annulled = await db.Credito.SingleAsync(x => x.Id == annulledSale.CreditId);
            annulled.Estado = "ANULADO";
            var customer = await db.Cliente.SingleAsync(x => x.Id == setup.CustomerId);
            customer.Activo = false;
            await db.SaveChangesAsync();
        }
        var overdueDto = await ReadAsync<CreditDto>(await client.GetAsync($"/api/credits/{overdueSale.CreditId}"), HttpStatusCode.OK);
        var partial = await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, overdueDto.Id,
            "overdue-inactive-customer-payment", new ApplyCreditPaymentRequest(overdueDto.Version,
                [new("EFECTIVO", 100m, 100m, null)])), HttpStatusCode.Created);
        Assert.Equal("VENCIDO", partial.State);
        Assert.Equal(900m, partial.Balance);
        var after = await SnapshotAsync(overdueDto.Id, setup.ProductId);
        await AssertProblemAsync(await PayAsync(client, overdueDto.Id, "stale-credit-payment",
            new ApplyCreditPaymentRequest(overdueDto.Version, [new("EFECTIVO", 100m, 100m, null)])), HttpStatusCode.Conflict);
        Assert.Equal(after, await SnapshotAsync(overdueDto.Id, setup.ProductId));
        await AssertProblemAsync(await PayAsync(client, annulledSale.CreditId!.Value, "annulled-credit-payment",
            new ApplyCreditPaymentRequest(2, [new("EFECTIVO", 100m, 100m, null)])), HttpStatusCode.Conflict);
        Assert.Equal(0, await PaymentCountAsync(annulledSale.CreditId.Value));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ConcurrentIdenticalIdempotencyKey_WritesOnePaymentAndReturnsSameOperation()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var creditId = (await CreateCreditAsync(client, setup, "credit-same-key-sale")).CreditId!.Value;
        using var firstClient = CreateClient(setup.Admin.AccessToken);
        using var secondClient = CreateClient(setup.Admin.AccessToken);
        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await LockInstallationAsync(blocker, transaction);
        var request = new ApplyCreditPaymentRequest(1, [new CreditPaymentMethodRequest("EFECTIVO", 100m, 100m, null)]);
        var first = PayAsync(firstClient, creditId, "concurrent-identical-credit-key", request);
        var second = PayAsync(secondClient, creditId, "concurrent-identical-credit-key", request);
        await WaitForInstallationWaitersAsync(2);
        await transaction.CommitAsync();
        var responses = await Task.WhenAll(first, second);
        var a = await ReadAsync<CreditPaymentOperationDto>(responses[0], HttpStatusCode.Created);
        var b = await ReadAsync<CreditPaymentOperationDto>(responses[1], HttpStatusCode.Created);
        Assert.Equal(a.CorrelationId, b.CorrelationId);
        Assert.Equal(Assert.Single(a.Payments).Id, Assert.Single(b.Payments).Id);
        Assert.Equal(1, await PaymentCountAsync(creditId));
        Assert.Equal(900m, (await ReadAsync<CreditDto>(await client.GetAsync($"/api/credits/{creditId}"),
            HttpStatusCode.OK)).Balance);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task SharedShiftDeferredFailure_PreservesOperatorSessionAndUnrelatedCashSaleInventoryRows()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client, shared: true);
        var creditId = (await CreateCreditAsync(client, setup, "shared-credit-sale")).CreditId!.Value;
        var session = await StartAdminSessionAsync(client, setup);
        SetBearer(client, setup.Admin.AccessToken);
        await ReadAsync<CashMovementDto>(await client.PostAsJsonAsync("/api/cash/movements/incomes",
            new CreateCashMovementRequest(25m, "Ingreso ajeno a abono", session.Id)), HttpStatusCode.Created);
        var before = await SnapshotAsync(creditId, setup.ProductId);
        var sessionBefore = await SessionSnapshotAsync(session.Id);
        await InstallDeferredPaymentFailureAsync();

        await AssertProblemAsync(await PayAsync(client, creditId, "shared-deferred-payment",
            new ApplyCreditPaymentRequest(1, [new("EFECTIVO", 100m, 100m, null)])), HttpStatusCode.Conflict);
        Assert.Equal(before, await SnapshotAsync(creditId, setup.ProductId));
        Assert.Equal(sessionBefore, await SessionSnapshotAsync(session.Id));
        Assert.Equal("ACTIVA", sessionBefore.State);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ReportCashFlowCoverage_CreditPaymentUsesPaymentTurnDateInsteadOfSaleDate()
    {
        using var client = CreateClient();
        var setup = await BootstrapAsync(client);
        var sale = await CreateCreditAsync(client, setup, "report-later-credit-sale");
        var reconciliation = await ReadAsync<CashReconciliationDto>(
            await client.GetAsync("/api/cash/reconciliation"), HttpStatusCode.OK);
        Assert.Equal(0m, reconciliation.TotalExpected);
        await ReadAsync<CashReconciliationDto>(await client.PostAsJsonAsync(
            $"/api/cash/shifts/{setup.Shift.Id}/close", new CloseCashShiftRequest(
                reconciliation.ShiftVersion, reconciliation.PaymentMethods.Select(x =>
                    new CountedPaymentMethodRequest(x.PaymentMethodId, x.ExpectedAmount)).ToArray(),
                null, null, null)), HttpStatusCode.OK);
        var next = await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Turno posterior a la venta a credito")), HttpStatusCode.Created);
        var paymentDate = setup.Shift.OperatingDate.AddDays(1);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        await db.TurnoCaja.Where(x => x.Id == next.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FechaOperativa, paymentDate));
        await ReadAsync<CreditPaymentOperationDto>(await PayAsync(client, sale.CreditId!.Value,
            "report-later-credit-payment", new ApplyCreditPaymentRequest(1,
                [new("EFECTIVO", 200m, 250m, null), new("TRANSFERENCIA", 300m, null, "REPORT-LATER-ABO")])),
            HttpStatusCode.Created);

        var entries = await scope.ServiceProvider.GetRequiredService<ControlPlus.Application.Reports.IDailyCashFlowRepository>()
            .GetEntriesAsync(await db.Caja.Select(x => x.InstalacionId).SingleAsync(),
                setup.Shift.OperatingDate, paymentDate, default);
        Assert.DoesNotContain(entries, x => x.Date == setup.Shift.OperatingDate && x.Method is not null);
        Assert.Equal(new ControlPlus.Application.Reports.CashFlowEntry(paymentDate, "ABONO_CREDITO", "EFECTIVO", 200m),
            Assert.Single(entries, x => x.Method == "EFECTIVO"));
        Assert.Equal(new ControlPlus.Application.Reports.CashFlowEntry(paymentDate, "ABONO_CREDITO", "TRANSFERENCIA", 300m),
            Assert.Single(entries, x => x.Method == "TRANSFERENCIA"));
        Assert.DoesNotContain(entries, x => x.Method is null && x.Origin != "APERTURA");
        var payments = await db.PagoCredito.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, payments.Length);
        Assert.All(payments, x => Assert.Equal(next.Id, x.TurnoCajaId));
        Assert.Single(payments.Select(x => x.MovimientoCajaId).Distinct());
        var expected = await scope.ServiceProvider.GetRequiredService<ControlPlus.Application.Cash.Ports.ICashRepository>()
            .GetExpectedAmountsAsync(next.Id);
        Assert.All(expected, x => Assert.Equal(x.Code switch
        {
            "EFECTIVO" => 200m,
            "TRANSFERENCIA" => 300m,
            _ => 0m
        }, x.ExpectedAmount));
        Assert.Equal(expected.Sum(x => x.ExpectedAmount), entries.Sum(x => x.Amount));
    }

    private async Task<Setup> BootstrapAsync(HttpClient client, bool configureAbo = true, bool shared = false)
    {
        using var setupRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest(
                "Comercio ficticio de Créditos", "CREDIT-TEST", "CREDIT-INSTALLATION", "CRD1",
                "CREDIT-TERMINAL", "Terminal ficticia de Créditos", AdminName,
                "Administrador ficticio de Créditos", _adminPassword))
        };
        setupRequest.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setupRequest)).StatusCode);
        var admin = await LoginAsync(client, AdminName, _adminPassword);
        SetBearer(client, admin.AccessToken);
        var roles = await ReadAsync<PagedResult<RoleDto>>(await client.GetAsync("/api/roles?page=1&pageSize=20"), HttpStatusCode.OK);
        var supervisorRole = roles.Items.Single(x => x.Code == RoleCodes.Supervisor).Id;
        var cashierRole = roles.Items.Single(x => x.Code == RoleCodes.Cashier).Id;
        var supervisorUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(SupervisorName, "Supervisor ficticio de Créditos", _supervisorPassword,
                [supervisorRole])), HttpStatusCode.Created);
        var cashierUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users",
            new CreateUserRequest(CashierName, "Cajero ficticio de Créditos", _cashierPassword,
                [cashierRole])), HttpStatusCode.Created);
        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Categoría ficticia de Créditos", null)), HttpStatusCode.Created);
        var units = await ReadAsync<IReadOnlyCollection<MeasurementUnitDto>>(
            await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        var product = await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, units.Single(x => x.Code == "UNIDAD").Id, null,
                "CREDIT-PRODUCT", null, null, true, "Producto ficticio de Créditos", null,
                1_000m, null, null, 0)), HttpStatusCode.Created);
        var customer = await ReadAsync<CustomerDto>(await client.PostAsJsonAsync("/api/customers",
            new CreateCustomerRequest("CC", "CREDIT-CUSTOMER-1", "Cliente privado de Créditos",
                "3005550199", "Dirección privada", "private@example.com", "Observaciones privadas")), HttpStatusCode.Created);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var row = await db.Producto.SingleAsync(x => x.Id == product.Id);
            row.StockActual = 100;
            row.CostoPromedio = 500m;
            await db.SaveChangesAsync();
        }
        await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync("/api/document-numbering/sales",
            new CreateSalesDocumentNumberingRequest("VTA", 1)), HttpStatusCode.Created);
        if (configureAbo)
            await ReadAsync<SalesDocumentNumberingDto>(await client.PostAsJsonAsync(
                "/api/document-numbering/credit-payments", new CreateSalesDocumentNumberingRequest("ABO", 1)),
                HttpStatusCode.Created);
        await ReadAsync<CashRegisterDto>(await client.PostAsJsonAsync("/api/cash/register",
            new ConfigureCashRegisterRequest("CREDIT-CASH", "Caja ficticia de Créditos")), HttpStatusCode.Created);
        if (shared)
        {
            var state = await ReadAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
            await ReadAsync<CashStateDto>(await client.PutAsJsonAsync("/api/cash/shift-mode",
                new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)),
                HttpStatusCode.OK);
        }
        var shift = await ReadAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Turno de Créditos")), HttpStatusCode.Created);
        var supervisor = await LoginAsync(client, SupervisorName, _supervisorPassword);
        var cashier = await LoginAsync(client, CashierName, _cashierPassword);
        SetBearer(client, admin.AccessToken);
        return new Setup(admin, supervisor, cashier, supervisorUser, cashierUser, product.Id, customer.Id, shift, shared);
    }

    private async Task<SaleDto> CreateCreditAsync(HttpClient client, Setup setup, string key)
    {
        if (setup.Shared) await StartAdminSessionAsync(client, setup);
        SetBearer(client, setup.Admin.AccessToken);
        var request = new ConfirmSaleRequest(setup.CustomerId,
            [new SaleLineRequest(setup.ProductId, 1, "MINORISTA")], null, null, [],
            "CREDITO", setup.Shift.OperatingDate.AddDays(10),
            new SaleCreditAuthorizationRequest(SupervisorName, _supervisorPassword));
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/sales") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        var sale = await ReadAsync<SaleDto>(await client.SendAsync(message), HttpStatusCode.Created);
        Assert.NotNull(sale.CreditId);
        Assert.Empty(sale.Payments);
        return sale;
    }

    private static async Task<OperatorSessionDto> StartAdminSessionAsync(HttpClient client, Setup setup)
    {
        SetBearer(client, setup.Admin.AccessToken);
        var credential = await ReadAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{setup.Admin.User.Id}", new IssueOperatorCredentialRequest(null)),
            HttpStatusCode.Created);
        return await ReadAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(credential.CredentialToken)),
            HttpStatusCode.Created);
    }

    private async Task<SessionSnapshot> SessionSnapshotAsync(Guid sessionId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>()
            .SesionOperador.AsNoTracking().SingleAsync(x => x.Id == sessionId);
        return new SessionSnapshot(session.Estado, session.FechaUltimoUso, session.FechaFin, session.MotivoCierre);
    }

    private async Task<PaymentSnapshot> SnapshotAsync(Guid creditId, Guid productId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var credit = await db.Credito.AsNoTracking().SingleAsync(x => x.Id == creditId);
        return new PaymentSnapshot(credit.SaldoPendiente, credit.Version, credit.Estado,
            await db.PagoCredito.CountAsync(x => x.CreditoId == creditId),
            await db.MovimientoCaja.CountAsync(), await db.OperacionIdempotente.CountAsync(),
            await db.EventoAuditoria.CountAsync(), await db.ConsecutivoDocumento.AsNoTracking()
                .Where(x => x.TipoDocumento == "PAGO_CREDITO").Select(x => (long?)x.UltimoNumero).SingleOrDefaultAsync(),
            await db.Venta.CountAsync(), await db.MovimientoInventario.CountAsync(),
            await db.AutorizacionOperacion.CountAsync(),
            await db.Producto.Where(x => x.Id == productId).Select(x => x.StockActual).SingleAsync());
    }

    private async Task<int> PaymentCountAsync(Guid creditId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>()
            .PagoCredito.CountAsync(x => x.CreditoId == creditId);
    }

    private static async Task<HttpResponseMessage> PayAsync(HttpClient client, Guid creditId,
        string key, ApplyCreditPaymentRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/credits/{creditId}/payments")
        { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private static async Task<HttpResponseMessage> RawJsonAsync(HttpClient client, HttpMethod method,
        string path, object payload, string? key = null)
    {
        using var message = new HttpRequestMessage(method, path)
        { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        if (key is not null) message.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(message);
    }

    private async Task InstallDeferredPaymentFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE auditoria.test_credit_payment_observed;
            CREATE FUNCTION auditoria.test_fail_credit_payment_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.accion = 'CREDITPAYMENTAPPLIED' THEN
                PERFORM nextval('auditoria.test_credit_payment_observed');
                RAISE EXCEPTION 'Forced isolated credit payment failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_credit_payment_commit
              AFTER INSERT ON auditoria.evento_auditoria DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION auditoria.test_fail_credit_payment_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DeferredPaymentFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT is_called FROM auditoria.test_credit_payment_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task LockInstallationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id FROM configuracion.instalacion WHERE activo
            ORDER BY fecha_instalacion, id LIMIT 1 FOR UPDATE
            """, connection, transaction);
        Assert.NotNull(await command.ExecuteScalarAsync());
    }

    private async Task WaitForInstallationWaitersAsync(int expected)
    {
        await using var observer = new NpgsqlConnection(_postgres.GetConnectionString());
        await observer.OpenAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                  AND query LIKE '%FROM configuracion.instalacion AS i%'
                  AND query LIKE '%FOR UPDATE%'
                """, observer);
            if ((long)(await command.ExecuteScalarAsync())! >= expected) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"No se observaron {expected} operaciones esperando la instalación.");
    }

    private HttpClient CreateClient(string? token = null)
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        if (token is not null) SetBearer(client, token);
        return client;
    }

    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string user, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(user, password)), HttpStatusCode.OK);
    }

    private static async Task<Guid> PermissionIdAsync(HttpClient client, string code)
    {
        var permissions = await ReadAsync<PagedResult<PermissionDto>>(
            await client.GetAsync("/api/permissions?page=1&pageSize=100"), HttpStatusCode.OK);
        return permissions.Items.Single(x => x.Code == code).Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} ({expected}), received {(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
        var value = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        return value;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected} ({expected}), received {(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.True(problem.RootElement.TryGetProperty("code", out _));
    }

    private static async Task<string> ReadAuditTextAsync(OfficialControlPlusDbContext db)
    {
        var audits = await db.EventoAuditoria.AsNoTracking().ToArrayAsync();
        var text = new StringBuilder();
        foreach (var audit in audits)
        {
            foreach (var data in new[] { audit.DatosAnteriores, audit.DatosNuevos })
            {
                if (data is null) continue;
                using var json = JsonDocument.Parse(data);
                text.Append(json.RootElement.GetRawText());
            }
            text.Append(audit.Motivo);
        }
        return text.ToString();
    }

    private static void AssertPrivateCreditJson(string body)
    {
        using var json = JsonDocument.Parse(body);
        foreach (var field in new[] { "address", "email", "observations", "audit", "authorization", "password" })
            Assert.False(json.RootElement.TryGetProperty(field, out _));
        foreach (var value in new[] { "Dirección privada", "private@example.com", "Observaciones privadas" })
            Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string Secret(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private sealed record Setup(AuthenticationResult Admin, AuthenticationResult Supervisor,
        AuthenticationResult Cashier, UserDto SupervisorUser, UserDto CashierUser,
        Guid ProductId, Guid CustomerId, CashShiftDto Shift, bool Shared);

    private sealed record PaymentSnapshot(decimal Balance, long Version, string State,
        int PaymentRows, int CashMovementRows, int IdempotencyRows, int AuditRows, long? LastAbo,
        int SaleRows, int InventoryRows, int AuthorizationRows, int Stock);

    private sealed record SessionSnapshot(string State, DateTime LastUsed, DateTime? Ended, string? CloseReason);
}
