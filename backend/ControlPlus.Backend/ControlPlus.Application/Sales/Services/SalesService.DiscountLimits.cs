using ControlPlus.Domain.Sales;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Sales.Services;

public sealed partial class SalesService
{
    /// <summary>
    /// Decisión D7 del 23 de noviembre de 2026: el descuento máximo de cada rol se lee de la base
    /// (seguridad.limite_operacion_rol), donde el Administrador configura los del Cajero y el Supervisor; el del
    /// Administrador es 100 % y lo limita el costo. Sin fila configurada se usa el valor inicial del rol.
    /// </summary>
    private async Task<decimal> DiscountLimitAsync(RoleLevel role, CancellationToken cancellationToken)
    {
        var code = RoleCode(role);
        var limits = await repository.GetDiscountLimitsAsync(false, cancellationToken);
        var configured = limits.Where(x => string.Equals(x.Rol?.Codigo, code, StringComparison.OrdinalIgnoreCase))
            .Select(x => (decimal?)x.ValorMaximo).FirstOrDefault();
        return Math.Clamp(configured ?? SalePricingPolicy.MaximumDiscountPercentage(role), 0m, 100m);
    }

    internal static string RoleCode(RoleLevel role) => role switch
    {
        RoleLevel.Administrador => "ADMINISTRADOR",
        RoleLevel.Supervisor => "SUPERVISOR",
        _ => "CAJERO"
    };
}