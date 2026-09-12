using ControlPlus.Domain.Common;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Security;

/// <summary>
/// Centralizes the ordered Cajero, Supervisor and Administrador hierarchy.
/// </summary>
public static class RoleHierarchy
{
    public static bool HasAtLeast(RoleLevel actualLevel, RoleLevel requiredLevel)
    {
        EnsureValid(actualLevel, nameof(actualLevel));

        if (requiredLevel != RoleLevel.None)
        {
            EnsureValid(requiredLevel, nameof(requiredLevel));
        }

        return actualLevel >= requiredLevel;
    }

    public static bool IsHigherThan(RoleLevel actualLevel, RoleLevel targetLevel)
    {
        EnsureValid(actualLevel, nameof(actualLevel));

        if (targetLevel != RoleLevel.None)
        {
            EnsureValid(targetLevel, nameof(targetLevel));
        }

        return actualLevel > targetLevel;
    }

    public static bool IsDefined(RoleLevel level) =>
        level is RoleLevel.Cajero or RoleLevel.Supervisor or RoleLevel.Administrador;

    private static void EnsureValid(RoleLevel level, string parameterName)
    {
        if (!IsDefined(level))
        {
            throw new DomainRuleViolationException("A valid role authority level is required.");
        }
    }
}
