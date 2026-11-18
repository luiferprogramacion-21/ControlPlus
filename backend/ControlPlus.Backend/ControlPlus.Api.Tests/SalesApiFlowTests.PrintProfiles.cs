using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Printing;
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
    public async Task PrintProfile_WithoutHistory_IsDeletedWithItsTemplate_AndAudited()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var profile = await ReadAsync<PrintProfileDto>(await client.PostAsJsonAsync(
            "/api/printing/profiles", new CreatePrintProfileRequest("Perfil sin uso", "COLA_SIMULADA", 32)),
            HttpStatusCode.Created);

        // Solo el Administrador con CONFIGURATION.MANAGE borra perfiles.
        SetBearer(client, scenario.Cashier.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/printing/profiles/{profile.Id}")).StatusCode);
        SetBearer(client, scenario.Admin.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/printing/profiles/{Guid.CreateVersion7()}")).StatusCode);

        var result = await ReadAsync<DeletePrintProfileResultDto>(
            await client.DeleteAsync($"/api/printing/profiles/{profile.Id}"), HttpStatusCode.OK);
        Assert.True(result.PhysicallyDeleted);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/printing/profiles/{profile.Id}")).StatusCode);
        Assert.Empty(await ReadAsync<List<PrintProfileDto>>(await client.GetAsync("/api/printing/profiles"), HttpStatusCode.OK));
        Assert.False((await ReadAsync<PrintStatusDto>(await client.GetAsync("/api/printing/status"), HttpStatusCode.OK)).HasActiveProfile);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        Assert.False(await db.PerfilImpresora.AsNoTracking().AnyAsync(x => x.Id == profile.Id));
        Assert.False(await db.PlantillaImpresion.AsNoTracking().AnyAsync(x => x.Codigo.StartsWith("RECEIPT_" + profile.Id.ToString("N").ToUpper())));
        Assert.Equal(1, await db.EventoAuditoria.AsNoTracking().CountAsync(x => x.Accion == "PRINTPROFILEDELETED" && x.EntidadId == profile.Id));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PrintProfile_WithHistory_IsRetiredKeepingJobs_AndItsNameCanBeReused()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        using var client = CreateClient();
        var scenario = await BootstrapAsync(client);
        SetBearer(client, scenario.Admin.AccessToken);
        var profile = await ReadAsync<PrintProfileDto>(await client.PostAsJsonAsync(
            "/api/printing/profiles", new CreatePrintProfileRequest("Recibos caja", "COLA_SIMULADA", 32)),
            HttpStatusCode.Created);
        await OpenSharedShiftAsync(client, scenario.Admin.AccessToken);
        _ = await StartSessionAsync(client, scenario.Admin.AccessToken, scenario.Admin.AccessToken, scenario.Admin.User.Id);
        var sale = await ReadAsync<SaleDto>(await PostSaleAsync(client, "profile-history-sale",
            CashRequest(scenario.Products.Standard, 1, 1_000m)), HttpStatusCode.Created);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        for (var attempt = 0; attempt < 100 && !await db.TrabajoImpresion.AsNoTracking()
                 .AnyAsync(x => x.DocumentoEmitido.EntidadId == sale.Id && x.Estado == "IMPRESO"); attempt++)
            await Task.Delay(50);
        Assert.True(await db.TrabajoImpresion.AsNoTracking().AnyAsync(x => x.DocumentoEmitido.EntidadId == sale.Id && x.Estado == "IMPRESO"));

        var result = await ReadAsync<DeletePrintProfileResultDto>(
            await client.DeleteAsync($"/api/printing/profiles/{profile.Id}"), HttpStatusCode.OK);
        Assert.False(result.PhysicallyDeleted);
        Assert.Empty(await ReadAsync<List<PrintProfileDto>>(await client.GetAsync("/api/printing/profiles"), HttpStatusCode.OK));
        Assert.False((await ReadAsync<PrintStatusDto>(await client.GetAsync("/api/printing/status"), HttpStatusCode.OK)).HasActiveProfile);

        var retired = await db.PerfilImpresora.AsNoTracking().SingleAsync(x => x.Id == profile.Id);
        Assert.False(retired.Activo);
        Assert.False(retired.EsPredeterminada);
        Assert.Contains("(eliminado", retired.Nombre);
        Assert.NotEqual("Recibos caja", retired.Nombre);
        Assert.Equal(1, await db.TrabajoImpresion.AsNoTracking().CountAsync(x => x.PerfilImpresoraId == profile.Id));
        Assert.Equal(1, await db.EventoAuditoria.AsNoTracking().CountAsync(x => x.Accion == "PRINTPROFILEDELETED" && x.EntidadId == profile.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/printing/profiles/{profile.Id}")).StatusCode);

        // El nombre queda libre para crear el perfil de nuevo.
        var again = await ReadAsync<PrintProfileDto>(await client.PostAsJsonAsync(
            "/api/printing/profiles", new CreatePrintProfileRequest("Recibos caja", "COLA_SIMULADA", 32)),
            HttpStatusCode.Created);
        Assert.NotEqual(profile.Id, again.Id);
        Assert.Single(await ReadAsync<List<PrintProfileDto>>(await client.GetAsync("/api/printing/profiles"), HttpStatusCode.OK));
    }
}
