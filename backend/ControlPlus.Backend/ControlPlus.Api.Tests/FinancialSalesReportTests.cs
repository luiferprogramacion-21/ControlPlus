using ControlPlus.Application.Reports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.Security.Enums;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class FinancialSalesReportTests
{
    [Fact]
    public void Selection_FiltersOperationalDateStatusTypeAndInstallationWithoutPaymentJoins()
    {
        var installation = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 20);
        var sales = new[]
        {
            new Venta { InstalacionId = installation, FechaOperativa = day, Estado = "CONFIRMADA", TipoVenta = "CONTADO", Total = 100m,
                PagoVenta = [new PagoVenta(), new PagoVenta()] },
            new Venta { InstalacionId = installation, FechaOperativa = day, Estado = "CONFIRMADA", TipoVenta = "CREDITO", Total = 80m },
            new Venta { InstalacionId = installation, FechaOperativa = day, Estado = "ANULADA", TipoVenta = "CONTADO", Total = 1000m },
            new Venta { InstalacionId = Guid.NewGuid(), FechaOperativa = day, Estado = "CONFIRMADA", TipoVenta = "CONTADO", Total = 2000m },
            new Venta { InstalacionId = installation, FechaOperativa = day.AddDays(1), Estado = "CONFIRMADA", TipoVenta = "CONTADO", Total = 3000m }
        };
        var selected = sales.AsQueryable().Where(FinancialSalesReportSelection.For(installation, day, day)).ToArray();
        Assert.Equal(2, selected.Length);
        Assert.Equal(180m, selected.Sum(x => x.Total));
        Assert.Single(selected, x => x.TipoVenta == "CONTADO");
        Assert.Single(selected, x => x.TipoVenta == "CREDITO");
    }

    [Fact]
    public async Task Report_UsesOperatingDatesSeparatesSaleTypesAndFillsEmptyDays()
    {
        var repository = new Repository
        {
            Rows =
            [
                new(new DateOnly(2026, 10, 20), "CONTADO", 2, 120.50m),
                new(new DateOnly(2026, 10, 20), "CREDITO", 1, 80m),
                new(new DateOnly(2026, 10, 22), "CONTADO", 1, 10m)
            ]
        };
        var service = new FinancialSalesReportService(new Checker(true), repository);
        var result = await service.GetAsync(Actor(), new(2026, 10, 20), new(2026, 10, 22), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Days.Count);
        Assert.Equal(new SalesAmount(0, 0m), result.Value.Days[1].Total);
        Assert.Equal(new SalesAmount(3, 130.50m), result.Value.Cash);
        Assert.Equal(new SalesAmount(1, 80m), result.Value.Credit);
        Assert.Equal(new SalesAmount(4, 210.50m), result.Value.Total);
        Assert.Equal(repository.InstallationId, repository.RequestedInstallationId);
    }

    [Theory]
    [InlineData("2026-10-23", "2026-10-22")]
    [InlineData("2026-10-01", "2026-11-01")]
    public async Task Report_RejectsInvalidRange(string from, string to)
    {
        var repository = new Repository();
        var result = await new FinancialSalesReportService(new Checker(true), repository)
            .GetAsync(Actor(), DateOnly.Parse(from), DateOnly.Parse(to), default);
        Assert.Equal("validation.failed", result.Error?.Code);
        Assert.Null(repository.RequestedInstallationId);
    }

    [Fact]
    public async Task Report_RejectsRevokedPermissionBeforeReadingSales()
    {
        var repository = new Repository();
        var result = await new FinancialSalesReportService(new Checker(false), repository)
            .GetAsync(Actor(), new(2026, 10, 20), new(2026, 10, 20), default);
        Assert.Equal("authorization.forbidden", result.Error?.Code);
        Assert.Null(repository.RequestedInstallationId);
    }

    private static ActorContext Actor() => new(Guid.NewGuid(), RoleLevel.Administrador);

    private sealed class Checker(bool allowed) : IPermissionChecker
    {
        public Task<bool> HasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default)
        {
            Assert.Equal(PermissionCodes.FinancialReportsRead, permissionCode);
            return Task.FromResult(allowed);
        }
    }

    private sealed class Repository : IFinancialSalesReportRepository
    {
        public Guid InstallationId { get; } = Guid.NewGuid();
        public Guid? RequestedInstallationId { get; private set; }
        public IReadOnlyList<SalesReportRow> Rows { get; init; } = [];
        public Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken) => Task.FromResult<Guid?>(InstallationId);
        public Task<IReadOnlyList<SalesReportRow>> GetDailySalesAsync(Guid installationId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            RequestedInstallationId = installationId;
            return Task.FromResult(Rows);
        }
    }
}
