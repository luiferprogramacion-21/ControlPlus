using ControlPlus.Application.Reports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class DashboardSummaryTests
{
    [Fact]
    public async Task Summary_UsesEstablishmentDateAndKeepsSalesSeparateFromPayments()
    {
        var installation = Guid.NewGuid();
        var source = new Source(installation);
        var service = new DashboardSummaryService(new Permission(true),
            new Clock(new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero)), source, source, source);
        var result = await service.GetAsync(new ActorContext(Guid.NewGuid(), RoleLevel.Administrador), default);
        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 10, 24), result.Value!.OperatingDate);
        Assert.Equal(new SalesAmount(1, 100m), result.Value.CashSales);
        Assert.Equal(new SalesAmount(1, 80m), result.Value.CreditSales);
        Assert.Equal(new SalesAmount(2, 180m), result.Value.TotalSales);
        Assert.Equal(20m, result.Value.OpeningBalance);
        Assert.Equal(60m, result.Value.PaymentTotal);
        Assert.Equal(-5m, result.Value.ManualTotal);
        Assert.Equal(55m, result.Value.NetCashFlow);
        Assert.Equal(installation, source.LastInstallation);
    }

    [Fact]
    public async Task Summary_DeniesRevokedPermissionBeforeReadingInstallation()
    {
        var source = new Source(Guid.NewGuid());
        var service = new DashboardSummaryService(new Permission(false),
            new Clock(DateTimeOffset.UtcNow), source, source, source);
        var result = await service.GetAsync(new ActorContext(Guid.NewGuid(), RoleLevel.Administrador), default);
        Assert.Equal("authorization.forbidden", result.Error?.Code);
        Assert.False(source.Read);
    }

    private sealed class Permission(bool allowed) : IPermissionChecker
    {
        public Task<bool> HasPermissionAsync(Guid userId, string code, CancellationToken cancellationToken = default)
        {
            Assert.Equal(PermissionCodes.FinancialReportsRead, code);
            return Task.FromResult(allowed);
        }
    }

    private sealed class Clock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }

    private sealed class Source(Guid id) : IDashboardInstallationRepository, IFinancialSalesReportRepository, IDailyCashFlowRepository
    {
        public bool Read { get; private set; }
        public Guid LastInstallation { get; private set; }
        public Task<DashboardInstallation?> GetActiveAsync(CancellationToken cancellationToken)
        {
            Read = true;
            return Task.FromResult<DashboardInstallation?>(new(id, "America/Bogota"));
        }
        public Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SalesReportRow>> GetDailySalesAsync(Guid installationId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            LastInstallation = installationId;
            Assert.Equal(from, to);
            return Task.FromResult<IReadOnlyList<SalesReportRow>>([new(from, "CONTADO", 1, 100m), new(from, "CREDITO", 1, 80m)]);
        }
        public Task<IReadOnlyList<CashFlowEntry>> GetEntriesAsync(Guid installationId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            Assert.Equal(id, installationId);
            Assert.Equal(from, to);
            return Task.FromResult<IReadOnlyList<CashFlowEntry>>([
                new(from, "APERTURA", null, 20m),
                new(from, "VENTA_CONTADO", "EFECTIVO", 60m),
                new(from, "GASTO_MANUAL", null, -5m)]);
        }
    }
}
