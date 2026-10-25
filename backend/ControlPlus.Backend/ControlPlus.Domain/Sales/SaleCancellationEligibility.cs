using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Domain.Sales;

/// <summary>Reglas aprobadas que pueden comprobarse antes de decidir los efectos de una anulación.</summary>
public static class SaleCancellationEligibility
{
    public static bool HasAuthorizedRole(RoleLevel role) =>
        role is RoleLevel.Supervisor or RoleLevel.Administrador;

    public static bool IsCurrentConfirmedSale(string state, long currentVersion, long expectedVersion) =>
        state == "CONFIRMADA" && expectedVersion > 0 && currentVersion == expectedVersion;
}
