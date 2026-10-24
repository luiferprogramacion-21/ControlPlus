using ControlPlus.Application.Reports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class DailyCashFlowServiceTests
{
    [Fact]
    public async Task Report_FillsEmptyDaysAndKeepsOpeningSeparateFromPaymentsAndManualFlow()
    {
        var date = new DateOnly(2026, 10, 23);
        var repository = new Repository
        {
            Entries =
            [
                new(date, "APERTURA", null, 100m),
                new(date, "VENTA_CONTADO", "EFECTIVO", 40m),
                new(date, "VENTA_CONTADO", "TRANSFERENCIA", 60m),
                new(date, "GASTO_MANUAL", null, -10m)
            ]
        };
        var result = await new DailyCashFlowService(new Checker(true), repository)
            .GetAsync(new ActorContext(Guid.NewGuid(), RoleLevel.Administrador), date, date.AddDays(1), default);
        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Value!.OpeningBalance);
        Assert.Equal(100m, result.Value.PaymentTotal);
        Assert.Equal(-10m, result.Value.ManualTotal);
        Assert.Equal(2, result.Value.Days.Count);
        Assert.Empty(result.Value.Days[1].Payments);
        Assert.Empty(result.Value.Days[1].ManualMovements);
    }

    [Fact]
    public async Task Report_RejectsRevokedPermissionBeforeRepositoryRead()
    {
        var repository = new Repository();
        var date = new DateOnly(2026, 10, 23);
        var result = await new DailyCashFlowService(new Checker(false), repository)
            .GetAsync(new ActorContext(Guid.NewGuid(), RoleLevel.Administrador), date, date, default);
        Assert.Equal("authorization.forbidden", result.Error?.Code);
        Assert.False(repository.Read);
    }

    private sealed class Checker(bool allowed) : IPermissionChecker
    {
        public Task<bool> HasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default)
        {
            Assert.Equal(PermissionCodes.FinancialReportsRead, permissionCode);
            return Task.FromResult(allowed);
        }
    }

    private sealed class Repository : IDailyCashFlowRepository
    {
        public bool Read { get; private set; }
        public IReadOnlyList<CashFlowEntry> Entries { get; init; } = [];
        public Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken)
        {
            Read = true;
            return Task.FromResult<Guid?>(Guid.NewGuid());
        }
        public Task<IReadOnlyList<CashFlowEntry>> GetEntriesAsync(Guid installationId, DateOnly from, DateOnly to,
            CancellationToken cancellationToken) => Task.FromResult(Entries);
    }
}
