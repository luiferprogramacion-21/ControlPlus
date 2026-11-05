using ControlPlus.Domain.Security;
using ControlPlus.Domain.Security.Enums;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class RoleHierarchyTests
{
    [Theory]
    [InlineData(RoleLevel.Supervisor, RoleLevel.Cajero, true)]
    [InlineData(RoleLevel.Administrador, RoleLevel.Cajero, true)]
    [InlineData(RoleLevel.Cajero, RoleLevel.Cajero, false)]
    [InlineData(RoleLevel.Administrador, RoleLevel.Supervisor, true)]
    [InlineData(RoleLevel.Supervisor, RoleLevel.Supervisor, false)]
    [InlineData(RoleLevel.Administrador, RoleLevel.Administrador, false)]
    public void SecondAuthorizerMustHaveStrictlyHigherBaseRole(RoleLevel authorizer, RoleLevel executor, bool expected)
    {
        Assert.Equal(expected, RoleHierarchy.IsHigherThan(authorizer, executor));
    }
}
