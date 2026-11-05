using ControlPlus.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Catalog.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Suppliers.Contracts;
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

public sealed class SuppliersApiFlowTests : IAsyncLifetime
{
    private const string AdminName = "suppliers.admin.test";
    private const string SupervisorName = "suppliers.supervisor.test";
    private const string CashierName = "suppliers.cashier.test";
    private readonly string _masterKey = Secret(48);
    private readonly string _jwtKey = Secret(64);
    private readonly string _adminPassword = $"Suppliers-Admin-{Secret(32)}!";
    private readonly string _supervisorPassword = $"Suppliers-Supervisor-{Secret(32)}!";
    private readonly string _cashierPassword = $"Suppliers-Cashier-{Secret(32)}!";
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
            builder.UseSetting("Jwt:Issuer", "controlplus-suppliers-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-suppliers-client");
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
    public async Task Create_ValidatesStrictJsonNormalizationUniquenessAndSanitizedAudit()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var minimal = await CreateAsync(client, new(null, null, "  Sin documento  ", null, null, null, null));
        Assert.Equal('7', minimal.Id.ToString("D")[14]);
        Assert.True(minimal.IsActive); Assert.Equal(1, minimal.Version); Assert.Equal("Sin documento", minimal.Name);
        await CreateAsync(client, new(null, null, "Otra sin documento", null, null, null, null));
        var full = await CreateAsync(client, new(" nit ", " ab-1 ", " Proveedor completo ", " 300-PRIVATE-7788 ",
            " Dirección ", " TEST@EXAMPLE.COM ", " privado "));
        Assert.Equal("NIT", full.DocumentType); Assert.Equal("AB-1", full.DocumentNumber);
        Assert.Equal("test@example.com", full.Email); Assert.Equal("300-PRIVATE-7788", full.Phone);
        var before = await StateAsync();
        await ProblemAsync(await client.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest("NIT", "AB-1", "Duplicado", null, null, null, null)), HttpStatusCode.Conflict, "resource.conflict");
        foreach (var invalid in new[]
        {
            new CreateSupplierRequest(null, null, null, null, null, null, null),
            new CreateSupplierRequest(null, null, "   ", null, null, null, null),
            new CreateSupplierRequest(null, null, new string('n', 201), null, null, null, null),
            new CreateSupplierRequest("NIT", null, "Incompleto", null, null, null, null),
            new CreateSupplierRequest(null, "1", "Incompleto", null, null, null, null),
            new CreateSupplierRequest(new string('t', 21), "1", "Largo", null, null, null, null),
            new CreateSupplierRequest("NIT", new string('1', 51), "Largo", null, null, null, null),
            new CreateSupplierRequest(null, null, "Correo", null, null, "invalido", null),
            new CreateSupplierRequest(null, null, "Largo", new string('1', 31), null, null, null),
            new CreateSupplierRequest(null, null, "Largo", null, new string('d', 301), null, null),
            new CreateSupplierRequest(null, null, "Largo", null, null, new string('e', 255), null)
        })
            await ProblemAsync(await client.PostAsJsonAsync("/api/suppliers", invalid), HttpStatusCode.BadRequest, "validation.failed");
        foreach (var extra in new[] { "unknown", "id", "isActive", "version", "createdAt", "createdByUserId" })
            await ProblemAsync(await client.PostAsJsonAsync("/api/suppliers",
                new Dictionary<string, object?> { ["name"] = "Estricto", [extra] = "cliente" }), HttpStatusCode.BadRequest, "validation.failed");
        Assert.Equal(before, await StateAsync());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persisted = await db.Proveedor.AsNoTracking().SingleAsync(x => x.Id == full.Id);
        Assert.Equal(users.Admin.User.Id, persisted.UsuarioCreacionId);
        Assert.NotEqual(default, persisted.FechaCreacion);
        var audit = await db.EventoAuditoria.AsNoTracking().SingleAsync(x => x.EntidadId == full.Id);
        Assert.Equal("SUPPLIERCREATED", audit.Accion);
        AssertSanitized(audit);
        foreach (var sensitive in new[] { "AB-1", "test@example.com", "300-PRIVATE-7788", "Dirección", "privado" })
            Assert.DoesNotContain(sensitive, audit.DatosNuevos!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DuplicateRace_CommitsOneSupplierAndOneAudit()
    {
        using var first = Client(); using var second = Client();
        var users = await BootstrapAsync(first);
        Bearer(first, users.Admin.Token.AccessToken); Bearer(second, users.Admin.Token.AccessToken);
        var before = await StateAsync();
        var request = new CreateSupplierRequest("NIT", "RACE-1", "Concurrente", null, null, null, null);
        var replies = await Task.WhenAll(first.PostAsJsonAsync("/api/suppliers", request),
            second.PostAsJsonAsync("/api/suppliers", request));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict);
        await ProblemAsync(conflict, HttpStatusCode.Conflict, "resource.conflict");
        Assert.DoesNotContain("RACE-1", await conflict.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var after = await StateAsync();
        Assert.Equal(before.Suppliers + 1, after.Suppliers);
        Assert.Equal(before.Audits + 1, after.Audits);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persisted = await db.Proveedor.AsNoTracking().SingleAsync(x => x.TipoDocumento == "NIT" && x.NumeroDocumento == "RACE-1");
        Assert.Equal(1, persisted.Version);
        AssertSanitized(Assert.Single(await db.EventoAuditoria.AsNoTracking()
            .Where(x => x.EntidadId == persisted.Id).ToArrayAsync()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RolesOverridesUpdatesStatusFiltersAndNoDelete()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await CreateAsync(client, new("NIT", "FILTER-1", "Alfa", null, null, null, null));
        await CreateAsync(client, new(null, null, "Beta", null, null, null, null));
        var readId = await PermissionIdAsync(client, PermissionCodes.SuppliersRead);
        var manageId = await PermissionIdAsync(client, PermissionCodes.SuppliersManage);
        Bearer(client, users.Supervisor.Token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/suppliers")).StatusCode);
        await ProblemAsync(await client.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest(null, null, "No", null, null, null, null)), HttpStatusCode.Forbidden, "authorization.forbidden");
        Bearer(client, users.Admin.Token.AccessToken);
        await SetPermissionAsync(client, users.Supervisor.User.Id, manageId, true);
        var supervisor = await LoginAsync(client, SupervisorName, _supervisorPassword);
        Bearer(client, supervisor.AccessToken);
        await ProblemAsync(await client.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest(null, null, "Tampoco", null, null, null, null)), HttpStatusCode.Forbidden, "authorization.forbidden");
        await ProblemAsync(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}",
            new UpdateSupplierRequest("NIT", "FILTER-1", "Tampoco", null, null, null, null, supplier.Version)), HttpStatusCode.Forbidden, "authorization.forbidden");
        Bearer(client, users.Cashier.Token.AccessToken);
        await ProblemAsync(await client.GetAsync("/api/suppliers"), HttpStatusCode.Forbidden, "authorization.forbidden");
        Bearer(client, users.Admin.Token.AccessToken);
        await SetPermissionAsync(client, users.Cashier.User.Id, readId, true);
        await SetPermissionAsync(client, users.Cashier.User.Id, manageId, true);
        var cashier = await LoginAsync(client, CashierName, _cashierPassword);
        Bearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/suppliers")).StatusCode);
        await ProblemAsync(await client.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest(null, null, "No", null, null, null, null)), HttpStatusCode.Forbidden, "authorization.forbidden");
        Bearer(client, users.Admin.Token.AccessToken);
        await SetPermissionAsync(client, users.Cashier.User.Id, readId, false);
        cashier = await LoginAsync(client, CashierName, _cashierPassword);
        Bearer(client, cashier.AccessToken);
        await ProblemAsync(await client.GetAsync("/api/suppliers"), HttpStatusCode.Forbidden, "authorization.forbidden");
        Bearer(client, users.Admin.Token.AccessToken);
        var beforeExtreme = await StateAsync();
        var found = await ReadAsync<PagedResult<SupplierDto>>(await client.GetAsync("/api/suppliers?name=ALFA&documentType=nit&documentNumber=filter-1"), HttpStatusCode.OK);
        Assert.Single(found.Items);
        Assert.Equal(supplier.Id, Assert.Single(found.Items).Id);
        var ordered = await ReadAsync<PagedResult<SupplierDto>>(await client.GetAsync("/api/suppliers?page=1&pageSize=1"), HttpStatusCode.OK);
        Assert.Equal(supplier.Id, Assert.Single(ordered.Items).Id);
        await ProblemAsync(await client.GetAsync("/api/suppliers?page=2147483647&pageSize=100"), HttpStatusCode.BadRequest, "validation.failed");
        Assert.Equal(beforeExtreme, await StateAsync());
        await ProblemAsync(await client.GetAsync("/api/suppliers?documentType=NIT"), HttpStatusCode.BadRequest, "validation.failed");
        await ProblemAsync(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}",
            new Dictionary<string, object?> { ["name"] = "Prohibido", ["expectedVersion"] = supplier.Version,
                ["id"] = supplier.Id }), HttpStatusCode.BadRequest, "validation.failed");
        await ProblemAsync(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/status",
            new Dictionary<string, object?> { ["isActive"] = false, ["expectedVersion"] = supplier.Version,
                ["usuarioModificacionId"] = users.Admin.User.Id }), HttpStatusCode.BadRequest, "validation.failed");
        var updated = await ReadAsync<SupplierDto>(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}",
            new UpdateSupplierRequest("NIT", "FILTER-1", "Alfa nueva", null, null, null, null, supplier.Version)), HttpStatusCode.OK);
        Assert.Equal(2, updated.Version);
        await ProblemAsync(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}",
            new UpdateSupplierRequest("NIT", "FILTER-1", "Obsoleto", null, null, null, null, supplier.Version)), HttpStatusCode.Conflict, "concurrency.conflict");
        var inactive = await ReadAsync<SupplierDto>(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/status",
            new SetSupplierStatusRequest(false, updated.Version)), HttpStatusCode.OK);
        Assert.False(inactive.IsActive);
        Assert.Empty((await ReadAsync<PagedResult<SupplierDto>>(await client.GetAsync("/api/suppliers?name=Alfa%20nueva"), HttpStatusCode.OK)).Items);
        Assert.Single((await ReadAsync<PagedResult<SupplierDto>>(await client.GetAsync("/api/suppliers?isActive=false&name=Alfa%20nueva"), HttpStatusCode.OK)).Items);
        var active = await ReadAsync<SupplierDto>(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/status",
            new SetSupplierStatusRequest(true, inactive.Version)), HttpStatusCode.OK);
        Assert.True(active.IsActive);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"/api/suppliers/{supplier.Id}")).StatusCode);
        await SetPermissionAsync(client, users.Admin.User.Id, manageId, false);
        var admin = await LoginAsync(client, AdminName, _adminPassword);
        Bearer(client, admin.AccessToken);
        await ProblemAsync(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/status",
            new SetSupplierStatusRequest(false, active.Version)), HttpStatusCode.Forbidden, "authorization.forbidden");
        await ProblemAsync(await client.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest(null, null, "Revocado", null, null, null, null)), HttpStatusCode.Forbidden, "authorization.forbidden");
        await ProblemAsync(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}",
            new UpdateSupplierRequest("NIT", "FILTER-1", "Revocado", null, null, null, null, active.Version)), HttpStatusCode.Forbidden, "authorization.forbidden");
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var actions = await db.EventoAuditoria.AsNoTracking().Where(x => x.EntidadId == supplier.Id)
            .OrderBy(x => x.FechaHora).ToArrayAsync();
        Assert.Equal(new[] { "SUPPLIERCREATED", "SUPPLIERUPDATED", "SUPPLIERDEACTIVATED", "SUPPLIERREACTIVATED" }
                .Order(StringComparer.Ordinal), actions.Select(x => x.Accion).Order(StringComparer.Ordinal));
        foreach (var audit in actions) AssertSanitized(audit);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Inactivation_PreservesAssociatedProductAndRejectsNewAssignment()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await CreateAsync(client, new(null, null, "Asociado", null, null, null, null));
        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Categoría de proveedor", null, "PRV")), HttpStatusCode.Created);
        var units = await ReadAsync<MeasurementUnitDto[]>(await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        var unit = Assert.Single(units, x => x.Code == "UNIDAD");
        var associated = await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, unit.Id, supplier.Id, "SUP-ASSOCIATED", null, null, true,
                "Producto asociado", null, 1000, null, null, 1)), HttpStatusCode.Created);
        Assert.Equal(supplier.Id, associated.SupplierId);
        var productBefore = await ProductSnapshotAsync(associated.Id);
        var before = await StateAsync();
        var inactive = await ReadAsync<SupplierDto>(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}/status",
            new SetSupplierStatusRequest(false, supplier.Version)), HttpStatusCode.OK);
        Assert.False(inactive.IsActive);
        Assert.Equal(supplier.Version + 1, inactive.Version);
        Assert.Equal(productBefore, await ProductSnapshotAsync(associated.Id));
        Assert.Equal(supplier.Id, (await ProductSupplierIdAsync(associated.Id)));
        Assert.False((await ReadAsync<SupplierDto>(await client.GetAsync($"/api/suppliers/{supplier.Id}"), HttpStatusCode.OK)).IsActive);
        var afterInactivation = await StateAsync();
        Assert.Equal(before.Suppliers, afterInactivation.Suppliers);
        Assert.Equal(before.Audits + 1, afterInactivation.Audits);
        var rejected = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, unit.Id, supplier.Id, "SUP-REJECTED", null, null, true,
                "Otro producto", null, 1000, null, null, 1));
        await ProblemAsync(rejected, HttpStatusCode.BadRequest, "validation.failed");
        Assert.Equal(afterInactivation, await StateAsync());
        Assert.Equal(productBefore, await ProductSnapshotAsync(associated.Id));
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.Equal(1, await db.Producto.CountAsync(x => x.ProveedorId == supplier.Id));
        Assert.False(await db.Producto.AnyAsync(x => x.CodigoInterno == "SUP-REJECTED"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentEdits_CommitExactlyOneVersionAndOneAudit()
    {
        using var first = Client(); using var second = Client();
        var users = await BootstrapAsync(first);
        Bearer(first, users.Admin.Token.AccessToken); Bearer(second, users.Admin.Token.AccessToken);
        var supplier = await CreateAsync(first, new("NIT", "EDIT-RACE", "Original", null, null, null, null));
        var firstRequest = new UpdateSupplierRequest("NIT", "EDIT-RACE", "Ganador A", null, null, null, null, supplier.Version);
        var secondRequest = firstRequest with { Name = "Ganador B" };
        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var lockTransaction = await blocker.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("SELECT id FROM catalogo.proveedor WHERE id = @id FOR UPDATE", blocker, lockTransaction))
        {
            lockCommand.Parameters.AddWithValue("id", supplier.Id);
            Assert.Equal(supplier.Id, (Guid)(await lockCommand.ExecuteScalarAsync())!);
        }
        var one = first.PutAsJsonAsync($"/api/suppliers/{supplier.Id}", firstRequest);
        var two = second.PutAsJsonAsync($"/api/suppliers/{supplier.Id}", secondRequest);
        await Task.Delay(250);
        await lockTransaction.CommitAsync();
        var replies = await Task.WhenAll(one, two);
        var success = Assert.Single(replies, x => x.StatusCode == HttpStatusCode.OK);
        await ProblemAsync(Assert.Single(replies, x => x.StatusCode == HttpStatusCode.Conflict),
            HttpStatusCode.Conflict, "concurrency.conflict");
        var winner = await ReadAsync<SupplierDto>(success, HttpStatusCode.OK);
        Assert.Contains(winner.Name, new[] { "Ganador A", "Ganador B" });
        Assert.Equal(supplier.Version + 1, winner.Version);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persisted = await db.Proveedor.AsNoTracking().SingleAsync(x => x.Id == supplier.Id);
        Assert.Equal(winner.Name, persisted.NombreRazonSocial);
        Assert.Equal(winner.Version, persisted.Version);
        var audits = await db.EventoAuditoria.AsNoTracking().Where(x => x.EntidadId == supplier.Id).ToArrayAsync();
        Assert.Single(audits, x => x.Accion == "SUPPLIERUPDATED");
        Assert.Equal(2, audits.Length);
        foreach (var audit in audits) AssertSanitized(audit);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeferredEditCommitFailure_RestoresValuesVersionRelationsAndAudits()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        var supplier = await CreateAsync(client, new("NIT", "ROLLBACK-EDIT", "Antes", null, null, null, null));
        var unrelated = await CreateAsync(client, new(null, null, "Ajeno", null, null, null, null));
        var category = await ReadAsync<CategoryDto>(await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest("Categoría rollback", null, "RLB")), HttpStatusCode.Created);
        var units = await ReadAsync<MeasurementUnitDto[]>(await client.GetAsync("/api/measurement-units"), HttpStatusCode.OK);
        var unit = Assert.Single(units, x => x.Code == "UNIDAD");
        var product = await ReadAsync<ProductDto>(await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest(category.Id, unit.Id, supplier.Id, "SUP-ROLLBACK", null, null, true,
                "Producto preservado", null, 1000, null, null, 1)), HttpStatusCode.Created);
        var before = await SupplierAndRelationsSnapshotAsync(supplier.Id, unrelated.Id, product.Id);
        await InstallDeferredSupplierCommitFailureAsync("SUPPLIERUPDATED",
            $"id = '{supplier.Id}'::uuid AND nombre_razon_social = 'Rollback' AND version = 2");
        await ProblemAsync(await client.PutAsJsonAsync($"/api/suppliers/{supplier.Id}",
            new UpdateSupplierRequest("NIT", "ROLLBACK-EDIT", "Rollback", "300", "Privada",
                "private@example.com", "Privado", supplier.Version)), HttpStatusCode.Conflict, "resource.conflict");
        Assert.True(await DeferredFailureObservedAsync());
        Assert.Equal(before, await SupplierAndRelationsSnapshotAsync(supplier.Id, unrelated.Id, product.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeferredCommitFailure_RollsBackSupplierAndAudit()
    {
        using var client = Client();
        var users = await BootstrapAsync(client);
        Bearer(client, users.Admin.Token.AccessToken);
        await InstallDeferredSupplierCommitFailureAsync("SUPPLIERCREATED", "numero_documento = 'ROLLBACK'");
        var before = await StateAsync();
        await ProblemAsync(await client.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest("NIT", "ROLLBACK", "Revertir", null, null, null, null)), HttpStatusCode.Conflict, "resource.conflict");
        Assert.Equal(before, await StateAsync());
        Assert.True(await DeferredFailureObservedAsync());
    }

    private async Task<Users> BootstrapAsync(HttpClient client)
    {
        using var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        { Content = JsonContent.Create(new SetupFirstAdministratorRequest("Proveedores Test", "SUPPLIERS-TEST", "SUPPLIERS-INSTALL", "SUPPLIERS1", "SUPPLIERS-TERM", "Terminal", AdminName, "Administrador Proveedores", _adminPassword)) };
        setup.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setup)).StatusCode);
        var admin = await LoginAsync(client, AdminName, _adminPassword);
        Bearer(client, admin.AccessToken);
        var supervisorRole = await RoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRole = await RoleIdAsync(client, RoleCodes.Cashier);
        var supervisorUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(SupervisorName, "Supervisor", _supervisorPassword, [supervisorRole])), HttpStatusCode.Created);
        var cashierUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(CashierName, "Cajero", _cashierPassword, [cashierRole])), HttpStatusCode.Created);
        return new Users(new AuthUser(admin, await UserAsync(client, AdminName)),
            new AuthUser(await LoginAsync(client, SupervisorName, _supervisorPassword), supervisorUser),
            new AuthUser(await LoginAsync(client, CashierName, _cashierPassword), cashierUser));
    }

    private async Task<SupplierDto> CreateAsync(HttpClient client, CreateSupplierRequest request) =>
        await ReadAsync<SupplierDto>(await client.PostAsJsonAsync("/api/suppliers", request), HttpStatusCode.Created);
    private async Task<(int Suppliers, int Audits)> StateAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        return (await db.Proveedor.CountAsync(), await db.EventoAuditoria.CountAsync());
    }
    private async Task<string> ProductSnapshotAsync(Guid productId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var product = await db.Producto.AsNoTracking().Where(x => x.Id == productId).Select(x => new
        {
            x.Id, x.CategoriaId, x.UnidadMedidaId, x.ProveedorId, x.CodigoInterno, x.CodigoBarras,
            x.FormatoCodigoBarras, x.CodigoBarrasGenerado, x.Nombre, x.Descripcion, x.PrecioMinorista,
            x.PrecioMayorista, x.CostoPromedio, x.PorcentajeIva, x.StockActual, x.StockReservado,
            x.StockMinimo, x.Activo, x.UsuarioCreacionId, x.UsuarioModificacionId,
            x.FechaCreacion, x.FechaModificacion, x.Version
        }).SingleAsync();
        return JsonSerializer.Serialize(product);
    }
    private async Task<Guid?> ProductSupplierIdAsync(Guid productId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        return await db.Producto.AsNoTracking().Where(x => x.Id == productId)
            .Select(x => x.ProveedorId).SingleAsync();
    }
    private async Task<string> SupplierAndRelationsSnapshotAsync(Guid supplierId, Guid unrelatedId, Guid productId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var suppliers = await db.Proveedor.AsNoTracking().Where(x => x.Id == supplierId || x.Id == unrelatedId)
            .OrderBy(x => x.Id).Select(x => new
            {
                x.Id, x.TipoDocumento, x.NumeroDocumento, x.NombreRazonSocial, x.Telefono,
                x.Direccion, x.Correo, x.Observaciones, x.Activo, x.UsuarioCreacionId,
                x.UsuarioModificacionId, x.FechaCreacion, x.FechaModificacion, x.Version
            }).ToArrayAsync();
        var product = await db.Producto.AsNoTracking().Where(x => x.Id == productId).Select(x => new
        { x.Id, x.ProveedorId, x.Nombre, x.Activo, x.Version }).SingleAsync();
        var audits = await db.EventoAuditoria.AsNoTracking()
            .Where(x => x.EntidadId == supplierId || x.EntidadId == unrelatedId || x.EntidadId == productId)
            .OrderBy(x => x.Id).Select(x => new
            { x.Id, x.EntidadId, x.Accion, x.DatosAnteriores, x.DatosNuevos, x.FechaHora }).ToArrayAsync();
        return JsonSerializer.Serialize(new { suppliers, product, audits });
    }
    private async Task InstallDeferredSupplierCommitFailureAsync(string action, string supplierPredicate)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE SEQUENCE auditoria.test_supplier_commit_observed;
            CREATE FUNCTION auditoria.test_fail_supplier_commit() RETURNS trigger LANGUAGE plpgsql AS $trigger$
            BEGIN
              IF NEW.accion = '{{action}}' AND EXISTS (SELECT 1 FROM catalogo.proveedor WHERE {{supplierPredicate}}) THEN
                PERFORM nextval('auditoria.test_supplier_commit_observed');
                RAISE EXCEPTION 'forced supplier commit failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $trigger$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_supplier_commit AFTER INSERT ON auditoria.evento_auditoria
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION auditoria.test_fail_supplier_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
    private async Task<bool> DeferredFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT is_called FROM auditoria.test_supplier_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
    private static void AssertSanitized(EventoAuditoria audit)
    {
        Assert.Null(audit.DatosAnteriores); Assert.Null(audit.Motivo);
        using var document = JsonDocument.Parse(Assert.IsType<string>(audit.DatosNuevos));
        var root = document.RootElement;
        Assert.Equal(new[] { "isActive", "modifiedFields", "supplierId", "version" },
            root.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.True(Guid.TryParse(root.GetProperty("supplierId").GetString(), out _));
        Assert.True(root.GetProperty("version").GetInt64() > 0);
        foreach (var field in root.GetProperty("modifiedFields").EnumerateArray())
            Assert.Contains(field.GetString(), new[] { "isActive", "documentType", "documentNumber", "name", "phone", "address", "email", "observations" });
    }
    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("Npgsql", body, StringComparison.OrdinalIgnoreCase);
    }
    private HttpClient Client() => _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string name, string password)
    { client.DefaultRequestHeaders.Authorization = null; return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(name, password)), HttpStatusCode.OK); }
    private static async Task<Guid> RoleIdAsync(HttpClient client, string code) => (await ReadAsync<PagedResult<RoleDto>>(await client.GetAsync("/api/roles?pageSize=100"), HttpStatusCode.OK)).Items.Single(x => x.Code == code).Id;
    private static async Task<Guid> PermissionIdAsync(HttpClient client, string code) => (await ReadAsync<PagedResult<PermissionDto>>(await client.GetAsync("/api/permissions?pageSize=100"), HttpStatusCode.OK)).Items.Single(x => x.Code == code).Id;
    private static async Task<UserDto> UserAsync(HttpClient client, string name) => (await ReadAsync<PagedResult<UserDto>>(await client.GetAsync("/api/users?pageSize=100"), HttpStatusCode.OK)).Items.Single(x => x.UserName == name);
    private static async Task SetPermissionAsync(HttpClient client, Guid user, Guid permission, bool grant) => Assert.Equal(HttpStatusCode.NoContent,
        (await client.PutAsJsonAsync($"/api/users/{user}/permissions/{permission}", new SetUserPermissionRequest(grant))).StatusCode);
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    { var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}"); return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; }
    private static void Bearer(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static string Secret(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
    private sealed record AuthUser(AuthenticationResult Token, UserDto User);
    private sealed record Users(AuthUser Admin, AuthUser Supervisor, AuthUser Cashier);
}
