using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CatalogAutomaticInternalCodeApiTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task PhysicalDelete_RemovesOnlyOwnedRows_KeepsReplayAndAudit()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var request = ProductRequest(scenario, "DELETE-ALIAS") with { InitialQuantity = 2, InitialUnitCost = 12m };
        var product = await ReadProductAsync(await PostInitialAsync(client, request, "physical-delete-replay"));
        var update = new UpdateProductRequest(scenario.CategoryId, scenario.UnitId, null, product.InternalCode,
            product.Barcode, product.BarcodeFormat, product.BarcodeGenerated, "Producto previo al borrado", null,
            product.RetailPrice, null, null, 0, product.Version);
        using var changed = await client.PutAsJsonAsync($"/api/products/{product.Id}", update);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var current = (await changed.Content.ReadFromJsonAsync<ProductDto>())!;
        using (var stale = await client.DeleteAsync(DeleteUrl(product, product.Version)))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
            Assert.Equal("concurrency.conflict", problem.RootElement.GetProperty("code").GetString());
        }

        using var deleted = await client.DeleteAsync(DeleteUrl(current, current.Version));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(DeleteUrl(current, current.Version))).StatusCode);
        using var replay = await PostInitialAsync(client, request, "physical-delete-replay");
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        using (var problem = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()))
        {
            Assert.Equal("resource.conflict", problem.RootElement.GetProperty("code").GetString());
            Assert.Contains("El producto de esta operación fue eliminado", problem.RootElement.GetProperty("title").GetString());
        }

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.False(await db.Producto.AnyAsync(x => x.Id == product.Id));
        Assert.False(await db.MovimientoInventario.AnyAsync(x => x.ProductoId == product.Id));
        Assert.False(await db.ProductoCodigo.AnyAsync(x => x.ProductoId == product.Id));
        Assert.True(await db.OperacionIdempotente.AnyAsync(x => x.EntidadId == product.Id));
        Assert.True(await db.EventoAuditoria.AnyAsync(x => x.EntidadId == product.Id && x.Accion == "PRODUCTDELETED"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task PhysicalDelete_RejectsLaterMovement_WithoutPartialEffects()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var product = await ReadProductAsync(await PostInitialAsync(client,
            ProductRequest(scenario, "DELETE-HISTORY") with { InitialQuantity = 2, InitialUnitCost = 12m }, "delete-history"));
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var initial = await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.ProductoId == product.Id);
            db.MovimientoInventario.Add(new MovimientoInventario
            {
                Id = Guid.CreateVersion7(), ProductoId = product.Id, UsuarioId = initial.UsuarioId,
                TipoMovimiento = "REVERSO", CantidadStock = 1, CantidadReservada = 0,
                StockAnterior = 2, StockPosterior = 3, ReservadoAnterior = 0, ReservadoPosterior = 0,
                CostoPromedioAnterior = 12m, CostoPromedioPosterior = 12m,
                FechaHora = DateTime.UtcNow, CorrelacionId = Guid.CreateVersion7()
            });
            await db.SaveChangesAsync();
        }
        using var rejected = await client.DeleteAsync(DeleteUrl(product, product.Version));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using (var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync()))
        {
            Assert.Equal("resource.conflict", problem.RootElement.GetProperty("code").GetString());
            Assert.Contains("solo puede inactivarse", problem.RootElement.GetProperty("title").GetString());
        }
        await using var verify = _factory!.Services.CreateAsyncScope();
        var persisted = verify.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.True(await persisted.Producto.AnyAsync(x => x.Id == product.Id));
        Assert.Equal(2, await persisted.MovimientoInventario.CountAsync(x => x.ProductoId == product.Id));
        Assert.True(await persisted.ProductoCodigo.AnyAsync(x => x.ProductoId == product.Id));
        Assert.False(await persisted.EventoAuditoria.AnyAsync(x => x.EntidadId == product.Id && x.Accion == "PRODUCTDELETED"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task PhysicalDelete_DeferredDatabaseFailure_RollsBackOwnedRowsAndAudit()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var product = await ReadProductAsync(await PostInitialAsync(client,
            ProductRequest(scenario, "DELETE-ROLLBACK") with { InitialQuantity = 1, InitialUnitCost = 5m }, "delete-rollback"));
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                CREATE FUNCTION catalogo.test_reject_product_delete() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'forced product delete failure' USING ERRCODE = '23514';
                END $body$;
                CREATE TRIGGER zz_test_reject_product_delete BEFORE DELETE ON catalogo.producto
                    FOR EACH ROW EXECUTE FUNCTION catalogo.test_reject_product_delete();
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        using var failed = await client.DeleteAsync(DeleteUrl(product, product.Version));
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.True(await db.Producto.AnyAsync(x => x.Id == product.Id));
        Assert.True(await db.MovimientoInventario.AnyAsync(x => x.ProductoId == product.Id));
        Assert.True(await db.ProductoCodigo.AnyAsync(x => x.ProductoId == product.Id));
        Assert.False(await db.EventoAuditoria.AnyAsync(x => x.EntidadId == product.Id && x.Accion == "PRODUCTDELETED"));
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(RoleCodes.Cashier)]
    [InlineData(RoleCodes.Supervisor)]
    public async Task PhysicalDelete_RequiresAdministratorEvenWithPermission(string roleCode)
    {
        var scenario = await CreateValidationScenarioAsync();
        using var admin = scenario.Client;
        var product = await ReadProductAsync(await admin.PostAsJsonAsync("/api/products", ProductRequest(scenario, null)));
        var roles = (await admin.GetFromJsonAsync<PagedResult<RoleDto>>("/api/roles?page=1&pageSize=20"))!;
        var roleId = roles.Items.Single(x => x.Code == roleCode).Id;
        var userName = "delete." + roleCode.ToLowerInvariant();
        const string password = "Catalog-Delete-Role-2026!";
        var created = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(userName, "Operador temporal", password, [roleId]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = (await created.Content.ReadFromJsonAsync<UserDto>())!;
        var permissions = (await admin.GetFromJsonAsync<PagedResult<PermissionDto>>("/api/permissions?page=1&pageSize=100"))!;
        var sensitiveId = permissions.Items.Single(x => x.Code == PermissionCodes.ProductsSensitiveUpdate).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}/permissions/{sensitiveId}", new SetUserPermissionRequest(true))).StatusCode);
        using var operatorClient = _factory!.CreateClient();
        await LoginValidationActorAsync(operatorClient, userName, password);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync(DeleteUrl(product, product.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/products/{product.Id}")).StatusCode);
    }

    private static string DeleteUrl(ProductDto product, long version) =>
        $"/api/products/{product.Id}?expectedVersion={version}";
}
