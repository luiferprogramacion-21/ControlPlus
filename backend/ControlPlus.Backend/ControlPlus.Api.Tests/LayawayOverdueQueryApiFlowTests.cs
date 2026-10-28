using System.Net;
using System.Net.Http.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Layaways.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed partial class LayawayCreateApiFlowTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task OverdueQueries_Http_UseLocalDateFilterBeforePaginationIsolateInstallationAndRevalidatePermission()
    {
        Assert.StartsWith("controlplus_test_",
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Database);
        using var setupClient = CreateClient();
        var scenario = await BootstrapAsync(setupClient, sharedShift: false);
        // UTC is already October 28; the installation is still on October 27.
        var clock = new OverdueQueryClock();
        var (local, foreign) = await SeedOverdueQueriesAsync(scenario, clock.UtcNow);
        using var fixedFactory = _factory!.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
            }));
        using var client = fixedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
        });
        // Tokens come from the original host, so JWT lifetime validation uses real time.
        SetBearer(client, scenario.Cashier.AccessToken);
        var before = await CaptureAsync(scenario);
        var headersBefore = await OverdueQueryHeadersAsync();

        var all = await PageAsync(null, 1, 100);
        Assert.Equal(local.Length, all.TotalCount);
        Assert.Equal(local.Select(x => x.Id).Order(), all.Items.Select(x => x.Id).Order());
        foreach (var expected in local)
        {
            var row = all.Items.Single(x => x.Id == expected.Id);
            Assert.Equal(expected.IsOverdue, row.IsOverdue);
            Assert.Equal(expected.State, row.State);
            Assert.Equal(expected.DueOn, row.DueOn);
            var detail = await ReadAsync<LayawayDetailDto>(await client.GetAsync(
                $"/api/layaways/{expected.Id}"), HttpStatusCode.OK);
            Assert.Equal(row, detail.Layaway);
        }
        foreach (var row in foreign)
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/layaways/{row.Id}")).StatusCode);

        await AssertFilteredPagesAsync(true, local.Where(x => x.IsOverdue).ToArray(), 1);
        await AssertFilteredPagesAsync(false, local.Where(x => !x.IsOverdue).ToArray(), 2);
        var paid = await ReadAsync<PagedResult<LayawayQueryDto>>(await client.GetAsync(
            "/api/layaways?overdue=false&state=PAGADO&pageSize=1"), HttpStatusCode.OK);
        Assert.Equal(1, paid.TotalCount);
        Assert.Equal(local.Single(x => x.State == "PAGADO").Id, Assert.Single(paid.Items).Id);
        var incompatible = await ReadAsync<PagedResult<LayawayQueryDto>>(await client.GetAsync(
            "/api/layaways?overdue=true&state=PAGADO"), HttpStatusCode.OK);
        Assert.Empty(incompatible.Items);
        Assert.Equal(0, incompatible.TotalCount);

        // The day boundary changes the projection and filtered count without a write.
        clock.UtcNow = new DateTimeOffset(2026, 10, 28, 5, 0, 0, TimeSpan.Zero);
        var dueToday = local.Single(x => x.State == "ACTIVO" && x.DueOn == new DateOnly(2026, 10, 27));
        var afterMidnight = await ReadAsync<LayawayDetailDto>(await client.GetAsync(
            $"/api/layaways/{dueToday.Id}"), HttpStatusCode.OK);
        Assert.True(afterMidnight.Layaway.IsOverdue);
        var expiredAfterMidnight = await PageAsync(true, 1, 100);
        Assert.Equal(3, expiredAfterMidnight.TotalCount);
        Assert.Equal(local.Where(x => x.IsOverdue || x.Id == dueToday.Id).Select(x => x.Id).Order(),
            expiredAfterMidnight.Items.Select(x => x.Id).Order());
        Assert.Equal(before, await CaptureAsync(scenario));
        Assert.Equal(headersBefore, await OverdueQueryHeadersAsync());

        // An invalidated token and a fresh token without the effective permission are both rejected.
        SetBearer(setupClient, scenario.Admin.AccessToken);
        var permissionId = await GetPermissionIdAsync(setupClient, PermissionCodes.LayawayManage);
        Assert.Equal(HttpStatusCode.NoContent, (await setupClient.PutAsJsonAsync(
            $"/api/users/{scenario.Cashier.User.Id}/permissions/{permissionId}",
            new SetUserPermissionRequest(false))).StatusCode);
        foreach (var path in new[] { "/api/layaways?overdue=true", $"/api/layaways/{local[0].Id}" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        var revoked = await LoginAsync(setupClient, CashierName, CashierPassword);
        SetBearer(client, revoked.AccessToken);
        foreach (var path in new[] { "/api/layaways?overdue=false", $"/api/layaways/{local[0].Id}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(headersBefore, await OverdueQueryHeadersAsync());

        async Task<PagedResult<LayawayQueryDto>> PageAsync(bool? overdue, int page, int size) =>
            await ReadAsync<PagedResult<LayawayQueryDto>>(await client.GetAsync(
                $"/api/layaways?page={page}&pageSize={size}" +
                (overdue is bool value ? $"&overdue={value.ToString().ToLowerInvariant()}" : "")),
                HttpStatusCode.OK);

        async Task AssertFilteredPagesAsync(bool overdue, OverdueQueryRow[] expected, int size)
        {
            var seen = new List<Guid>();
            var pages = (expected.Length + size - 1) / size;
            for (var page = 1; page <= pages + 1; page++)
            {
                var result = await PageAsync(overdue, page, size);
                Assert.Equal(expected.Length, result.TotalCount);
                Assert.Equal(pages, result.TotalPages);
                Assert.Equal(page, result.Page);
                Assert.Equal(size, result.PageSize);
                Assert.Equal(Math.Min(size, Math.Max(0, expected.Length - (page - 1) * size)), result.Items.Count);
                Assert.All(result.Items, row => Assert.Equal(overdue, row.IsOverdue));
                seen.AddRange(result.Items.Select(x => x.Id));
            }
            Assert.Equal(expected.Select(x => x.Id).Order(), seen.Order());
        }
    }

    private async Task<(OverdueQueryRow[] Local, OverdueQueryRow[] Foreign)> SeedOverdueQueriesAsync(
        Scenario scenario, DateTimeOffset now)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var product = await db.Producto.Include(x => x.UnidadMedida)
            .SingleAsync(x => x.Id == scenario.ProductId);
        var cashMethodId = await db.MetodoPago.Where(x => x.Codigo == "EFECTIVO")
            .Select(x => x.Id).SingleAsync();
        var installation = await db.Instalacion.SingleAsync();
        var establishment = await db.Establecimiento.SingleAsync();
        establishment.ZonaHoraria = "America/Bogota";
        // A historical installation must never contribute rows or totals to current queries.
        var other = (Instalacion)db.Entry(installation).CurrentValues.ToObject();
        other.Id = Guid.CreateVersion7();
        other.Codigo = "OVERDUE-OTHER-INSTALLATION";
        other.Serie = "OTHER";
        other.Activo = false;
        db.Instalacion.Add(other);
        var otherCash = (Caja)db.Entry(await db.Caja.SingleAsync()).CurrentValues.ToObject();
        otherCash.Id = Guid.CreateVersion7();
        otherCash.InstalacionId = other.Id;
        db.Caja.Add(otherCash);
        var otherTerminal = (Terminal)db.Entry(await db.Terminal.SingleAsync()).CurrentValues.ToObject();
        otherTerminal.Id = Guid.CreateVersion7();
        otherTerminal.InstalacionId = other.Id;
        otherTerminal.Codigo = "OVERDUE-OTHER-TERMINAL";
        db.Terminal.Add(otherTerminal);
        var otherShift = (TurnoCaja)db.Entry(await db.TurnoCaja.SingleAsync()).CurrentValues.ToObject();
        otherShift.Id = Guid.CreateVersion7();
        otherShift.CajaId = otherCash.Id;
        otherShift.TerminalId = otherTerminal.Id;
        db.TurnoCaja.Add(otherShift);
        var permissionId = await db.Permiso.Where(x => x.Codigo == PermissionCodes.LayawayManage)
            .Select(x => x.Id).SingleAsync();
        var date = new DateOnly(2026, 10, 27);
        var cases = new (string State, int Days, bool Overdue)[]
        {
            ("ACTIVO", -1, true), ("ACTIVO", -1, true),
            ("ACTIVO", 0, false), ("ACTIVO", 1, false),
            ("PAGADO", -1, false), ("ENTREGADO", -1, false), ("ANULADO", -1, false)
        };
        var local = Seed(installation.Id, scenario.Shift.Id, 0);
        var foreign = Seed(other.Id, otherShift.Id, 20);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return (local, foreign);

        OverdueQueryRow[] Seed(Guid installationId, Guid shiftId, int minutes)
        {
            return cases.Select((item, index) =>
            {
                var at = now.UtcDateTime.AddDays(-5).AddMinutes(minutes + index);
                var authorization = Authorization(at);
                var row = new Apartado
                {
                    Id = Guid.CreateVersion7(), InstalacionId = installationId, TurnoCajaId = shiftId,
                    ClienteId = scenario.CustomerId, UsuarioId = scenario.Admin.User.Id,
                    AutorizacionOperacion = authorization, Prefijo = "APT", Serie = "OVERDUE",
                    Consecutivo = index + 1, FechaHora = at, FechaLimite = date.AddDays(item.Days),
                    AmbitoDescuento = "NINGUNO", PorcentajeMinimoInicial = 20m,
                    AbonoInicial = 201m, SubtotalBruto = 1001m, Total = 1001m,
                    SaldoPendiente = item.State is "PAGADO" or "ENTREGADO" ? 0m : 800m,
                    Estado = item.State, Version = 1
                };
                if (item.State is "PAGADO" or "ENTREGADO") row.FechaPago = at;
                if (item.State == "ENTREGADO")
                {
                    row.FechaEntrega = at;
                    row.UsuarioEntregaId = scenario.Admin.User.Id;
                }
                if (item.State == "ANULADO")
                {
                    row.FechaAnulacion = at;
                    row.UsuarioAnulacionId = scenario.Admin.User.Id;
                    row.AutorizacionAnulacion = Authorization(at);
                    row.TotalAbonadoCancelacion = 201m;
                    row.ValorDevueltoCancelacion = 0m;
                    row.ValorRetenidoCancelacion = 201m;
                }
                row.DetalleApartado.Add(new DetalleApartado
                {
                    Id = Guid.CreateVersion7(), ProductoId = product.Id,
                    CodigoProducto = product.CodigoInterno, NombreProducto = product.Nombre,
                    UnidadMedida = product.UnidadMedida.Nombre, Cantidad = 1,
                    PrecioReserva = row.Total, CostoUnitario = product.CostoPromedio,
                    SubtotalBruto = row.Total, SubtotalNeto = row.Total,
                    OrigenDescuento = "NINGUNO"
                });
                // Deferred totals also require applied payments, including the retained payment after cancellation.
                var paid = row.Total - row.SaldoPendiente;
                row.PagoApartado.Add(new PagoApartado
                {
                    Id = Guid.CreateVersion7(), InstalacionId = installationId, TurnoCajaId = shiftId,
                    MetodoPagoId = cashMethodId, UsuarioId = scenario.Admin.User.Id,
                    Prefijo = "PAP", Serie = row.Serie, Consecutivo = row.Consecutivo,
                    ValorAplicado = paid, ValorRecibido = paid, Cambio = 0m,
                    FechaHora = at, Estado = "APLICADO"
                });
                db.Apartado.Add(row);
                return new OverdueQueryRow(row.Id, row.Estado, row.FechaLimite, item.Overdue);
            }).ToArray();
        }

        AutorizacionOperacion Authorization(DateTime at) => new()
        {
            Id = Guid.CreateVersion7(), UsuarioSolicitanteId = scenario.Admin.User.Id,
            UsuarioAutorizadorId = scenario.Supervisor.User.Id, PermisoId = permissionId,
            TipoOperacion = "APARTADO", CorrelacionId = Guid.CreateVersion7(),
            DescripcionOperacion = "Fixture de consulta de vencimiento",
            MetodoAutenticacion = "PASSWORD", Resultado = "EXITOSA", Estado = "UTILIZADA",
            FechaSolicitud = at, FechaExpiracion = at.AddMinutes(5), FechaUtilizacion = at
        };
    }

    private async Task<string> OverdueQueryHeadersAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfficialControlPlusDbContext>();
        var rows = await db.Apartado.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        return System.Text.Json.JsonSerializer.Serialize(rows.Select(x => new
        {
            x.Id, x.Estado, x.Version, x.FechaLimite, x.SaldoPendiente,
            x.FechaPago, x.FechaEntrega, x.FechaAnulacion
        }));
    }

    private sealed record OverdueQueryRow(Guid Id, string State, DateOnly DueOn, bool IsOverdue);

    private sealed class OverdueQueryClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 28, 4, 59, 59, TimeSpan.Zero);
    }
}
