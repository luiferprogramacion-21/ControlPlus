using ControlPlus.Tests;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
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

/// <summary>Exercises the customer API against the official PostgreSQL schema; no persistent database is used.</summary>
public sealed class CustomersApiFlowTests : IAsyncLifetime
{
    private const string AdminName = "customers.admin.test";
    private const string SupervisorName = "customers.supervisor.test";
    private const string CashierName = "customers.cashier.test";

    private readonly string _masterKey = RandomSecret(48);
    private readonly string _jwtKey = RandomSecret(64);
    private readonly string _adminPassword = $"Customers-Admin-{RandomSecret(32)}!";
    private readonly string _supervisorPassword = $"Customers-Supervisor-{RandomSecret(32)}!";
    private readonly string _cashierPassword = $"Customers-Cashier-{RandomSecret(32)}!";
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
            builder.UseSetting("Jwt:Issuer", "controlplus-customers-tests");
            builder.UseSetting("Jwt:Audience", "controlplus-customers-client");
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
    public async Task Create_ValidatesNormalizesUniquenessUuidVersionAndSanitizedAudit()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);

        var withoutDocument = await CreateAsync(client, new(" cc ", " ana-77 ", "  Ana Pérez  ", " 300 123 ", " Calle 1 ", " ANA@EXAMPLE.COM ", null));
        Assert.True(withoutDocument.IsActive);
        Assert.Equal(1, withoutDocument.Version);
        Assert.Equal('7', withoutDocument.Id.ToString("D")[14]);
        Assert.Equal("Ana Pérez", withoutDocument.FullName);
        Assert.Equal("ANA-77", withoutDocument.DocumentNumber);
        Assert.Equal("ana@example.com", withoutDocument.Email);

        foreach (var type in new[] { "CC", "CE", "NIT", "PASAPORTE", "PPT", "OTRO" })
        {
            var created = await CreateAsync(client, new($" {type} ", $" ab-{type}.1 ", $"Cliente {type}", "300", "Dirección", null, null));
            Assert.Equal(type, created.DocumentType);
            Assert.Equal($"AB-{type}.1", created.DocumentNumber);
        }

        var duplicate = await client.PostAsJsonAsync("/api/customers",
            new CreateCustomerRequest("CC", "ab-cc.1", "Duplicado", "300", "Dirección", null, null));
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "resource.conflict");

        var before = await CustomerStateAsync();
        var invalid = new CreateCustomerRequest("INVALID", "1", "", new string('t', 31), "", "invalid-email", null);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/customers", invalid), HttpStatusCode.BadRequest, "validation.failed");
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/customers",
            new CreateCustomerRequest("CC", "N-1", null, "300", "Dirección", null, null)), HttpStatusCode.BadRequest, "validation.failed");
        // Decisión del 21 de noviembre de 2026: nombre, documento, teléfono y dirección son obligatorios.
        foreach (var missing in new[]
                 {
                     new CreateCustomerRequest(null, null, "Nombre", "300", "Dirección", null, null),
                     new CreateCustomerRequest("CC", null, "Nombre", "300", "Dirección", null, null),
                     new CreateCustomerRequest(null, "123", "Nombre", "300", "Dirección", null, null),
                     new CreateCustomerRequest("CC", "M-1", "Nombre", null, "Dirección", null, null),
                     new CreateCustomerRequest("CC", "M-2", "Nombre", "300", null, null, null),
                     new CreateCustomerRequest("CC", "M-3", new string('n', 201), "300", "Dirección", null, null),
                     new CreateCustomerRequest("CC", "M-4", "Nombre", "300", new string('d', 301), null, null)
                 })
            await AssertProblemAsync(await client.PostAsJsonAsync("/api/customers", missing), HttpStatusCode.BadRequest, "validation.failed");
        Assert.Equal(before, await CustomerStateAsync());

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persisted = await context.Cliente.AsNoTracking().SingleAsync(x => x.Id == withoutDocument.Id);
        Assert.Equal(users.Admin.User.Id, persisted.UsuarioCreacionId);
        Assert.NotEqual(default, persisted.FechaCreacion);
        Assert.Null(persisted.UsuarioModificacionId);
        Assert.Null(persisted.FechaModificacion);
        var audit = await context.EventoAuditoria.AsNoTracking().SingleAsync(x => x.EntidadId == withoutDocument.Id);
        Assert.Equal("CUSTOMERCREATED", audit.Accion);
        AssertSanitizedCustomerAudit(audit);
        var detail = audit.DatosNuevos ?? string.Empty;
        Assert.Contains("customerId", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activo", detail, StringComparison.OrdinalIgnoreCase);
        foreach (var sensitive in new[] { "Ana Pérez", "300 123", "ana@example.com", "Calle 1", "ANA-77", "AB-CC.1" })
            Assert.DoesNotContain(sensitive, detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_StrictJsonRejectsUnknownAndServerOwnedFieldsWithoutWrites()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);
        var before = await CustomerStateAsync();
        foreach (var property in new[]
                 {
                     "unknownRoot", "id", "version", "isActive", "activo", "createdByUserId",
                     "usuarioCreacionId", "createdAt", "fechaCreacion", "updatedByUserId", "updatedAt", "audit"
                 })
        {
            var payload = new Dictionary<string, object?>
            {
                ["fullName"] = "Estricto", ["address"] = "Dirección", [property] = property == "id" ? Guid.CreateVersion7() : "client-value"
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customers")
            { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
            var response = await client.SendAsync(request);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
            Assert.Equal(before, await CustomerStateAsync());
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_AcceptsOfficialBoundariesAndRejectsEachExceededBoundary()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);
        var validMail = $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 57)}.com";
        Assert.Equal(254, validMail.Length);
        var boundary = await CreateAsync(client, new("OTRO", new string('x', 50), new string('n', 200), new string('t', 30), new string('d', 300), validMail, null));
        Assert.Equal(200, boundary.FullName.Length);
        Assert.Equal(300, boundary.Address!.Length);
        Assert.Equal(30, boundary.Phone!.Length);
        Assert.Equal(254, boundary.Email!.Length);
        var before = await CustomerStateAsync();
        foreach (var invalid in new[]
                 {
                     new CreateCustomerRequest("OTRO", new string('x', 51), "Name", "300", "Address", null, null),
                     new CreateCustomerRequest("CC", "B-1", "Name", new string('t', 31), "Address", null, null),
                     new CreateCustomerRequest("CC", "B-2", "Name", "300", "Address", new string('a', 255), null),
                     new CreateCustomerRequest("CC", "B-3", "Name", "300", "Address", "not-an-email", null)
                 })
        {
            await AssertProblemAsync(await client.PostAsJsonAsync("/api/customers", invalid), HttpStatusCode.BadRequest, "validation.failed");
            Assert.Equal(before, await CustomerStateAsync());
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_EnforcesBasicAndManagePermissionsForDocumentsAndObservations()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        Assert.True(await HasEffectivePermissionAsync(users.Cashier.User.Id, PermissionCodes.CustomersBasicManage));
        Assert.False(await HasEffectivePermissionAsync(users.Cashier.User.Id, PermissionCodes.CustomersManage));

        SetBearer(client, users.Cashier.Token.AccessToken);
        var beforeRejected = await CustomerStateAsync();
        // Observaciones y cupo siguen reservados al permiso completo; el documento ya no (18 de noviembre de 2026).
        var forbiddenRequests = new[]
        {
            new CreateCustomerRequest(null, null, "Observaciones", null, "Dirección", null, "Dato privado"),
            new CreateCustomerRequest("CC", "BASIC-BOTH", "Documento y observaciones", null, "Dirección", null, "Dato privado")
        };
        foreach (var request in forbiddenRequests)
        {
            await AssertProblemAsync(
                await client.PostAsJsonAsync("/api/customers", request),
                HttpStatusCode.Forbidden,
                "authorization.forbidden");
            Assert.Equal(beforeRejected, await CustomerStateAsync());
        }

        var basicCustomer = await CreateAsync(client,
            new CreateCustomerRequest("CC", "BASIC-1", "Cliente básico", "300", "Dirección", "basic@example.com", null));
        Assert.Equal("CC", basicCustomer.DocumentType);
        Assert.Equal("BASIC-1", basicCustomer.DocumentNumber);
        Assert.Null(basicCustomer.Observations);
        var afterBasicCreate = await CustomerStateAsync();
        Assert.Equal(beforeRejected.CustomerCount + 1, afterBasicCreate.CustomerCount);
        Assert.Equal(beforeRejected.AuditCount + 1, afterBasicCreate.AuditCount);

        // El permiso básico escribe el documento (opcional, tipo y número juntos y sin repetirse).
        var withDocument = await CreateAsync(client,
            new CreateCustomerRequest("CC", "BASIC-DOCUMENT", "Documento básico", "300", "Dirección", null, null));
        Assert.Equal("CC", withDocument.DocumentType);
        Assert.Equal("BASIC-DOCUMENT", withDocument.DocumentNumber);
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/customers",
            new CreateCustomerRequest("CC", null, "Tipo aislado", "300", "Dirección", null, null)),
            HttpStatusCode.BadRequest, "validation.failed");
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/customers",
            new CreateCustomerRequest("CC", "BASIC-DOCUMENT", "Documento repetido", "300", "Dirección", null, null)),
            HttpStatusCode.Conflict, "resource.conflict");

        SetBearer(client, users.Admin.Token.AccessToken);
        var managedCustomer = await CreateAsync(client,
            new CreateCustomerRequest("CC", "ADMIN-DOCUMENT", "Cliente completo", "300", "Dirección", null, "Observación válida"));
        Assert.Equal("CC", managedCustomer.DocumentType);
        Assert.Equal("ADMIN-DOCUMENT", managedCustomer.DocumentNumber);
        Assert.Equal("Observación válida", managedCustomer.Observations);

        var managePermission = await PermissionIdAsync(client, PermissionCodes.CustomersManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.User.Id}/permissions/{managePermission}",
            new SetUserPermissionRequest(true))).StatusCode);
        var granted = await LoginAsync(client, CashierName, _cashierPassword);
        Assert.True(await HasEffectivePermissionAsync(users.Cashier.User.Id, PermissionCodes.CustomersManage));
        SetBearer(client, granted.AccessToken);
        var individuallyManaged = await CreateAsync(client,
            new CreateCustomerRequest("PPT", "GRANTED-DOCUMENT", "Concesión individual", "300", "Dirección", null, "Observación concedida"));
        Assert.Equal("PPT", individuallyManaged.DocumentType);
        Assert.Equal("GRANTED-DOCUMENT", individuallyManaged.DocumentNumber);
        Assert.Equal("Observación concedida", individuallyManaged.Observations);

        SetBearer(client, users.Admin.Token.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.User.Id}/permissions/{managePermission}",
            new SetUserPermissionRequest(false))).StatusCode);
        var revoked = await LoginAsync(client, CashierName, _cashierPassword);
        Assert.False(await HasEffectivePermissionAsync(users.Cashier.User.Id, PermissionCodes.CustomersManage));
        SetBearer(client, revoked.AccessToken);
        var beforeRevokedRequest = await CustomerStateAsync();
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/api/customers",
                new CreateCustomerRequest("CE", "REVOKED-DOCUMENT", "Revocación individual", null, "Dirección", null, "Observación")),
            HttpStatusCode.Forbidden,
            "authorization.forbidden");
        Assert.Equal(beforeRevokedRequest, await CustomerStateAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_InactiveCustomerRequiresManageAndHonorsIndividualGrantAndRevocation()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);
        var active = await CreateAsync(client,
            new CreateCustomerRequest("CC", "INACTIVE-GATE", "Cliente antes de inactivar", "300", "Dirección", "before@example.com", "Observación inicial"));
        var inactive = await SetStatusAsync(client, active.Id, false, active.Version);
        Assert.False(inactive.IsActive);

        Assert.True(await HasEffectivePermissionAsync(users.Cashier.User.Id, PermissionCodes.CustomersBasicManage));
        Assert.False(await HasEffectivePermissionAsync(users.Cashier.User.Id, PermissionCodes.CustomersManage));
        SetBearer(client, users.Cashier.Token.AccessToken);
        var beforeBasicAttempt = await CustomerAndAuditSnapshotAsync(active.Id);
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/api/customers/{active.Id}",
                new UpdateCustomerRequest("CC", "INACTIVE-GATE", "Cambio BASIC rechazado", "301", "Otra dirección", "after@example.com", "Observación inicial", inactive.Version)),
            HttpStatusCode.Forbidden,
            "authorization.forbidden");
        Assert.Equal(beforeBasicAttempt, await CustomerAndAuditSnapshotAsync(active.Id));

        SetBearer(client, users.Admin.Token.AccessToken);
        var managePermission = await PermissionIdAsync(client, PermissionCodes.CustomersManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.User.Id}/permissions/{managePermission}",
            new SetUserPermissionRequest(true))).StatusCode);
        var granted = await LoginAsync(client, CashierName, _cashierPassword);
        Assert.True(await HasEffectivePermissionAsync(users.Cashier.User.Id, PermissionCodes.CustomersManage));
        SetBearer(client, granted.AccessToken);
        var managed = await ReadAsync<CustomerDto>(await client.PutAsJsonAsync($"/api/customers/{active.Id}",
            new UpdateCustomerRequest("CE", "MANAGED-INACTIVE", "Cambio autorizado", "302", "Dirección autorizada", "managed@example.com", "Observación autorizada", inactive.Version)),
            HttpStatusCode.OK);
        Assert.False(managed.IsActive);
        Assert.Equal(inactive.Version + 1, managed.Version);
        Assert.Equal("MANAGED-INACTIVE", managed.DocumentNumber);
        Assert.Equal("Observación autorizada", managed.Observations);

        SetBearer(client, users.Admin.Token.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.User.Id}/permissions/{managePermission}",
            new SetUserPermissionRequest(false))).StatusCode);
        var revoked = await LoginAsync(client, CashierName, _cashierPassword);
        Assert.False(await HasEffectivePermissionAsync(users.Cashier.User.Id, PermissionCodes.CustomersManage));
        SetBearer(client, revoked.AccessToken);
        var beforeRevokedAttempt = await CustomerAndAuditSnapshotAsync(active.Id);
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/api/customers/{active.Id}",
                new UpdateCustomerRequest("CE", "MANAGED-INACTIVE", "Cambio tras revocar", "303", "Otra dirección", "revoked@example.com", "Observación autorizada", managed.Version)),
            HttpStatusCode.Forbidden,
            "authorization.forbidden");
        Assert.Equal(beforeRevokedAttempt, await CustomerAndAuditSnapshotAsync(active.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task List_SearchPagingOrderingAndInactiveVisibilityFollowEffectivePermissions()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);
        var zulu = await CreateAsync(client, new("CC", "DOC-777", "zulu", "300-777", "D", null, null));
        var alphaOne = await CreateAsync(client, new("CC", "AL-1", "Alpha", "555-A", "D", null, null));
        var alphaTwo = await CreateAsync(client, new("CC", "AL-2", "Alpha", "555-B", "D", null, null));
        _ = await CreateAsync(client, new("CC", "BE-1", "Beta", "555-C", "D", null, null));

        var defaults = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers"), HttpStatusCode.OK);
        Assert.Equal(1, defaults.Page);
        Assert.Equal(20, defaults.PageSize);
        Assert.Equal(4, defaults.TotalCount);
        Assert.All(defaults.Items, x => Assert.True(x.IsActive));
        var activeByDocument = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?search=doc-7"), HttpStatusCode.OK);
        Assert.Equal(zulu.Id, Assert.Single(activeByDocument.Items).Id);
        await SetStatusAsync(client, zulu.Id, false, zulu.Version);

        var byDocument = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?search=doc-7"), HttpStatusCode.OK);
        Assert.Empty(byDocument.Items);
        var byName = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?search=ALPHA&pageSize=2"), HttpStatusCode.OK);
        Assert.Equal(new[] { alphaOne.Id, alphaTwo.Id }.OrderBy(x => x), byName.Items.Select(x => x.Id).OrderBy(x => x));
        var byPhone = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?search=555-a"), HttpStatusCode.OK);
        Assert.Single(byPhone.Items);
        Assert.Equal(alphaOne.Id, byPhone.Items.Single().Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/customers?pageSize=101")).StatusCode);
        var ordered = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?page=1&pageSize=100"), HttpStatusCode.OK);
        var repeatedOrder = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?page=1&pageSize=100"), HttpStatusCode.OK);
        Assert.Equal(ordered.Items.Select(x => x.Id), repeatedOrder.Items.Select(x => x.Id));
        Assert.Equal(new[] { "Alpha", "Alpha", "Beta" }, ordered.Items.Select(x => x.FullName));
        Assert.Equal(new[] { alphaOne.Id, alphaTwo.Id }.OrderBy(x => x), ordered.Items.Take(2).Select(x => x.Id));
        Assert.All(ordered.Items, x => Assert.True(x.IsActive));
        var pageOne = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?page=1&pageSize=1"), HttpStatusCode.OK);
        var pageTwo = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?page=2&pageSize=1"), HttpStatusCode.OK);
        Assert.Single(pageOne.Items); Assert.Single(pageTwo.Items); Assert.NotEqual(pageOne.Items.Single().Id, pageTwo.Items.Single().Id);

        SetBearer(client, users.Cashier.Token.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customers?status=inactive")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/customers/{zulu.Id}")).StatusCode);
        SetBearer(client, users.Supervisor.Token.AccessToken);
        var inactive = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?status=inactive"), HttpStatusCode.OK);
        Assert.Single(inactive.Items);
        Assert.Equal(zulu.Id, inactive.Items.Single().Id);
        var all = await ReadAsync<PagedResult<CustomerDto>>(await client.GetAsync("/api/customers?status=all"), HttpStatusCode.OK);
        Assert.Equal(4, all.TotalCount);
        Assert.False((await ReadAsync<CustomerDto>(await client.GetAsync($"/api/customers/{zulu.Id}"), HttpStatusCode.OK)).IsActive);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task List_UsesEscapedCaseInsensitivePrefixesWithoutWildcardInjection()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);

        var percent = await CreateAsync(client, new("OTRO", "DOC%LITERAL", "Percent%Literal", "555_100", "D", null, null));
        _ = await CreateAsync(client, new("OTRO", "DOCXLITERAL", "PercentXLiteral", "555A100", "D", null, null));
        var slash = await CreateAsync(client, new("CC", "SL-1", "Slash\\Literal", "777-1", "D", null, null));
        _ = await CreateAsync(client, new("CC", "SL-2", "SlashXLiteral", "777-2", "D", null, null));

        Assert.Equal(percent.Id, Assert.Single((await SearchAsync(client, "PERCENT%")).Items).Id);
        Assert.Equal(percent.Id, Assert.Single((await SearchAsync(client, "doc%")).Items).Id);
        Assert.Equal(percent.Id, Assert.Single((await SearchAsync(client, "555_")).Items).Id);
        Assert.Equal(slash.Id, Assert.Single((await SearchAsync(client, "slash\\")).Items).Id);
        Assert.Empty((await SearchAsync(client, "Literal")).Items);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_ConcurrentDuplicateDocumentCommitsExactlyOneCustomerAndAudit()
    {
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        var users = await BootstrapAsync(firstClient);
        SetBearer(firstClient, users.Admin.Token.AccessToken);
        SetBearer(secondClient, users.Admin.Token.AccessToken);
        var before = await CustomerStateAsync();
        const string documentNumber = "RACE-DOCUMENT-SENSITIVE";

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/customers",
                new CreateCustomerRequest("CC", documentNumber, "Race One", "300", "Address", null, null)),
            secondClient.PostAsJsonAsync("/api/customers",
                new CreateCustomerRequest("CC", documentNumber, "Race Two", "300", "Address", null, null)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);
        var conflictBody = await conflict.Content.ReadAsStringAsync();
        using (var problem = JsonDocument.Parse(conflictBody))
            Assert.Equal("resource.conflict", problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(documentNumber, conflictBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("uq_cliente_documento", conflictBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Npgsql", conflictBody, StringComparison.OrdinalIgnoreCase);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var persisted = await context.Cliente.AsNoTracking()
            .SingleAsync(x => x.TipoDocumento == "CC" && x.NumeroDocumento == documentNumber);
        Assert.Equal(1, persisted.Version);
        var audits = await context.EventoAuditoria.AsNoTracking()
            .Where(x => x.EntidadId == persisted.Id && x.Accion == "CUSTOMERCREATED")
            .ToArrayAsync();
        AssertSanitizedCustomerAudit(Assert.Single(audits));
        var after = await CustomerStateAsync();
        Assert.Equal(before.CustomerCount + 1, after.CustomerCount);
        Assert.Equal(before.AuditCount + 1, after.AuditCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateStatusPermissionsConcurrencyAndIndividualOverridesWorkWithoutRejectedWrites()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);
        var customer = await CreateAsync(client, new("CC", "INITIAL", "Inicial", "300", "Dirección", "initial@example.com", "private"));
        SetBearer(client, users.Cashier.Token.AccessToken);
        var basic = new UpdateCustomerRequest("CC", "INITIAL", "Básico", "301", "Nueva dirección", "BASIC@EXAMPLE.COM", "private", customer.Version);
        var updated = await ReadAsync<CustomerDto>(await client.PutAsJsonAsync($"/api/customers/{customer.Id}", basic), HttpStatusCode.OK);
        Assert.Equal(2, updated.Version);
        Assert.Equal("basic@example.com", updated.Email);
        var beforeRejected = await CustomerStateAsync();
        var forbidden = await client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            basic with { DocumentNumber = "CHANGED", Observations = "changed with document", ExpectedVersion = updated.Version });
        await AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, "authorization.forbidden");
        await AssertProblemAsync(await client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            basic with { Observations = "changed", ExpectedVersion = updated.Version }), HttpStatusCode.Forbidden, "authorization.forbidden");
        Assert.Equal(beforeRejected, await CustomerStateAsync());

        SetBearer(client, users.Supervisor.Token.AccessToken);
        var full = await ReadAsync<CustomerDto>(await client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            basic with { DocumentNumber = "MANAGED", Observations = "managed", ExpectedVersion = updated.Version }), HttpStatusCode.OK);
        Assert.Equal(3, full.Version);
        await AssertProblemAsync(await client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            basic with { ExpectedVersion = updated.Version }), HttpStatusCode.Conflict, "concurrency.conflict");
        var inactive = await SetStatusAsync(client, customer.Id, false, full.Version);
        Assert.False(inactive.IsActive);
        var active = await SetStatusAsync(client, customer.Id, true, inactive.Version);
        Assert.True(active.IsActive);

        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var audits = await db.EventoAuditoria.AsNoTracking().Where(x => x.EntidadId == customer.Id).ToArrayAsync();
            Assert.Contains(audits, x => x.Accion == "CUSTOMERUPDATED");
            Assert.Contains(audits, x => x.Accion == "CUSTOMERDEACTIVATED");
            Assert.Contains(audits, x => x.Accion == "CUSTOMERREACTIVATED");
            foreach (var audit in audits)
            {
                AssertSanitizedCustomerAudit(audit);
                var text = string.Join('|', audit.DatosAnteriores, audit.DatosNuevos, audit.Motivo);
                Assert.DoesNotContain("Inicial", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("MANAGED", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("initial@example.com", text, StringComparison.OrdinalIgnoreCase);
            }
        }

        SetBearer(client, users.Admin.Token.AccessToken);
        var customersBasicPermission = await PermissionIdAsync(client, PermissionCodes.CustomersBasicManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.User.Id}/permissions/{customersBasicPermission}", new SetUserPermissionRequest(false))).StatusCode);
        var revoked = await LoginAsync(client, CashierName, _cashierPassword);
        SetBearer(client, revoked.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customers")).StatusCode);
        SetBearer(client, users.Admin.Token.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.User.Id}/permissions/{customersBasicPermission}", new SetUserPermissionRequest(true))).StatusCode);
        var granted = await LoginAsync(client, CashierName, _cashierPassword);
        SetBearer(client, granted.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customers")).StatusCode);

        SetBearer(client, users.Admin.Token.AccessToken);
        var customersManagePermission = await PermissionIdAsync(client, PermissionCodes.CustomersManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.User.Id}/permissions/{customersManagePermission}", new SetUserPermissionRequest(true))).StatusCode);
        var individuallyElevated = await LoginAsync(client, CashierName, _cashierPassword);
        SetBearer(client, individuallyElevated.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customers?status=all")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateAndStatus_StrictJsonRequiredVersionAndConcurrentWritersAreSafe()
    {
        using var setupClient = CreateClient();
        using var firstClient = CreateClient();
        using var secondClient = CreateClient();
        var users = await BootstrapAsync(setupClient);
        SetBearer(setupClient, users.Admin.Token.AccessToken);
        var customer = await CreateAsync(setupClient, new("CC", "CONC-1", "Concurrent", "300", "Address", null, null));
        SetBearer(firstClient, users.Admin.Token.AccessToken);
        SetBearer(secondClient, users.Admin.Token.AccessToken);

        var state = await CustomerStateAsync();
        using (var invalidRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/customers/{customer.Id}")
               { Content = new StringContent("{\"fullName\":\"Name\",\"address\":\"Address\",\"expectedVersion\":1,\"unknown\":true}", Encoding.UTF8, "application/json") })
            await AssertProblemAsync(await setupClient.SendAsync(invalidRequest), HttpStatusCode.BadRequest, "validation.failed");
        using (var missingUpdateVersion = new HttpRequestMessage(HttpMethod.Put, $"/api/customers/{customer.Id}")
               { Content = new StringContent("{\"fullName\":\"Name\",\"address\":\"Address\"}", Encoding.UTF8, "application/json") })
            await AssertProblemAsync(await setupClient.SendAsync(missingUpdateVersion), HttpStatusCode.BadRequest, "validation.failed");
        using (var missingStatus = new HttpRequestMessage(HttpMethod.Put, $"/api/customers/{customer.Id}/status")
               { Content = new StringContent("{\"expectedVersion\":1}", Encoding.UTF8, "application/json") })
            await AssertProblemAsync(await setupClient.SendAsync(missingStatus), HttpStatusCode.BadRequest, "validation.failed");
        using (var unknownStatus = new HttpRequestMessage(HttpMethod.Put, $"/api/customers/{customer.Id}/status")
               { Content = new StringContent("{\"isActive\":false,\"expectedVersion\":1,\"unknown\":true}", Encoding.UTF8, "application/json") })
            await AssertProblemAsync(await setupClient.SendAsync(unknownStatus), HttpStatusCode.BadRequest, "validation.failed");
        Assert.Equal(state, await CustomerStateAsync());

        await using var blocker = new NpgsqlConnection(_postgres.GetConnectionString());
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("SELECT id FROM catalogo.cliente WHERE id = @id FOR UPDATE", blocker, transaction))
        {
            lockCommand.Parameters.AddWithValue("id", customer.Id);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync());
        }
        var one = firstClient.PutAsJsonAsync($"/api/customers/{customer.Id}", new UpdateCustomerRequest("CC", "CONC-1", "First", "300", "Address", null, null, customer.Version));
        var two = secondClient.PutAsJsonAsync($"/api/customers/{customer.Id}", new UpdateCustomerRequest("CC", "CONC-1", "Second", "300", "Address", null, null, customer.Version));
        await Task.Delay(250);
        await transaction.CommitAsync();
        var results = await Task.WhenAll(one, two);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var persisted = await ReadAsync<CustomerDto>(await setupClient.GetAsync($"/api/customers/{customer.Id}"), HttpStatusCode.OK);
        Assert.Equal(2, persisted.Version);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteIsMethodNotAllowedAndDeferredCommitFailureRollsBackCustomerAndAudit()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);
        var existing = await CreateAsync(client, new("CC", "KEEP-1", "No eliminable", "300", "Dirección", null, null));
        var beforeDelete = await CustomerStateAsync();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"/api/customers/{existing.Id}")).StatusCode);
        Assert.Equal(beforeDelete, await CustomerStateAsync());
        var before = await CustomerStateAsync();
        await InstallDeferredCustomerCreateFailureAsync();
        var response = await client.PostAsJsonAsync("/api/customers",
            new CreateCustomerRequest("CC", "ROLLBACK", "Rollback", "300", "Dirección", "rollback@example.com", "sensitive"));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "resource.conflict");
        Assert.True(await DeferredFailureObservedAsync());
        Assert.Equal(before, await CustomerStateAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Update_DeferredCommitFailureRollsBackCustomerVersionAndAuditCompletely()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);
        var customer = await CreateAsync(client, new("CC", "RB-1", "Before", "300", "Address", null, null));
        var before = await CustomerStateAsync();
        await InstallDeferredCustomerUpdateFailureAsync(customer.Id);
        var response = await client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            new UpdateCustomerRequest("CC", "RB-1", "Rollback", "300", "Address", null, null, customer.Version));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "resource.conflict");
        Assert.True(await DeferredUpdateFailureObservedAsync());
        Assert.Equal(before, await CustomerStateAsync());
        var persisted = await ReadAsync<CustomerDto>(await client.GetAsync($"/api/customers/{customer.Id}"), HttpStatusCode.OK);
        Assert.Equal("Before", persisted.FullName); Assert.Equal(1, persisted.Version);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task History_RequiresEffectivePermissionAndHidesInactiveCustomers()
    {
        using var client = CreateClient();
        var users = await BootstrapAsync(client);
        SetBearer(client, users.Admin.Token.AccessToken);
        var customer = await CreateAsync(client, new("CC", "HIST-1", "Historial", "300", "Dirección", null, null));
        var url = $"/api/customers/{customer.Id}/history";
        var empty = await ReadAsync<PagedResult<CustomerHistoryItem>>(await client.GetAsync(url), HttpStatusCode.OK);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        var nextPage = await ReadAsync<PagedResult<CustomerHistoryItem>>(
            await client.GetAsync(url + "?page=2&pageSize=1"), HttpStatusCode.OK);
        Assert.Empty(nextPage.Items);
        Assert.Equal(2, nextPage.Page);
        Assert.Equal(1, nextPage.PageSize);
        await AssertProblemAsync(await client.GetAsync(url + "?page=0"), HttpStatusCode.BadRequest, "validation.failed");
        await AssertProblemAsync(await client.GetAsync(url + "?pageSize=101"), HttpStatusCode.BadRequest, "validation.failed");
        await AssertProblemAsync(await client.GetAsync($"/api/customers/{Guid.NewGuid()}/history"), HttpStatusCode.NotFound, "resource.not_found");

        SetBearer(client, users.Cashier.Token.AccessToken);
        var cashierPage = await ReadAsync<PagedResult<CustomerHistoryItem>>(await client.GetAsync(url), HttpStatusCode.OK);
        Assert.Empty(cashierPage.Items);
        SetBearer(client, users.Admin.Token.AccessToken);
        var historyPermission = await PermissionIdAsync(client, PermissionCodes.CustomersHistoryRead);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Cashier.User.Id}/permissions/{historyPermission}",
            new SetUserPermissionRequest(false))).StatusCode);
        var revoked = await LoginAsync(client, CashierName, _cashierPassword);
        SetBearer(client, revoked.AccessToken);
        await AssertProblemAsync(await client.GetAsync(url), HttpStatusCode.Forbidden, "authorization.forbidden");

        SetBearer(client, users.Admin.Token.AccessToken);
        await SetStatusAsync(client, customer.Id, false, customer.Version);
        var managePermission = await PermissionIdAsync(client, PermissionCodes.CustomersManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Supervisor.User.Id}/permissions/{managePermission}",
            new SetUserPermissionRequest(false))).StatusCode);
        var limitedSupervisor = await LoginAsync(client, SupervisorName, _supervisorPassword);
        SetBearer(client, limitedSupervisor.AccessToken);
        await AssertProblemAsync(await client.GetAsync(url), HttpStatusCode.NotFound, "resource.not_found");
        SetBearer(client, users.Admin.Token.AccessToken);
        Assert.Empty((await ReadAsync<PagedResult<CustomerHistoryItem>>(await client.GetAsync(url), HttpStatusCode.OK)).Items);
    }

    private async Task<TestUsers> BootstrapAsync(HttpClient client)
    {
        using var setup = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
        {
            Content = JsonContent.Create(new SetupFirstAdministratorRequest("Clientes Test", "CUSTOMERS-TEST", "CUSTOMERS-INSTALL", "CUSTOMERS1", "CUSTOMERS-TERM", "Terminal", AdminName, "Administrador Clientes", _adminPassword))
        };
        setup.Headers.Add("X-ControlPlus-Master-Key", _masterKey);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(setup)).StatusCode);
        var admin = await LoginAsync(client, AdminName, _adminPassword);
        SetBearer(client, admin.AccessToken);
        var supervisorRole = await RoleIdAsync(client, RoleCodes.Supervisor);
        var cashierRole = await RoleIdAsync(client, RoleCodes.Cashier);
        var supervisorUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(SupervisorName, "Supervisor", _supervisorPassword, [supervisorRole])), HttpStatusCode.Created);
        var cashierUser = await ReadAsync<UserDto>(await client.PostAsJsonAsync("/api/users", new CreateUserRequest(CashierName, "Cajero", _cashierPassword, [cashierRole])), HttpStatusCode.Created);
        return new TestUsers(
            new AuthUser(admin, await UsersAsync(client, AdminName)),
            new AuthUser(await LoginAsync(client, SupervisorName, _supervisorPassword), supervisorUser),
            new AuthUser(await LoginAsync(client, CashierName, _cashierPassword), cashierUser));
    }

    private static async Task<CustomerDto> CreateAsync(HttpClient client, CreateCustomerRequest request) =>
        await ReadAsync<CustomerDto>(await client.PostAsJsonAsync("/api/customers", request), HttpStatusCode.Created);
    private static async Task<CustomerDto> SetStatusAsync(HttpClient client, Guid id, bool active, long version) =>
        await ReadAsync<CustomerDto>(await client.PutAsJsonAsync($"/api/customers/{id}/status", new SetCustomerStatusRequest(active, version)), HttpStatusCode.OK);
    private static async Task<PagedResult<CustomerDto>> SearchAsync(HttpClient client, string search) =>
        await ReadAsync<PagedResult<CustomerDto>>(
            await client.GetAsync($"/api/customers?search={Uri.EscapeDataString(search)}"),
            HttpStatusCode.OK);
    private async Task<CustomerState> CustomerStateAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        return new(await db.Cliente.CountAsync(), await db.EventoAuditoria.CountAsync(),
            string.Join(',', await db.Cliente.OrderBy(x => x.Id).Select(x => x.Version).ToArrayAsync()));
    }
    private async Task<bool> HasEffectivePermissionAsync(Guid userId, string permissionCode)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        return await permissions.HasPermissionAsync(userId, permissionCode);
    }
    private async Task<string> CustomerAndAuditSnapshotAsync(Guid customerId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var customer = await db.Cliente.AsNoTracking().Where(x => x.Id == customerId)
            .Select(x => new
            {
                x.Id,
                x.TipoDocumento,
                x.NumeroDocumento,
                x.NombreCompleto,
                x.Telefono,
                x.Direccion,
                x.Correo,
                x.Observaciones,
                x.Activo,
                x.UsuarioCreacionId,
                x.UsuarioModificacionId,
                x.FechaCreacion,
                x.FechaModificacion,
                x.Version
            })
            .SingleAsync();
        var audits = await db.EventoAuditoria.AsNoTracking()
            .Where(x => x.EntidadId == customerId)
            .OrderBy(x => x.FechaHora)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.UsuarioId,
                x.FechaHora,
                x.Accion,
                x.Entidad,
                x.EntidadId,
                x.Resultado,
                x.Motivo,
                x.DatosAnteriores,
                x.DatosNuevos,
                x.CorrelacionId
            })
            .ToArrayAsync();
        return JsonSerializer.Serialize(new { customer, audits });
    }
    private async Task InstallDeferredCustomerCreateFailureAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            CREATE SEQUENCE auditoria.test_customer_commit_observed;
            CREATE FUNCTION auditoria.test_fail_customer_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.accion = 'CUSTOMERCREATED' AND EXISTS (SELECT 1 FROM catalogo.cliente WHERE numero_documento = 'ROLLBACK') THEN
                PERFORM nextval('auditoria.test_customer_commit_observed');
                RAISE EXCEPTION 'forced customer commit failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_customer_commit AFTER INSERT ON auditoria.evento_auditoria DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION auditoria.test_fail_customer_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
    private async Task<bool> DeferredFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT is_called FROM auditoria.test_customer_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
    private async Task InstallDeferredCustomerUpdateFailureAsync(Guid customerId)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            CREATE SEQUENCE auditoria.test_customer_update_commit_observed;
            CREATE FUNCTION auditoria.test_fail_customer_update_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.accion = 'CUSTOMERUPDATED'
                 AND NEW.entidad_id = '{{customerId}}'::uuid
                 AND EXISTS (SELECT 1 FROM catalogo.cliente WHERE id = '{{customerId}}'::uuid AND nombre_completo = 'Rollback' AND version = 2)
              THEN
                PERFORM nextval('auditoria.test_customer_update_commit_observed');
                RAISE EXCEPTION 'forced customer update commit failure' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER zz_test_fail_customer_update_commit AFTER INSERT ON auditoria.evento_auditoria DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION auditoria.test_fail_customer_update_commit();
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
    private async Task<bool> DeferredUpdateFailureObservedAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT is_called FROM auditoria.test_customer_update_commit_observed", connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }
    private HttpClient CreateClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
    private static async Task<AuthenticationResult> LoginAsync(HttpClient client, string user, string password) { client.DefaultRequestHeaders.Authorization = null; return await ReadAsync<AuthenticationResult>(await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user, password)), HttpStatusCode.OK); }
    private static async Task<Guid> RoleIdAsync(HttpClient client, string code) => (await ReadAsync<PagedResult<RoleDto>>(await client.GetAsync("/api/roles?pageSize=100"), HttpStatusCode.OK)).Items.Single(x => x.Code == code).Id;
    private static async Task<Guid> PermissionIdAsync(HttpClient client, string code) => (await ReadAsync<PagedResult<PermissionDto>>(await client.GetAsync("/api/permissions?pageSize=100"), HttpStatusCode.OK)).Items.Single(x => x.Code == code).Id;
    private static async Task<UserDto> UsersAsync(HttpClient client, string name) => (await ReadAsync<PagedResult<UserDto>>(await client.GetAsync("/api/users?pageSize=100"), HttpStatusCode.OK)).Items.Single(x => x.UserName == name);
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected) { var body = await response.Content.ReadAsStringAsync(); Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}"); return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!; }
    private static void SetBearer(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static string RandomSecret(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
    private static void AssertSanitizedCustomerAudit(EventoAuditoria audit)
    {
        Assert.Null(audit.DatosAnteriores);
        Assert.Null(audit.Motivo);
        using var document = JsonDocument.Parse(Assert.IsType<string>(audit.DatosNuevos));
        var root = document.RootElement;
        Assert.Equal(new[] { "active", "customerId", "modifiedFields" },
            root.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.True(Guid.TryParse(root.GetProperty("customerId").GetString(), out _));
        Assert.True(root.GetProperty("active").ValueKind is JsonValueKind.True or JsonValueKind.False);
        var allowedFields = new[]
        {
            "activo", "tipoDocumento", "numeroDocumento", "nombreCompleto", "telefono", "direccion", "correo", "observaciones"
        };
        foreach (var field in root.GetProperty("modifiedFields").EnumerateArray())
            Assert.Contains(Assert.IsType<string>(field.GetString()), allowedFields);
    }
    private sealed record AuthUser(AuthenticationResult Token, UserDto User);
    private sealed record TestUsers(AuthUser Admin, AuthUser Supervisor, AuthUser Cashier);
    private sealed record CustomerState(int CustomerCount, int AuditCount, string Versions);
}
