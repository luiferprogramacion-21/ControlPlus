using System.Linq.Expressions;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

/// <summary>
/// Criterio único de «stock bajo» (decisión del 15 de noviembre de 2026, ajustada por las pruebas del usuario del 26 de
/// noviembre): producto activo cuyo stock disponible (existencias menos reservado) es mayor que cero e igual o menor al
/// mínimo. Los agotados (disponible igual o menor a cero) van aparte. Lo comparten el Dashboard, Catálogo e Inventario
/// para que todos cuenten exactamente los mismos productos.
/// </summary>
public static class LowStockSelection
{
    public static Expression<Func<Producto, bool>> Predicate { get; } =
        x => x.Activo && x.StockActual - x.StockReservado > 0 && x.StockActual - x.StockReservado <= x.StockMinimo;

    /// <summary>Producto activo agotado: disponible igual o menor a cero.</summary>
    public static Expression<Func<Producto, bool>> OutOfStock { get; } =
        x => x.Activo && x.StockActual - x.StockReservado <= 0;
}
