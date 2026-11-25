using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class SalesApiFlowTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReceiptSnapshot_NewSaleIsImmutableAndCatalogChangesDoNotAlterReceipt()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        var customerId = await CreateCustomerAsync(scenario.Admin.User.Id, true, "SNAPSHOT-PRIVATE");
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        SetBearer(client, scenario.Admin.AccessToken);

        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "snapshot-historical-sale",
            new ConfirmSaleRequest(customerId,
                [new(scenario.Products.Standard, 1, "MINORISTA")], null, null,
                [new("EFECTIVO", 400m, 400m, null),
                    new("TRANSFERENCIA", 600m, null, "SNAPSHOT-TEST-REFERENCE")])),
            HttpStatusCode.Created);
        var url = $"/api/sales/{sale.Id}/receipt";
        var before = await ReadAsync<SaleReceiptDto>(await client.GetAsync(url), HttpStatusCode.OK);
        Assert.Null(before.VatPercentage);
        Assert.Null(before.TaxableBaseTotal);
        Assert.Null(before.VatTotal);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var document = await db.DocumentoEmitido.AsNoTracking().SingleAsync(x => x.EntidadId == sale.Id);
        Assert.Equal(1, document.VersionDocumento);
        Assert.Equal(sale.VisibleNumber, document.NumeroVisible);
        Assert.Null(document.PlantillaSnapshot);
        Assert.Equal(64, document.HashSnapshot.Length);
        Assert.Empty(await db.TrabajoImpresion.Where(x => x.DocumentoEmitidoId == document.Id).ToArrayAsync());
        foreach (var sensitive in new[] { AdminPassword, _masterKey, _jwtKey, scenario.Admin.AccessToken,
                     "SNAPSHOT-TEST-REFERENCE", customerId.ToString() })
            Assert.DoesNotContain(sensitive, document.DatosSnapshot, StringComparison.Ordinal);
        Assert.Equal("Cliente ficticio SNAPSHOT-PRIVATE", before.CustomerName);
        Assert.Equal(scenario.Admin.User.DisplayName, before.OperatorName);
        foreach (var sensitiveField in new[] { "password", "accessToken", "authorization", "reference",
                     "customerId", "unitCost", "costoUnitario" })
            Assert.DoesNotContain($"\"{sensitiveField}\"", document.DatosSnapshot, StringComparison.OrdinalIgnoreCase);

        using (var data = JsonDocument.Parse(document.DatosSnapshot))
        {
            Assert.Equal(1, data.RootElement.GetProperty("schemaVersion").GetInt32());
            var historical = data.RootElement.GetProperty("receipt").Deserialize<SaleReceiptDto>(
                JsonSerializerOptions.Web);
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(historical));
            var methods = data.RootElement.GetProperty("paymentMethods").EnumerateArray().ToArray();
            Assert.Contains(methods, x => x.GetProperty("code").GetString() == "EFECTIVO" &&
                !string.IsNullOrWhiteSpace(x.GetProperty("name").GetString()));
            Assert.Contains(methods, x => x.GetProperty("code").GetString() == "TRANSFERENCIA" &&
                !string.IsNullOrWhiteSpace(x.GetProperty("name").GetString()));
        }

        await db.Producto.Where(x => x.Id == scenario.Products.Standard)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Nombre, "Nombre actualizado después de la venta"));
        await db.MetodoPago.Where(x => x.Codigo == "EFECTIVO")
            .ExecuteUpdateAsync(x => x.SetProperty(m => m.Codigo, "EFECTIVO-POSTERIOR")
                .SetProperty(m => m.Nombre, "Nombre posterior del medio"));

        var after = await ReadAsync<SaleReceiptDto>(await client.GetAsync(url), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        var persisted = await db.DocumentoEmitido.AsNoTracking().SingleAsync(x => x.Id == document.Id);
        Assert.Equal(document.DatosSnapshot, persisted.DatosSnapshot);
        Assert.Equal(document.HashSnapshot, persisted.HashSnapshot);
        Assert.Single(await db.DocumentoEmitido.Where(x => x.EntidadId == sale.Id).ToArrayAsync());

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE configuracion.documento_emitido SET hash_snapshot = 'alterado' WHERE id = {0}", document.Id));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM configuracion.documento_emitido WHERE id = {0}", document.Id));

        // Corruption is simulated only inside this disposable test database. The read must
        // fail rather than silently fall back to mutable catalog data.
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE configuracion.documento_emitido DISABLE TRIGGER USER");
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE configuracion.documento_emitido SET hash_snapshot = 'alterado' WHERE id = {0}", document.Id);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE configuracion.documento_emitido ENABLE TRIGGER USER");
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReceiptSnapshot_ConcurrentReplayCreatesOneDocumentAndRejectedSaleCreatesNone()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        using var firstClient = CreateClient(scenario.Admin.AccessToken);
        using var secondClient = CreateClient(scenario.Admin.AccessToken);
        var responses = await Task.WhenAll(
            PostSaleAsync(firstClient, "snapshot-concurrent-replay",
                CashRequest(scenario.Products.Standard, 1, 1_000m)),
            PostSaleAsync(secondClient, "snapshot-concurrent-replay",
                CashRequest(scenario.Products.Standard, 1, 1_000m)));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        var sales = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<SaleDto>()));
        var saleId = Assert.Single(sales.Select(x => x!.Id).Distinct());

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Single(await db.DocumentoEmitido.Where(x => x.EntidadId == saleId).ToArrayAsync());
        Assert.Equal(1, await db.Venta.CountAsync(x => x.Id == saleId));
        Assert.Equal(1, await db.MovimientoCaja.CountAsync(x =>
            x.PagoVenta.Any(payment => payment.VentaId == saleId)));
        Assert.Empty(await db.TrabajoImpresion.ToArrayAsync());

        SetBearer(client, scenario.Admin.AccessToken);
        var beforeRejected = await ReadReceiptPersistenceStateAsync(db, scenario.Products.Standard);
        var rejected = await PostSaleAsync(client, "snapshot-rejected-no-session",
            CashRequest(scenario.Products.Standard, 1, 1_000m));
        await AssertProblemAsync(rejected, HttpStatusCode.Conflict);
        using (var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()))
            // 25/11/2026: el mensaje dice qué hacer cuando nadie ha pasado su carné en turno compartido.
            Assert.Contains(SharedShift.CardRequiredMessage, problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(beforeRejected, await ReadReceiptPersistenceStateAsync(db, scenario.Products.Standard));

        var rollbackSession = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        Assert.Equal("ACTIVA", await db.SesionOperador.AsNoTracking()
            .Where(x => x.Id == rollbackSession.Id).Select(x => x.Estado).SingleAsync());
        var beforeFailedCommit = await ReadReceiptPersistenceStateAsync(db, scenario.Products.Standard);
        // Sequence increments survive rollback. This probe belongs only to the disposable
        // fixture and proves that the intended deferred trigger actually ran at COMMIT.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE SEQUENCE configuracion.snapshot_failure_probe");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION configuracion.rechazar_snapshot_prueba() RETURNS trigger
            LANGUAGE plpgsql AS $body$ BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM ventas.venta v
                    JOIN caja.sesion_operador s ON s.id = v.sesion_operador_id
                    WHERE v.id = NEW.entidad_id AND v.estado = 'CONFIRMADA'
                      AND s.estado = 'CERRADA' AND s.motivo_cierre = 'CAMBIO_OPERADOR'
                ) THEN
                    RAISE EXCEPTION USING ERRCODE = 'XX000',
                        MESSAGE = 'El fixture no alcanzó la venta y sesión guardadas';
                END IF;
                PERFORM nextval('configuracion.snapshot_failure_probe');
                RAISE EXCEPTION USING ERRCODE = 'P0001',
                    MESSAGE = 'Fallo controlado del snapshot';
            END; $body$;
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE CONSTRAINT TRIGGER tg_rechazar_snapshot_prueba
            AFTER INSERT ON configuracion.documento_emitido
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION configuracion.rechazar_snapshot_prueba();
            """);
        try
        {
            var failed = await PostSaleAsync(client, "snapshot-insert-fails",
                CashRequest(scenario.Products.Standard, 1, 1_000m));
            await AssertProblemAsync(failed, HttpStatusCode.Conflict);
            var body = await failed.Content.ReadAsStringAsync();
            using (var problem = JsonDocument.Parse(body))
                Assert.Equal("resource.conflict", problem.RootElement.GetProperty("code").GetString());
            foreach (var internalDetail in new[] { "P0001", "Fallo controlado", "rechazar_snapshot_prueba",
                         "documento_emitido", "snapshot_failure_probe" })
                Assert.DoesNotContain(internalDetail, body, StringComparison.OrdinalIgnoreCase);

            await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT last_value, is_called FROM configuracion.snapshot_failure_probe", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(1), "No se alcanzó el trigger diferido de fallo del snapshot.");
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(beforeFailedCommit,
                await ReadReceiptPersistenceStateAsync(db, scenario.Products.Standard));
            Assert.False(await db.OperacionIdempotente.AnyAsync(x => x.ClaveIdempotencia == "snapshot-insert-fails"));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER tg_rechazar_snapshot_prueba ON configuracion.documento_emitido");
            await db.Database.ExecuteSqlRawAsync(
                "DROP FUNCTION configuracion.rechazar_snapshot_prueba()");
            await db.Database.ExecuteSqlRawAsync(
                "DROP SEQUENCE configuracion.snapshot_failure_probe");
        }
    }

    private static async Task<string> ReadReceiptPersistenceStateAsync(
        OfficialControlPlusDbContext db, Guid productId) => JsonSerializer.Serialize(new
    {
        Sales = await db.Venta.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Total, x.Estado, x.Version, x.SesionOperadorId }).ToArrayAsync(),
        Details = await db.DetalleVenta.CountAsync(),
        Payments = await db.PagoVenta.CountAsync(),
        CashMovements = await db.MovimientoCaja.CountAsync(),
        InventoryMovements = await db.MovimientoInventario.CountAsync(),
        Documents = await db.DocumentoEmitido.CountAsync(),
        PrintJobs = await db.TrabajoImpresion.CountAsync(),
        Idempotency = await db.OperacionIdempotente.CountAsync(),
        Audit = await db.EventoAuditoria.CountAsync(),
        Product = await db.Producto.AsNoTracking().Where(x => x.Id == productId)
            .Select(x => new { x.StockActual, x.StockReservado, x.CostoPromedio, x.Version }).SingleAsync(),
        Numbering = await db.ConsecutivoDocumento.AsNoTracking().Where(x => x.TipoDocumento == "VENTA")
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.UltimoNumero, x.Version }).ToArrayAsync(),
        Sessions = await db.SesionOperador.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Estado, x.FechaUltimoUso, x.FechaFin, x.MotivoCierre }).ToArrayAsync()
    });

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReceiptSnapshot_OlderSaleWithoutDocumentRemainsReadableWithoutBackfill()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken,
            scenario.Admin.AccessToken, scenario.Admin.User.Id);
        SetBearer(client, scenario.Admin.AccessToken);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "snapshot-legacy-sale",
            CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Created);

        // This fixture owns an isolated database. Remove the newly created document only to
        // model a sale from before this cut; normal writes still enforce the official trigger.
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE configuracion.documento_emitido DISABLE TRIGGER USER");
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM configuracion.documento_emitido WHERE entidad_id = {0}", sale.Id);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE configuracion.documento_emitido ENABLE TRIGGER USER");
        }

        var url = $"/api/sales/{sale.Id}/receipt";
        var first = await ReadAsync<SaleReceiptDto>(await client.GetAsync(url), HttpStatusCode.OK);
        var second = await ReadAsync<SaleReceiptDto>(await client.GetAsync(url), HttpStatusCode.OK);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(sale.VisibleNumber, first.VisibleNumber);
        Assert.Null(first.OperatorName);
        Assert.Null(first.CustomerName);
        Assert.Equal(0, await db.DocumentoEmitido.CountAsync(x => x.EntidadId == sale.Id));

        SetBearer(client, scenario.SecondCashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        using (var forbiddenRequest = new HttpRequestMessage(HttpMethod.Post,
                   $"/api/sales/{sale.Id}/reprint-requests"))
        {
            forbiddenRequest.Headers.Add("Idempotency-Key", "snapshot-legacy-forbidden");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(forbiddenRequest)).StatusCode);
        }
        Assert.False(await db.OperacionIdempotente.AnyAsync(x => x.ClaveIdempotencia == "snapshot-legacy-forbidden"));
        SetBearer(client, scenario.Admin.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/sales/{sale.Id}/reprint-requests");
        request.Headers.Add("Idempotency-Key", "snapshot-legacy-reprint");
        var reprint = await ReadAsync<SaleReprintRequestDto>(await client.SendAsync(request), HttpStatusCode.OK);
        Assert.Equal("SOLICITADA", reprint.Status);
        Assert.Equal(0, await db.DocumentoEmitido.CountAsync(x => x.EntidadId == sale.Id));
        Assert.Empty(await db.TrabajoImpresion.ToArrayAsync());
    }
}
