using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Application.Layaways.Services;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class LayawayDeliveryServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PaidLayaway_DeliversReservedProductsIncludingAfterDueDate_WithoutFinancialCalls(bool expired)
    {
        var fixture = new DeliveryFixture();
        fixture.Layaway.FechaLimite = DateOnly.FromDateTime(fixture.Now.UtcDateTime).AddDays(expired ? -5 : 5);

        var result = await fixture.DeliverAsync();

        Assert.True(result.IsSuccess);
        var delivery = result.Value!;
        Assert.Equal(fixture.Layaway.Id, delivery.LayawayId);
        Assert.Equal("APT-A-17", delivery.Number);
        Assert.Equal("ENTREGADO", delivery.State);
        Assert.Equal(8, delivery.Version);
        Assert.Equal(fixture.Now, delivery.DeliveredAtUtc);
        Assert.Equal(fixture.Actor.UserId, delivery.DeliveredByUserId);
        Assert.NotEqual(Guid.Empty, delivery.CorrelationId);
        var deliveredProduct = Assert.Single(delivery.Products);
        Assert.Equal(fixture.Product.Id, deliveredProduct.ProductId);
        Assert.Equal("P-ORIGINAL", deliveredProduct.ProductCode);
        Assert.Equal("Nombre reservado", deliveredProduct.ProductName);
        Assert.Equal("Unidad", deliveredProduct.Unit);
        Assert.Equal(2, deliveredProduct.Quantity);
        Assert.Equal(8, fixture.Product.StockActual);
        Assert.Equal(2, fixture.Product.StockReservado);
        Assert.Equal(777m, fixture.Product.CostoPromedio);
        Assert.Equal("ENTREGADO", fixture.Layaway.Estado);
        Assert.Equal(fixture.Now.UtcDateTime, fixture.Layaway.FechaEntrega);
        Assert.Equal(fixture.Actor.UserId, fixture.Layaway.UsuarioEntregaId);
        Assert.Null(fixture.Layaway.VentaEntregaId);
        Assert.Equal(0m, fixture.Layaway.SaldoPendiente);
        var movement = Assert.Single(fixture.Sales.Movements);
        Assert.Equal("ENTREGA_APARTADO", movement.TipoMovimiento);
        Assert.Equal(fixture.Product.Id, movement.ProductoId);
        Assert.Equal(fixture.Layaway.Id, movement.ApartadoId);
        Assert.Equal(fixture.Actor.UserId, movement.UsuarioId);
        Assert.Equal(-2, movement.CantidadStock);
        Assert.Equal(-2, movement.CantidadReservada);
        Assert.Equal(10, movement.StockAnterior);
        Assert.Equal(8, movement.StockPosterior);
        Assert.Equal(4, movement.ReservadoAnterior);
        Assert.Equal(2, movement.ReservadoPosterior);
        Assert.Equal(delivery.CorrelationId, movement.CorrelacionId);
        Assert.Null(movement.VentaId);
        Assert.Null(movement.SesionOperadorId);
        Assert.Equal(1, fixture.Saves);
        Assert.Equal(1, fixture.Transactions.CommitCalls);
        Assert.Equal(1, fixture.Transactions.DisposeCalls);
        var operation = Assert.Single(fixture.Sales.AddedIdempotency);
        Assert.Equal("ENTREGAR_APARTADO", operation.TipoOperacion);
        Assert.Equal(fixture.Actor.UserId, operation.UsuarioId);
        Assert.Equal(delivery.CorrelationId, operation.CorrelacionId);
        Assert.Equal(delivery.CorrelationId,
            JsonSerializer.Deserialize<LayawayDeliveryDto>(operation.Resultado!)!.CorrelationId);
        var audit = Assert.Single(fixture.Audits);
        Assert.Equal("LayawayDelivered", audit.Action.ToString());
        Assert.Equal(fixture.Layaway.Id, audit.EntityId);
        Assert.Equal(fixture.Actor.UserId, audit.ActorUserId);
        Assert.Equal(delivery.CorrelationId.ToString(), audit.CorrelationId);
        Assert.Null(audit.OperatorSessionId);
        Assert.True(fixture.Events.IndexOf("installation") < fixture.Events.IndexOf("layaway"));
        Assert.True(fixture.Events.IndexOf("layaway") < fixture.Events.IndexOf("products"));
        Assert.True(fixture.Events.IndexOf("save") < fixture.Events.IndexOf("commit"));
        Assert.DoesNotContain("cost", JsonSerializer.Serialize(delivery).ToLowerInvariant());
        Assert.DoesNotContain("costo", audit.Details?.ToLowerInvariant() ?? "");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedPermission_RejectsBeforeReadingIdempotencyEvenForReplay(bool replay)
    {
        var fixture = new DeliveryFixture();
        if (replay) fixture.SetReplay();
        fixture.Users.Snapshot = fixture.Snapshot(hasPermission: false);

        var result = await fixture.DeliverAsync();

        Assert.Equal("authorization.forbidden", result.Error?.Code);
        Assert.DoesNotContain("idempotency", fixture.Events);
        fixture.AssertNoEffects();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingCurrentIdentity_RejectsBeforeReadingIdempotencyEvenForReplay(bool replay)
    {
        var fixture = new DeliveryFixture();
        if (replay) fixture.SetReplay();
        fixture.Users.Snapshot = null;

        var result = await fixture.DeliverAsync();

        Assert.Equal("authentication.unauthorized", result.Error?.Code);
        Assert.DoesNotContain("idempotency", fixture.Events);
        fixture.AssertNoEffects();
    }

    [Fact]
    public async Task ExactReplay_ReturnsSavedSnapshotWithoutReadingOrMutatingLayawayAndProducts()
    {
        var fixture = new DeliveryFixture();
        var saved = fixture.SetReplay();
        fixture.Repository.Layaway = null;
        fixture.Sales.Products = [];

        var result = await fixture.DeliverAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(result.Value));
        Assert.True(fixture.Events.IndexOf("authorization") < fixture.Events.IndexOf("idempotency"));
        Assert.DoesNotContain("layaway", fixture.Events);
        Assert.DoesNotContain("products", fixture.Events);
        Assert.DoesNotContain("terminal", fixture.Events);
        fixture.AssertNoEffects();
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("operation")]
    [InlineData("hash")]
    [InlineData("empty-result")]
    [InlineData("null-result")]
    public async Task IncompatibleIdempotencyKey_ReturnsConflictWithoutEffects(string difference)
    {
        var fixture = new DeliveryFixture();
        fixture.SetReplay();
        var previous = fixture.Sales.Previous!;
        switch (difference)
        {
            case "actor": previous.UsuarioId = Guid.CreateVersion7(); break;
            case "operation": previous.TipoOperacion = "ABONAR_APARTADO"; break;
            case "hash": previous.HashSolicitud = "DIFFERENT"; break;
            case "empty-result": previous.Resultado = ""; break;
            case "null-result": previous.Resultado = "null"; break;
        }

        var result = await fixture.DeliverAsync();

        Assert.Equal("resource.conflict", result.Error?.Code);
        Assert.DoesNotContain("layaway", fixture.Events);
        fixture.AssertNoEffects();
    }

    [Theory]
    [InlineData("", 7, false)]
    [InlineData("   ", 7, false)]
    [InlineData("valid-key", 0, false)]
    [InlineData("valid-key", -1, false)]
    [InlineData("valid-key", 7, true)]
    public async Task InvalidRequest_IsRejectedBeforeStartingTransaction(string key, long version, bool emptyId)
    {
        var fixture = new DeliveryFixture();

        var result = await fixture.Service.DeliverAsync(fixture.Actor,
            emptyId ? Guid.Empty : fixture.Layaway.Id, key, new DeliverLayawayRequest(version));

        Assert.Equal("validation.failed", result.Error?.Code);
        Assert.Equal(0, fixture.Transactions.BeginCalls);
        fixture.AssertNoEffects();
    }

    [Fact]
    public async Task OversizedIdempotencyKey_IsRejectedBeforeStartingTransaction()
    {
        var fixture = new DeliveryFixture();

        var result = await fixture.DeliverAsync(new string('x', 101));

        Assert.Equal("validation.failed", result.Error?.Code);
        Assert.Equal(0, fixture.Transactions.BeginCalls);
        fixture.AssertNoEffects();
    }

    [Fact]
    public async Task NullRequest_IsRejectedBeforeStartingTransaction()
    {
        var fixture = new DeliveryFixture();

        var result = await fixture.Service.DeliverAsync(fixture.Actor, fixture.Layaway.Id, "delivery", null!);

        Assert.Equal("validation.failed", result.Error?.Code);
        Assert.Equal(0, fixture.Transactions.BeginCalls);
        fixture.AssertNoEffects();
    }

    [Fact]
    public async Task ObsoleteVersion_ReturnsConcurrencyConflictWithoutEffects()
    {
        var fixture = new DeliveryFixture();

        var result = await fixture.Service.DeliverAsync(fixture.Actor, fixture.Layaway.Id,
            "delivery", new DeliverLayawayRequest(6));

        Assert.Equal("concurrency.conflict", result.Error?.Code);
        fixture.AssertNoEffects();
    }

    [Theory]
    [InlineData("ACTIVO", 800)]
    [InlineData("ENTREGADO", 0)]
    [InlineData("ANULADO", 0)]
    [InlineData("PAGADO", 1)]
    public async Task NonDeliverableStateOrBalance_ReturnsConflictWithoutEffects(string state, int balance)
    {
        var fixture = new DeliveryFixture();
        fixture.Layaway.Estado = state;
        fixture.Layaway.SaldoPendiente = balance;

        var result = await fixture.DeliverAsync();

        Assert.Equal("resource.conflict", result.Error?.Code);
        fixture.AssertNoEffects(expectedState: state, expectedBalance: balance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingLayawayOrDifferentInstallation_ReturnsNotFoundWithoutEffects(bool otherInstallation)
    {
        var fixture = new DeliveryFixture();
        if (otherInstallation) fixture.Layaway.InstalacionId = Guid.CreateVersion7();
        else fixture.Repository.Layaway = null;

        var result = await fixture.DeliverAsync();

        Assert.Equal("resource.not_found", result.Error?.Code);
        fixture.AssertNoEffects();
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(10, 1)]
    public async Task InsufficientStockOrReservation_ReturnsConflictWithoutEffects(int stock, int reserved)
    {
        var fixture = new DeliveryFixture();
        fixture.Product.StockActual = stock;
        fixture.Product.StockReservado = reserved;

        var result = await fixture.DeliverAsync();

        Assert.Equal("resource.conflict", result.Error?.Code);
        fixture.AssertNoEffects(stock, reserved);
    }

    [Fact]
    public async Task MissingProduct_RejectsWithoutChangingOtherProducts()
    {
        var fixture = new DeliveryFixture();
        fixture.Repository.Lines.Add(fixture.Line(Guid.CreateVersion7(), 1));

        var result = await fixture.DeliverAsync();

        Assert.False(result.IsSuccess);
        fixture.AssertNoEffects();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidPersistedQuantity_RejectsWithoutEffects(int quantity)
    {
        var fixture = new DeliveryFixture();
        fixture.Repository.Lines[0].Cantidad = quantity;

        var result = await fixture.DeliverAsync();

        Assert.Equal("resource.conflict", result.Error?.Code);
        fixture.AssertNoEffects();
    }

    [Fact]
    public async Task EmptyPersistedLines_RejectsWithoutEffects()
    {
        var fixture = new DeliveryFixture();
        fixture.Repository.Lines.Clear();

        var result = await fixture.DeliverAsync();

        Assert.Equal("resource.conflict", result.Error?.Code);
        fixture.AssertNoEffects();
    }

    [Fact]
    public async Task RepeatedProductLines_AggregateExactQuantityIntoOneInventoryMovement()
    {
        var fixture = new DeliveryFixture();
        fixture.Repository.Lines.Add(fixture.Line(fixture.Product.Id, 1));

        var result = await fixture.DeliverAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, Assert.Single(result.Value!.Products).Quantity);
        Assert.Equal(-3, Assert.Single(fixture.Sales.Movements).CantidadStock);
        Assert.Equal(-3, Assert.Single(fixture.Sales.Movements).CantidadReservada);
        Assert.Equal(7, fixture.Product.StockActual);
        Assert.Equal(1, fixture.Product.StockReservado);
    }

    [Fact]
    public async Task RepeatedProductQuantityOverflow_RejectsWithoutEffects()
    {
        var fixture = new DeliveryFixture();
        fixture.Repository.Lines[0].Cantidad = int.MaxValue;
        fixture.Repository.Lines.Add(fixture.Line(fixture.Product.Id, 1));

        var result = await fixture.DeliverAsync();

        Assert.Equal("resource.conflict", result.Error?.Code);
        fixture.AssertNoEffects();
    }

    [Fact]
    public async Task MultipleProducts_DeliverExactQuantitiesAndPreserveOtherReservationsAndCosts()
    {
        var fixture = new DeliveryFixture();
        var other = new Producto { Id = Guid.CreateVersion7(), StockActual = 7,
            StockReservado = 5, CostoPromedio = null };
        fixture.Repository.Lines.Add(fixture.Line(other.Id, 3));
        fixture.Sales.Products = [other, fixture.Product];

        var result = await fixture.DeliverAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Products.Count);
        Assert.Equal(2, result.Value.Products.Single(x => x.ProductId == fixture.Product.Id).Quantity);
        Assert.Equal(3, result.Value.Products.Single(x => x.ProductId == other.Id).Quantity);
        Assert.Equal(8, fixture.Product.StockActual);
        Assert.Equal(2, fixture.Product.StockReservado);
        Assert.Equal(4, other.StockActual);
        Assert.Equal(2, other.StockReservado);
        Assert.Null(other.CostoPromedio);
        Assert.Equal(2, fixture.Sales.Movements.Count);
        var otherMovement = Assert.Single(fixture.Sales.Movements, x => x.ProductoId == other.Id);
        Assert.Equal(-3, otherMovement.CantidadStock);
        Assert.Equal(-3, otherMovement.CantidadReservada);
        Assert.Null(otherMovement.CostoPromedioAnterior);
        Assert.Null(otherMovement.CostoPromedioPosterior);
        Assert.All(fixture.Sales.Movements, x => Assert.Equal(result.Value.CorrelationId, x.CorrelacionId));
        Assert.Equal(1, fixture.Saves);
        Assert.Equal(1, fixture.Transactions.CommitCalls);
    }

    [Fact]
    public async Task InsufficientReservationInLaterProduct_DoesNotMutateEarlierProduct()
    {
        var fixture = new DeliveryFixture();
        var laterId = new Guid("ffffffff-ffff-7fff-bfff-ffffffffffff");
        var later = new Producto { Id = laterId, StockActual = 5, StockReservado = 0 };
        fixture.Repository.Lines.Add(fixture.Line(laterId, 1));
        fixture.Sales.Products = [fixture.Product, later];

        var result = await fixture.DeliverAsync();

        Assert.Equal("resource.conflict", result.Error?.Code);
        Assert.Equal(5, later.StockActual);
        Assert.Equal(0, later.StockReservado);
        fixture.AssertNoEffects();
    }

    private sealed class DeliveryFixture
    {
        public DateTimeOffset Now { get; } = new(2026, 10, 20, 16, 0, 0, TimeSpan.Zero);
        public List<string> Events { get; } = [];
        public ActorContext Actor { get; } = new(Guid.CreateVersion7(), RoleLevel.Cajero,
            [PermissionCodes.LayawayManage]);
        public Apartado Layaway { get; }
        public Producto Product { get; }
        public DeliveryRepository Repository { get; }
        public DeliverySalesProxy Sales { get; }
        public DeliveryUsersProxy Users { get; }
        public TransactionSpy Transactions { get; }
        public List<AuditRecord> Audits { get; } = [];
        public int Saves { get; private set; }
        public LayawayDeliveryService Service { get; }

        public DeliveryFixture()
        {
            var sales = DispatchProxy.Create<ISalesRepository, DeliverySalesProxy>();
            Sales = (DeliverySalesProxy)(object)sales;
            Sales.Events = Events;
            Layaway = new Apartado
            {
                Id = Guid.CreateVersion7(), InstalacionId = Sales.Installation.Id,
                Prefijo = "APT", Serie = "A", Consecutivo = 17, Total = 2000m,
                SaldoPendiente = 0m, Estado = "PAGADO", Version = 7,
                FechaLimite = new DateOnly(2026, 11, 1)
            };
            Product = new Producto
            {
                Id = Guid.CreateVersion7(), CodigoInterno = "P-ACTUAL", Nombre = "Nombre actual",
                StockActual = 10, StockReservado = 4, CostoPromedio = 777m,
                Version = 4, Activo = true
            };
            Sales.Products = [Product];
            Repository = new DeliveryRepository(Events) { Layaway = Layaway };
            Repository.Lines.Add(Line(Product.Id, 2));
            var users = DispatchProxy.Create<IUserRepository, DeliveryUsersProxy>();
            Users = (DeliveryUsersProxy)(object)users;
            Users.Events = Events;
            Users.Snapshot = Snapshot(hasPermission: true);
            var audits = DispatchProxy.Create<IAuditRepository, DeliveryAuditProxy>();
            ((DeliveryAuditProxy)(object)audits).Records = Audits;
            var unitOfWork = DispatchProxy.Create<IUnitOfWork, DeliveryUnitOfWorkProxy>();
            ((DeliveryUnitOfWorkProxy)(object)unitOfWork).Save = () =>
            {
                Events.Add("save");
                Saves++;
                Layaway.Version++;
                return Task.FromResult(1);
            };
            Transactions = new TransactionSpy(Events);
            Service = new LayawayDeliveryService(Repository, sales, Transactions, users,
                audits, unitOfWork, new FixedClock(Now));
        }

        public UserAuthorizationSnapshot Snapshot(bool hasPermission) => new(Actor.UserId,
            "delivery.test", "Entrega", "stamp", RoleLevel.Cajero, [], hasPermission
                ? [new PermissionClaim(Guid.CreateVersion7(), PermissionCodes.LayawayManage, "Apartados")]
                : []);

        public DetalleApartado Line(Guid productId, int quantity) => new()
        {
            Id = Guid.CreateVersion7(), ApartadoId = Layaway.Id, ProductoId = productId,
            CodigoProducto = "P-ORIGINAL", NombreProducto = "Nombre reservado", UnidadMedida = "Unidad",
            Cantidad = quantity, CostoUnitario = 666m, PrecioReserva = 1000m, SubtotalNeto = 1000m * quantity
        };

        public Task<ControlPlus.Application.Common.Result<LayawayDeliveryDto>> DeliverAsync(string key = "delivery") =>
            Service.DeliverAsync(Actor, Layaway.Id, key, new DeliverLayawayRequest(7));

        public LayawayDeliveryDto SetReplay()
        {
            var saved = new LayawayDeliveryDto(Layaway.Id, "APT-A-17", "ENTREGADO", 8,
                Now.AddDays(-1), Actor.UserId, Guid.CreateVersion7(),
                [new LayawayDeliveredProductDto(Product.Id, "P-ORIGINAL", "Nombre reservado", "Unidad", 2)]);
            var canonical = JsonSerializer.Serialize(new { layawayId = Layaway.Id, ExpectedVersion = 7L });
            Sales.Previous = new OperacionIdempotente
            {
                UsuarioId = Actor.UserId, TipoOperacion = "ENTREGAR_APARTADO",
                HashSolicitud = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))),
                Resultado = JsonSerializer.Serialize(saved)
            };
            return saved;
        }

        public void AssertNoEffects(int stock = 10, int reserved = 4,
            string expectedState = "PAGADO", decimal expectedBalance = 0m)
        {
            Assert.Equal(stock, Product.StockActual);
            Assert.Equal(reserved, Product.StockReservado);
            Assert.Equal(expectedState, Layaway.Estado);
            Assert.Equal(expectedBalance, Layaway.SaldoPendiente);
            Assert.Equal(7, Layaway.Version);
            Assert.Null(Layaway.FechaEntrega);
            Assert.Null(Layaway.UsuarioEntregaId);
            Assert.Null(Layaway.VentaEntregaId);
            Assert.Empty(Sales.Movements);
            Assert.Empty(Sales.AddedIdempotency);
            Assert.Empty(Audits);
            Assert.Equal(0, Saves);
            Assert.Equal(0, Transactions.CommitCalls);
        }
    }

    private sealed class DeliveryRepository(List<string> events) : ILayawayDeliveryRepository
    {
        public Apartado? Layaway { get; set; }
        public List<DetalleApartado> Lines { get; } = [];

        public Task<Apartado?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
        {
            events.Add("layaway");
            return Task.FromResult(Layaway);
        }

        public Task<IReadOnlyCollection<DetalleApartado>> GetLinesAsync(Guid id,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<DetalleApartado>>(Lines);
    }

    public class DeliverySalesProxy : DispatchProxy
    {
        public Instalacion Installation { get; } = new() { Id = Guid.CreateVersion7(), Serie = "A" };
        public Terminal Terminal { get; } = new() { Id = Guid.CreateVersion7() };
        public List<string> Events { get; set; } = [];
        public OperacionIdempotente? Previous { get; set; }
        public IReadOnlyCollection<Producto> Products { get; set; } = [];
        public List<MovimientoInventario> Movements { get; } = [];
        public List<OperacionIdempotente> AddedIdempotency { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(ISalesRepository.GetActiveInstallationForUpdateAsync):
                    Events.Add("installation");
                    return Task.FromResult<Instalacion?>(Installation);
                case nameof(ISalesRepository.GetActiveTerminalAsync):
                    Events.Add("terminal");
                    return Task.FromResult<Terminal?>(Terminal);
                case nameof(ISalesRepository.GetIdempotencyAsync):
                    Events.Add("idempotency");
                    return Task.FromResult(Previous);
                case nameof(ISalesRepository.GetProductsForUpdateAsync):
                    Events.Add("products");
                    return Task.FromResult(Products);
                case nameof(ISalesRepository.AddInventoryMovementsAsync):
                    Movements.AddRange((IReadOnlyCollection<MovimientoInventario>)args![0]!);
                    return Task.CompletedTask;
                case nameof(ISalesRepository.AddIdempotencyAsync):
                    AddedIdempotency.Add((OperacionIdempotente)args![0]!);
                    return Task.CompletedTask;
                default:
                    throw new InvalidOperationException($"Unexpected sales call: {targetMethod?.Name}. Delivery must not use shifts, sessions, payments, cash or sales.");
            }
        }
    }

    public class DeliveryUsersProxy : DispatchProxy
    {
        public List<string> Events { get; set; } = [];
        public UserAuthorizationSnapshot? Snapshot { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IUserRepository.GetAuthorizationSnapshotAsync))
                throw new InvalidOperationException("Unexpected user repository call.");
            Events.Add("authorization");
            return Task.FromResult(Snapshot);
        }
    }

    public class DeliveryAuditProxy : DispatchProxy
    {
        public List<AuditRecord> Records { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IAuditRepository.AddAsync))
                throw new InvalidOperationException("Unexpected audit repository call.");
            Records.Add((AuditRecord)args![0]!);
            return Task.CompletedTask;
        }
    }

    public class DeliveryUnitOfWorkProxy : DispatchProxy
    {
        public Func<Task<int>> Save { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IUnitOfWork.SaveChangesAsync)
                ? Save()
                : throw new InvalidOperationException("Unexpected unit of work call.");
    }

    private sealed class TransactionSpy(List<string> events) : ISalesTransactionManager, ISalesTransaction
    {
        public int BeginCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public Task<ISalesTransaction> BeginAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(IsolationLevel.ReadCommitted, isolationLevel);
            BeginCalls++;
            return Task.FromResult<ISalesTransaction>(this);
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            events.Add("commit");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
