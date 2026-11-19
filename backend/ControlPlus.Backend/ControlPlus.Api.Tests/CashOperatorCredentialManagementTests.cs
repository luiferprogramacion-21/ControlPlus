using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using ControlPlus.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class CashApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task OperatorCredential_RevocationWhileStartWaitsOnShift_CannotCreateAnActiveSessionFromStaleSnapshot()
    {
        using var manager = CreateClient();
        await SetupAsync(manager);
        var administrator = await LoginAsync(manager, AdministratorUserName, AdministratorPassword);
        SetBearer(manager, administrator.AccessToken);
        var users = await CreateOperationalUsersAsync(manager);
        await ReadSuccessAsync<CashRegisterDto>(await manager.PostAsJsonAsync(
            "/api/cash/register", new ConfigureCashRegisterRequest("CARD-RACE", "Caja concurrencia carné")), HttpStatusCode.Created);
        var state = await ReadSuccessAsync<CashStateDto>(await manager.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        await ReadSuccessAsync<CashStateDto>(await manager.PutAsJsonAsync("/api/cash/shift-mode",
            new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        var shift = await ReadSuccessAsync<CashShiftDto>(await manager.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Concurrencia carné")), HttpStatusCode.Created);
        var issued = await ReadSuccessAsync<IssuedOperatorCredentialDto>(await manager.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{users.Cashier.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        using var scanner = CreateClient();
        var cashier = await LoginAsync(scanner, CashierUserName, CashierPassword);
        SetBearer(scanner, cashier.AccessToken);

        // Stage the same shift lock and credential update used by revocation, withholding COMMIT.
        // The scanner then establishes its Serializable snapshot while waiting for this shift.
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        await using var revokeTransaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        await db.TurnoCaja.FromSqlInterpolated($"SELECT * FROM caja.turno_caja WHERE id = {shift.Id} FOR UPDATE").ToArrayAsync();
        var credential = await db.CredencialUsuario.SingleAsync(x => x.Id == issued.CredentialId);
        credential.Revoke(administrator.User.Id, _clock.UtcNow, "ELIMINACION_ADMINISTRADOR");
        await db.SaveChangesAsync();
        var revokePid = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
        var start = scanner.PostAsJsonAsync("/api/cash/operator-sessions", new StartOperatorSessionRequest(issued.CredentialToken));
        try
        {
            await using var observer = new NpgsqlConnection(_postgres.GetConnectionString());
            await observer.OpenAsync();
            var waiting = false;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!waiting)
            {
                await using var command = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND @blocker = ANY(pg_blocking_pids(pid)))", observer);
                command.Parameters.AddWithValue("blocker", revokePid);
                waiting = (bool)(await command.ExecuteScalarAsync(deadline.Token))!;
                if (!waiting) await Task.Delay(25, deadline.Token);
            }
            await revokeTransaction.CommitAsync();
            Assert.Equal(HttpStatusCode.Conflict, (await start).StatusCode);
            Assert.False(await db.SesionOperador.AsNoTracking().AnyAsync(x =>
                x.CredencialUsuarioId == issued.CredentialId && x.Estado == CashConstants.ActiveSessionState));
        }
        finally
        {
            // Release any waiter even if observing the controlled interleaving failed.
            await revokeTransaction.DisposeAsync();
            await start;
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task OperatorCredential_CanBeViewedAndRevokedByAdministratorWithoutLosingHistory()
    {
        using var client = CreateClient();
        await SetupAsync(client);
        var administrator = await LoginAsync(client, AdministratorUserName, AdministratorPassword);
        SetBearer(client, administrator.AccessToken);
        var users = await CreateOperationalUsersAsync(client);
        await ReadSuccessAsync<CashRegisterDto>(await client.PostAsJsonAsync(
            "/api/cash/register", new ConfigureCashRegisterRequest("CARDS", "Caja carné")), HttpStatusCode.Created);
        var state = await ReadSuccessAsync<CashStateDto>(await client.GetAsync("/api/cash/state"), HttpStatusCode.OK);
        await ReadSuccessAsync<CashStateDto>(await client.PutAsJsonAsync("/api/cash/shift-mode",
            new UpdateCashShiftModeRequest(CashConstants.SharedMode, state.ConfigurationVersion!.Value)), HttpStatusCode.OK);
        await ReadSuccessAsync<CashShiftDto>(await client.PostAsJsonAsync("/api/cash/shifts",
            new OpenCashShiftRequest(0m, "Carné")), HttpStatusCode.Created);

        var issued = await ReadSuccessAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{users.Cashier.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        var readResponse = await client.GetAsync($"/api/cash/operator-credentials/{users.Cashier.Id}");
        Assert.True(readResponse.Headers.CacheControl?.NoStore);
        var card = await ReadSuccessAsync<OperatorCredentialDto>(readResponse, HttpStatusCode.OK);
        Assert.Equal(issued.CredentialToken, card.CredentialToken);
        Assert.Equal(1, card.Version);
        await AssertCredentialIsHashedAndNotAuditedAsync(issued);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var stored = await db.UsuarioToken.SingleAsync(x => x.UsuarioId == users.Cashier.Id &&
                x.ProveedorLogin == ProtectedOperatorCredentialStore.Provider);
            Assert.StartsWith("v1:", stored.ValorToken);
            Assert.DoesNotContain(issued.CredentialToken, stored.ValorToken!);
        }

        var cashier = await LoginAsync(client, CashierUserName, CashierPassword);
        SetBearer(client, cashier.AccessToken);
        var session = await ReadSuccessAsync<OperatorSessionDto>(await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(issued.CredentialToken)), HttpStatusCode.Created);
        SetBearer(client, administrator.AccessToken);
        var deleteUrl = $"/api/cash/operator-credentials/{users.Cashier.Id}?credentialId={card.CredentialId}&expectedVersion={card.Version}";
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(deleteUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(deleteUrl)).StatusCode);
        SetBearer(client, cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            "/api/cash/operator-sessions", new StartOperatorSessionRequest(issued.CredentialToken))).StatusCode);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            var saved = await db.CredencialUsuario.SingleAsync(x => x.Id == issued.CredentialId);
            Assert.Equal("REVOCADA", saved.Estado);
            Assert.NotNull(saved.FechaRevocacion);
            Assert.Equal(CashConstants.ClosedSessionState, (await db.SesionOperador.SingleAsync(x => x.Id == session.Id)).Estado);
            Assert.False(await db.UsuarioToken.AnyAsync(x => x.UsuarioId == users.Cashier.Id &&
                x.ProveedorLogin == ProtectedOperatorCredentialStore.Provider));
            Assert.Equal(1, await db.EventoAuditoria.CountAsync(x => x.Accion == "OPERATORCREDENTIALREVOKED"));
        }
    }

    [Fact, Trait("Category", "Integration")]
    public async Task OperatorCredential_LegacyMetadataAndConcurrentReplacementAreSafe_AndDelegatedSupervisorIsDenied()
    {
        using var client = CreateClient();
        await SetupAsync(client);
        var administrator = await LoginAsync(client, AdministratorUserName, AdministratorPassword);
        SetBearer(client, administrator.AccessToken);
        var users = await CreateOperationalUsersAsync(client);
        var first = await ReadSuccessAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{users.Cashier.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
            await db.UsuarioToken.Where(x => x.UsuarioId == users.Cashier.Id &&
                x.ProveedorLogin == ProtectedOperatorCredentialStore.Provider).ExecuteDeleteAsync();
        }
        var legacy = await ReadSuccessAsync<OperatorCredentialDto>(
            await client.GetAsync($"/api/cash/operator-credentials/{users.Cashier.Id}"), HttpStatusCode.OK);
        Assert.Null(legacy.CredentialToken);
        Assert.Equal(first.VisibleFragment, legacy.VisibleFragment);
        var second = await ReadSuccessAsync<IssuedOperatorCredentialDto>(await client.PostAsJsonAsync(
            $"/api/cash/operator-credentials/{users.Cashier.Id}", new IssueOperatorCredentialRequest(null)), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(
            $"/api/cash/operator-credentials/{users.Cashier.Id}?credentialId={first.CredentialId}&expectedVersion=1")).StatusCode);
        Assert.Equal(second.CredentialId, (await ReadSuccessAsync<OperatorCredentialDto>(
            await client.GetAsync($"/api/cash/operator-credentials/{users.Cashier.Id}"), HttpStatusCode.OK)).CredentialId);
        var permissionId = await GetPermissionIdAsync(client, PermissionCodes.UsersManage);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/users/{users.Supervisor.Id}/permissions/{permissionId}", new SetUserPermissionRequest(true))).StatusCode);
        var supervisor = await LoginAsync(client, SupervisorUserName, SupervisorPassword);
        SetBearer(client, supervisor.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/cash/operator-credentials/{users.Cashier.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(
            $"/api/cash/operator-credentials/{users.Cashier.Id}?credentialId={second.CredentialId}&expectedVersion=1")).StatusCode);
    }
}
