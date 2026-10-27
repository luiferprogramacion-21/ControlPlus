using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CatalogAutomaticInternalCodeApiTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task InitialInventory_IsAtomicAndReplayReturnsOriginalProduct()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var request = ProductRequest(scenario, "INITIAL-1") with { InitialQuantity = 7, InitialUnitCost = 350.1250m };
        using var first = await PostInitialAsync(client, request, "initial-product-1");
        var product = await ReadProductAsync(first);
        Assert.Equal(7, product.CurrentStock);
        Assert.Equal(350.1250m, product.AverageCost);
        using var replay = await PostInitialAsync(client, request, "initial-product-1");
        var repeated = await ReadProductAsync(replay);
        Assert.Equal(product, repeated);
        Assert.Equal(HttpStatusCode.Conflict,
            (await PostInitialAsync(client, request with { InitialQuantity = 8 }, "initial-product-1")).StatusCode);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var stored = await db.Producto.AsNoTracking().SingleAsync();
        Assert.Equal(7, stored.StockActual);
        Assert.Equal(350.1250m, stored.CostoPromedio);
        var movement = await db.MovimientoInventario.AsNoTracking().SingleAsync();
        Assert.Equal("INVENTARIO_INICIAL", movement.TipoMovimiento);
        Assert.Equal(0, movement.StockAnterior);
        Assert.Equal(7, movement.StockPosterior);
        Assert.Null(movement.CostoPromedioAnterior);
        Assert.Equal(350.1250m, movement.CostoPromedioPosterior);
        Assert.Null(movement.CompraId);
        var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x => x.EntidadId == product.Id);
        var operation = await db.OperacionIdempotente.AsNoTracking().SingleAsync(x => x.EntidadId == product.Id);
        Assert.Equal(movement.CorrelacionId, audit.CorrelacionId);
        Assert.Equal(movement.CorrelacionId, operation.CorrelacionId);
        Assert.DoesNotContain("InitialUnitCost", audit.DatosNuevos);
    }

    [Fact, Trait("Category", "Integration")]
    public async Task ZeroInitialCost_IsKnownAndRecorded()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        using var response = await PostInitialAsync(client,
            ProductRequest(scenario, "INITIAL-ZERO-COST") with { InitialQuantity = 1, InitialUnitCost = 0m },
            "initial-zero-cost");
        var product = await ReadProductAsync(response);
        Assert.Equal(0m, product.AverageCost);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var movement = await db.MovimientoInventario.AsNoTracking().SingleAsync(x => x.ProductoId == product.Id);
        Assert.Null(movement.CostoPromedioAnterior);
        Assert.Equal(0m, movement.CostoPromedioPosterior);
    }

    [Theory, Trait("Category", "Integration")]
    [InlineData(-1, "10", true)]
    [InlineData(1, null, true)]
    [InlineData(1, "-1", true)]
    [InlineData(1, "1.12345", true)]
    [InlineData(1, "100000000000000", true)]
    [InlineData(0, "1", true)]
    [InlineData(1, "10", false)]
    public async Task InvalidInitialInventory_DoesNotWrite(int quantity, string? cost, bool keyProvided)
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        var request = ProductRequest(scenario, "INVALID-INITIAL") with
        {
            InitialQuantity = quantity,
            InitialUnitCost = cost is null ? null : decimal.Parse(cost, CultureInfo.InvariantCulture)
        };
        using var response = await PostInitialAsync(client, request, keyProvided ? "invalid-initial" : null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(0, await db.Producto.CountAsync());
        Assert.Equal(0, await db.MovimientoInventario.CountAsync());
        Assert.Equal(0, await db.OperacionIdempotente.CountAsync());
    }

    [Fact, Trait("Category", "Integration")]
    public async Task DeferredFailure_RollsBackProductMovementAuditAndIdempotency()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var client = scenario.Client;
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                CREATE FUNCTION compras.test_fail_initial_product_commit() RETURNS trigger LANGUAGE plpgsql AS $body$
                BEGIN
                    IF NEW.tipo_movimiento = 'INVENTARIO_INICIAL' THEN
                        RAISE EXCEPTION 'forced initial inventory failure' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $body$;
                CREATE CONSTRAINT TRIGGER zz_test_fail_initial_product_commit
                    AFTER INSERT ON compras.movimiento_inventario DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION compras.test_fail_initial_product_commit();
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        var request = ProductRequest(scenario, "INITIAL-ROLLBACK") with { InitialQuantity = 2, InitialUnitCost = 100m };
        using var response = await PostInitialAsync(client, request, "initial-rollback");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(0, await db.Producto.CountAsync());
        Assert.Equal(0, await db.MovimientoInventario.CountAsync());
        Assert.Equal(0, await db.OperacionIdempotente.CountAsync());
        Assert.Equal(0, await db.EventoAuditoria.CountAsync(x => x.Entidad == "Producto"));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task Supervisor_CanRegisterInitialInventoryWithoutInventoryAdjust()
    {
        var scenario = await CreateValidationScenarioAsync();
        using var manager = scenario.Client;
        var roles = (await manager.GetFromJsonAsync<PagedResult<RoleDto>>("/api/roles?page=1&pageSize=20"))!;
        var supervisorRole = roles.Items.Single(x => x.Code == RoleCodes.Supervisor);
        var created = await manager.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "catalog.initial.supervisor", "Supervisor inicial", "Catalog-Initial-Supervisor-2026!", [supervisorRole.Id]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var permissions = (await manager.GetFromJsonAsync<PagedResult<PermissionDto>>("/api/permissions?page=1&pageSize=100"))!;
        var adjust = permissions.Items.Single(x => x.Code == PermissionCodes.InventoryAdjust);
        var user = (await created.Content.ReadFromJsonAsync<UserDto>())!;
        var effective = (await manager.GetFromJsonAsync<EffectiveUserPermissionDto[]>(
            $"/api/users/{user.Id}/permissions/effective"))!;
        Assert.False(effective.Single(x => x.Id == adjust.Id).Granted);
        using var supervisor = _factory!.CreateClient();
        await LoginValidationActorAsync(supervisor, "catalog.initial.supervisor", "Catalog-Initial-Supervisor-2026!");
        using var response = await PostInitialAsync(supervisor,
            ProductRequest(scenario, "SUP-INITIAL") with { InitialQuantity = 3, InitialUnitCost = 20m }, "supervisor-initial");
        var product = await ReadProductAsync(response);
        Assert.Equal(3, product.CurrentStock);
        Assert.Null(product.AverageCost);
        Assert.False(product.CanViewCost);
    }

    private static Task<HttpResponseMessage> PostInitialAsync(HttpClient client, CreateProductRequest request, string? key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/products") { Content = JsonContent.Create(request) };
        if (key is not null) message.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(message);
    }
}
