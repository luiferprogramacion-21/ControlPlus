using ControlPlus.Domain.Sales;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class SaleCancellationEligibilityTests
{
    [Theory]
    [InlineData(RoleLevel.Cajero, false)]
    [InlineData(RoleLevel.Supervisor, true)]
    [InlineData(RoleLevel.Administrador, true)]
    public void Cancellation_RequiresSupervisorOrAdministrator(RoleLevel role, bool expected) =>
        Assert.Equal(expected, SaleCancellationEligibility.HasAuthorizedRole(role));

    [Theory]
    [InlineData("CONFIRMADA", 4, 4, true)]
    [InlineData("CONFIRMADA", 4, 3, false)]
    [InlineData("ANULADA", 4, 4, false)]
    [InlineData("CONFIRMADA", 4, 0, false)]
    public void Cancellation_RequiresCurrentConfirmedVersion(string state, long version,
        long expectedVersion, bool expected) =>
        Assert.Equal(expected, SaleCancellationEligibility.IsCurrentConfirmedSale(state, version, expectedVersion));
}
