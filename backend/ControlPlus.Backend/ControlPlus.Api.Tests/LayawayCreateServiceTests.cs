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
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class LayawayCreateServiceTests
{
    [Fact]
    public async Task CashierWithoutAuthorization_IsRejected()
    {
        var fixture = new Fixture(RoleLevel.Cajero);
        var result = await fixture.Service.CreateAsync(fixture.Actor, "missing-authorization", fixture.Request(null));
        Assert.False(result.IsSuccess);
        Assert.Equal("validation.failed", result.Error?.Code);
        Assert.Equal("La autorización de otro Supervisor o Administrador es obligatoria.", result.Error?.Message);
    }

    [Fact]
    public async Task SupervisorWithoutAuthorization_ReplaysOwnCreation()
    {
        var fixture = new Fixture(RoleLevel.Supervisor);
        var request = fixture.Request(null);
        var adminHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            request.CustomerId, request.DueDate, Lines = request.Lines,
            Payments = request.Payments, AuthorizationMode = "ADMINISTRATOR_SELF"
        }))));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(adminHash + "|SUPERVISOR_SELF")));
        var saved = new CreatedLayawayDto(Guid.CreateVersion7(), "APT-T-2", request.CustomerId,
            Guid.CreateVersion7(), fixture.Actor.UserId, null, new DateOnly(2026, 11, 19),
            20m, 20m, 20m, 100m, 80m, 0m, "ACTIVO", 1, [], []);
        fixture.Previous = new OperacionIdempotente
        {
            UsuarioId = fixture.Actor.UserId, TipoOperacion = "CREAR_APARTADO",
            HashSolicitud = hash, Resultado = JsonSerializer.Serialize(saved)
        };
        var replay = await fixture.Service.CreateAsync(fixture.Actor, "supervisor-replay", request);
        Assert.True(replay.IsSuccess);
        Assert.Equal(saved.Id, replay.Value?.Id);
        Assert.Equal(1, fixture.SnapshotReads);
    }

    [Theory]
    [InlineData(RoleLevel.Cajero)]
    [InlineData(RoleLevel.Supervisor)]
    public async Task NonAdministratorCannotAuthorizeOwnCreation(RoleLevel role)
    {
        var fixture = new Fixture(role);
        var result = await fixture.Service.CreateAsync(fixture.Actor, "own-authorization",
            fixture.Request(new("actor", "example-password")));
        Assert.False(result.IsSuccess);
        Assert.Equal("authorization.forbidden", result.Error?.Code);
    }

    [Fact]
    public async Task AdministratorWithoutAuthorization_ReplaysPersistedCreationAfterCurrentRoleAndPermissionCheck()
    {
        var fixture = new Fixture(RoleLevel.Administrador);
        var request = fixture.Request(null);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            request.CustomerId, request.DueDate, Lines = request.Lines,
            Payments = request.Payments, AuthorizationMode = "ADMINISTRATOR_SELF"
        }))));
        var saved = new CreatedLayawayDto(Guid.CreateVersion7(), "APT-T-1", request.CustomerId,
            Guid.CreateVersion7(), fixture.Actor.UserId, null, new DateOnly(2026, 11, 19),
            20m, 20m, 20m, 100m, 80m, 0m, "ACTIVO", 1, [], []);
        fixture.Previous = new OperacionIdempotente
        {
            UsuarioId = fixture.Actor.UserId, TipoOperacion = "CREAR_APARTADO",
            HashSolicitud = hash, Resultado = JsonSerializer.Serialize(saved)
        };

        var first = await fixture.Service.CreateAsync(fixture.Actor, "admin-replay", request);
        var replay = await fixture.Service.CreateAsync(fixture.Actor, "admin-replay", request);
        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        Assert.Equal(saved.Id, first.Value?.Id);
        Assert.Equal(saved.Id, replay.Value?.Id);
        Assert.Equal(2, fixture.SnapshotReads);
    }

    private sealed class Fixture
    {
        private readonly Guid _installationId = Guid.CreateVersion7();
        private readonly Guid _terminalId = Guid.CreateVersion7();
        private readonly Guid _customerId = Guid.CreateVersion7();
        private readonly Guid _productId = Guid.CreateVersion7();
        private readonly RoleLevel _role;

        public Fixture(RoleLevel role)
        {
            _role = role;
            Actor = new ActorContext(Guid.CreateVersion7(), role);
            var sales = Stub<ISalesRepository>.Create((method, _) => method.Name switch
            {
                nameof(ISalesRepository.GetActiveInstallationForUpdateAsync) =>
                    Task.FromResult<Instalacion?>(new Instalacion
                    { Id = _installationId, EstablecimientoId = Guid.CreateVersion7(), Serie = "T" }),
                nameof(ISalesRepository.GetIdempotencyAsync) => Task.FromResult(Previous),
                nameof(ISalesRepository.GetActiveTerminalAsync) =>
                    Task.FromResult<Terminal?>(new Terminal { Id = _terminalId }),
                nameof(ISalesRepository.GetOpenShiftForUpdateAsync) =>
                    Task.FromResult<TurnoCaja?>(new TurnoCaja
                    { TerminalId = _terminalId, ModoOperacion = "INDIVIDUAL", UsuarioResponsableId = Actor.UserId,
                      FechaOperativa = new DateOnly(2026, 11, 4) }),
                nameof(ISalesRepository.GetUserForUpdateAsync) => Task.FromResult<Usuario?>(new Usuario
                { Id = Actor.UserId, Activo = true }),
                _ => throw new NotSupportedException(method.Name)
            });
            var users = Stub<IUserRepository>.Create((method, _) => method.Name switch
            {
                nameof(IUserRepository.GetAuthorizationSnapshotAsync) => CurrentSnapshot(),
                nameof(IUserRepository.GetByUserNameAsync) => Task.FromResult<Usuario?>(new Usuario
                { Id = Actor.UserId, Activo = true }),
                _ => throw new NotSupportedException(method.Name)
            });
            var layaways = Stub<ILayawayCreateRepository>.Create((method, _) => method.Name switch
            {
                nameof(ILayawayCreateRepository.GetEstablishmentAsync) =>
                    Task.FromResult<Establecimiento?>(new Establecimiento { DiasLimiteApartado = 15 }),
                nameof(ILayawayCreateRepository.GetActiveCustomerForUpdateAsync) =>
                    Task.FromResult<Cliente?>(new Cliente { Id = _customerId, Telefono = "3000000000", Direccion = "Calle de prueba" }),
                _ => throw new NotSupportedException(method.Name)
            });
            Service = new LayawayCreateService(layaways, sales, users,
                Stub<IPermissionChecker>.Create((method, _) => throw new NotSupportedException(method.Name)),
                Stub<IPermissionRepository>.Create((method, _) => throw new NotSupportedException(method.Name)),
                Stub<IPasswordHasher>.Create((method, _) => throw new NotSupportedException(method.Name)),
                Stub<IAuthorizerCredentialResolver>.Create((method, _) => throw new NotSupportedException(method.Name)),
                new ImmediateCoordinator(),
                Stub<IAuditRepository>.Create((method, _) => throw new NotSupportedException(method.Name)),
                Stub<IUnitOfWork>.Create((method, _) => throw new NotSupportedException(method.Name)),
                Stub<IClock>.Create((method, _) => throw new NotSupportedException(method.Name)));
        }

        public ActorContext Actor { get; }
        public LayawayCreateService Service { get; }
        public OperacionIdempotente? Previous { get; set; }
        public int SnapshotReads { get; private set; }

        public CreateLayawayRequest Request(CreateLayawayAuthorizationRequest? authorization) =>
            new(_customerId, [new(_productId, 1, "MINORISTA")],
                [new("EFECTIVO", 20m, 20m, null)], null, authorization);

        private Task<UserAuthorizationSnapshot?> CurrentSnapshot()
        {
            SnapshotReads++;
            return Task.FromResult<UserAuthorizationSnapshot?>(new UserAuthorizationSnapshot(
                Actor.UserId, "actor", "Actor", "stamp", _role,
                [], [new PermissionClaim(Guid.CreateVersion7(), PermissionCodes.LayawayManage, "Apartados")]));
        }
    }

    private sealed class ImmediateCoordinator : ILoginAttemptCoordinator
    {
        public Task<T> ExecuteAsync<T>(string normalizedUserName,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    public class Stub<T> : DispatchProxy where T : class
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        public static T Create(Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = DispatchProxy.Create<T, Stub<T>>();
            ((Stub<T>)(object)proxy).Handler = handler;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }
}
