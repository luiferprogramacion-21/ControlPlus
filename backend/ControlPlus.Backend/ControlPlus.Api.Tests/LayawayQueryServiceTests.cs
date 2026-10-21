using System.Reflection;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Application.Layaways.Services;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class LayawayQueryServiceTests
{
    [Fact]
    public async Task CurrentPermissionRevocation_PreventsListAndDetailQueries()
    {
        var userId = Guid.CreateVersion7();
        var users = Users(userId, hasPermission: false);
        var repository = new QuerySpy();
        var service = new LayawayQueryService(repository, users);
        var actor = new ActorContext(userId, RoleLevel.Administrador, [PermissionCodes.LayawayManage]);

        var list = await service.ListAsync(actor, new LayawayListQuery());
        var detail = await service.GetAsync(actor, Guid.CreateVersion7());

        Assert.Equal("authorization.forbidden", list.Error?.Code);
        Assert.Equal("authorization.forbidden", detail.Error?.Code);
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(int.MaxValue, 100)]
    [InlineData(1, 101)]
    [InlineData(0, 20)]
    public async Task InvalidPagination_IsRejectedWithoutQuery(int page, int pageSize)
    {
        var userId = Guid.CreateVersion7();
        var repository = new QuerySpy();
        var service = new LayawayQueryService(repository, Users(userId, hasPermission: true));

        var result = await service.ListAsync(new ActorContext(userId, RoleLevel.Cajero),
            new LayawayListQuery(Page: page, PageSize: pageSize));

        Assert.Equal("validation.failed", result.Error?.Code);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task UnknownState_IsRejectedBeforeQuery()
    {
        var userId = Guid.CreateVersion7();
        var repository = new QuerySpy();
        var service = new LayawayQueryService(repository, Users(userId, hasPermission: true));

        var result = await service.ListAsync(new ActorContext(userId, RoleLevel.Cajero),
            new LayawayListQuery(State: "PENDIENTE"));

        Assert.Equal("validation.failed", result.Error?.Code);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Detail_ProjectsProductsAndMixedPaymentsWithoutCosts()
    {
        var userId = Guid.CreateVersion7();
        var layawayId = Guid.CreateVersion7();
        var correlation = Guid.CreateVersion7();
        var layaway = new Apartado
        {
            Id = layawayId, Prefijo = "APT", Serie = "A", Consecutivo = 4,
            ClienteId = Guid.CreateVersion7(), Cliente = new Cliente { NombreCompleto = "Cliente real" },
            FechaHora = new DateTime(2026, 10, 18, 14, 0, 0, DateTimeKind.Utc),
            FechaLimite = new DateOnly(2026, 11, 2), Total = 1001m, AbonoInicial = 201m,
            SaldoPendiente = 800m, Estado = "ACTIVO", Version = 1, PorcentajeMinimoInicial = 20m,
            DetalleApartado =
            [
                new DetalleApartado { ProductoId = Guid.CreateVersion7(), CodigoProducto = "P-1",
                    NombreProducto = "Producto", UnidadMedida = "Unidad", Cantidad = 1,
                    PrecioReserva = 1001m, SubtotalNeto = 1001m, CostoUnitario = 777m }
            ],
            PagoApartado =
            [
                new PagoApartado { Id = Guid.CreateVersion7(), Prefijo = "PAP", Serie = "A", Consecutivo = 1,
                    MetodoPago = new MetodoPago { Codigo = "EFECTIVO", Nombre = "Efectivo" },
                    ValorAplicado = 101m, ValorRecibido = 200m, Cambio = 99m,
                    FechaHora = new DateTime(2026, 10, 18, 14, 0, 0, DateTimeKind.Utc), Estado = "APLICADO",
                    MovimientoCaja = new MovimientoCaja { CorrelacionId = correlation } },
                new PagoApartado { Id = Guid.CreateVersion7(), Prefijo = "PAP", Serie = "A", Consecutivo = 2,
                    MetodoPago = new MetodoPago { Codigo = "NEQUI", Nombre = "Nequi" },
                    ValorAplicado = 100m, FechaHora = new DateTime(2026, 10, 18, 14, 0, 1, DateTimeKind.Utc),
                    Estado = "APLICADO", MovimientoCaja = new MovimientoCaja { CorrelacionId = correlation } }
            ]
        };
        var repository = new QuerySpy { Detail = layaway };
        var service = new LayawayQueryService(repository, Users(userId, hasPermission: true));

        var result = await service.GetAsync(new ActorContext(userId, RoleLevel.Cajero), layawayId);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal("APT-A-4", dto.Layaway.Number);
        Assert.Equal(201m, dto.Layaway.InitialPayment);
        Assert.Equal(20m, dto.InitialMinimumPercentage);
        Assert.Equal(1001m, Assert.Single(dto.Lines).Subtotal);
        Assert.Equal(2, dto.Payments.Count);
        Assert.Null(dto.Delivery);
        Assert.Null(dto.Cancellation);
        Assert.All(dto.Payments, x => Assert.Equal(correlation, x.CorrelationId));
        Assert.Equal(99m, dto.Payments.Single(x => x.MethodCode == "EFECTIVO").Change);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var detail = json.RootElement;
        AssertProperties(detail, "layaway", "initialMinimumPercentage", "lines", "payments", "delivery", "cancellation");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("delivery").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("cancellation").ValueKind);
        AssertProperties(detail.GetProperty("layaway"), "id", "number", "customerId",
            "customerName", "createdAtUtc", "dueOn", "total", "initialPayment",
            "balance", "state", "version");
        var line = Assert.Single(detail.GetProperty("lines").EnumerateArray());
        AssertProperties(line, "productId", "productCode", "productName", "unit",
            "quantity", "unitPrice", "subtotal");
        Assert.Equal(layaway.DetalleApartado.Single().ProductoId.ToString(),
            line.GetProperty("productId").GetString());
        Assert.Equal("P-1", line.GetProperty("productCode").GetString());
        Assert.Equal("Producto", line.GetProperty("productName").GetString());
        Assert.Equal(1001m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(1001m, line.GetProperty("subtotal").GetDecimal());
        var payments = detail.GetProperty("payments").EnumerateArray().ToArray();
        Assert.Equal(2, payments.Length);
        foreach (var payment in payments)
            AssertProperties(payment, "id", "number", "methodCode", "methodName",
                "appliedAmount", "receivedAmount", "change", "occurredAtUtc",
                "state", "correlationId");
        Assert.Equal("PAP-A-1", payments[0].GetProperty("number").GetString());
        Assert.Equal("EFECTIVO", payments[0].GetProperty("methodCode").GetString());
        Assert.Equal(101m, payments[0].GetProperty("appliedAmount").GetDecimal());
        Assert.Equal(99m, payments[0].GetProperty("change").GetDecimal());
        Assert.Equal("PAP-A-2", payments[1].GetProperty("number").GetString());
        Assert.Equal("NEQUI", payments[1].GetProperty("methodCode").GetString());
        Assert.Equal(100m, payments[1].GetProperty("appliedAmount").GetDecimal());

        static void AssertProperties(JsonElement value, params string[] expected) =>
            Assert.Equal(expected.Order(StringComparer.Ordinal),
                value.EnumerateObject().Select(property => property.Name)
                    .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Detail_ProjectsDeliveryFromInventoryMovementsWithoutCosts()
    {
        var userId = Guid.CreateVersion7();
        var layawayId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var deliveredAt = new DateTime(2026, 10, 20, 14, 30, 0, DateTimeKind.Utc);
        var layaway = new Apartado
        {
            Id = layawayId, Prefijo = "APT", Serie = "A", Consecutivo = 9,
            ClienteId = Guid.CreateVersion7(), Cliente = new Cliente { NombreCompleto = "Cliente real" },
            FechaHora = deliveredAt.AddDays(-20), FechaLimite = new DateOnly(2026, 10, 15),
            Total = 2002m, AbonoInicial = 2002m, SaldoPendiente = 0m, Estado = "ENTREGADO",
            Version = 3, PorcentajeMinimoInicial = 20m, FechaEntrega = deliveredAt,
            UsuarioEntregaId = userId,
            DetalleApartado =
            [
                new DetalleApartado { ProductoId = productId, CodigoProducto = "P-9",
                    NombreProducto = "Producto reservado", UnidadMedida = "Unidad", Cantidad = 2,
                    PrecioReserva = 1001m, SubtotalNeto = 2002m, CostoUnitario = 777m }
            ],
            MovimientoInventario =
            [
                new MovimientoInventario { ProductoId = productId, ApartadoId = layawayId,
                    TipoMovimiento = "RESERVA_APARTADO", CantidadStock = 0, CantidadReservada = 2,
                    CorrelacionId = Guid.CreateVersion7(), CostoPromedioAnterior = 777m },
                new MovimientoInventario { ProductoId = productId, ApartadoId = layawayId,
                    TipoMovimiento = "ENTREGA_APARTADO", CantidadStock = -2, CantidadReservada = -2,
                    CorrelacionId = correlationId, CostoPromedioAnterior = 777m,
                    CostoPromedioPosterior = 777m }
            ]
        };
        var service = new LayawayQueryService(new QuerySpy { Detail = layaway },
            Users(userId, hasPermission: true));

        var result = await service.GetAsync(new ActorContext(userId, RoleLevel.Cajero), layawayId);

        Assert.True(result.IsSuccess);
        var delivery = Assert.IsType<LayawayDeliveryDto>(result.Value!.Delivery);
        Assert.Equal(layawayId, delivery.LayawayId);
        Assert.Equal("APT-A-9", delivery.Number);
        Assert.Equal("ENTREGADO", delivery.State);
        Assert.Equal(3, delivery.Version);
        Assert.Equal(new DateTimeOffset(deliveredAt), delivery.DeliveredAtUtc);
        Assert.Equal(userId, delivery.DeliveredByUserId);
        Assert.Equal(correlationId, delivery.CorrelationId);
        var product = Assert.Single(delivery.Products);
        Assert.Equal(productId, product.ProductId);
        Assert.Equal("P-9", product.ProductCode);
        Assert.Equal("Producto reservado", product.ProductName);
        Assert.Equal("Unidad", product.Unit);
        Assert.Equal(2, product.Quantity);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var deliveryJson = json.RootElement.GetProperty("delivery");
        Assert.Equal(new[] { "correlationId", "deliveredAtUtc", "deliveredByUserId", "layawayId",
                "number", "products", "state", "version" },
            deliveryJson.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "productCode", "productId", "productName", "quantity", "unit" },
            Assert.Single(deliveryJson.GetProperty("products").EnumerateArray())
                .EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("cost", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("costo", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("777", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_NormalizesStateAndKeepsCountForPagination()
    {
        var userId = Guid.CreateVersion7();
        var repository = new QuerySpy();
        var service = new LayawayQueryService(repository, Users(userId, hasPermission: true));

        var result = await service.ListAsync(new ActorContext(userId, RoleLevel.Supervisor),
            new LayawayListQuery(Page: 2, PageSize: 10, State: " pagado "));

        Assert.True(result.IsSuccess);
        Assert.Equal("PAGADO", repository.LastListQuery?.State);
        Assert.Equal(10, repository.LastOffset);
        Assert.Equal(21, result.Value?.TotalCount);
        Assert.Equal(3, result.Value?.TotalPages);
    }

    private static IUserRepository Users(Guid userId, bool hasPermission)
    {
        var users = DispatchProxy.Create<IUserRepository, AuthorizationProxy>();
        ((AuthorizationProxy)(object)users).Snapshot = new UserAuthorizationSnapshot(userId, "query.test",
            "Consulta", "stamp", RoleLevel.Administrador, [], hasPermission
                ? [new PermissionClaim(Guid.CreateVersion7(), PermissionCodes.LayawayManage, "Apartados")]
                : []);
        return users;
    }

    public class AuthorizationProxy : DispatchProxy
    {
        public UserAuthorizationSnapshot? Snapshot { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IUserRepository.GetAuthorizationSnapshotAsync)
                ? Task.FromResult(Snapshot)
                : throw new InvalidOperationException("Unexpected user repository call.");
    }

    private sealed class QuerySpy : ILayawayQueryRepository
    {
        public int Calls { get; private set; }
        public int LastOffset { get; private set; }
        public LayawayListQuery? LastListQuery { get; private set; }
        public Apartado? Detail { get; init; }

        public Task<PagedResult<Apartado>> ListAsync(LayawayListQuery query, int offset,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastListQuery = query;
            LastOffset = offset;
            return Task.FromResult(new PagedResult<Apartado>([], query.Page, query.PageSize, 21));
        }

        public Task<Apartado?> GetAsync(Guid layawayId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Detail);
        }
    }
}
