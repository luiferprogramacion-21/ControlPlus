using System.Linq.Expressions;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

/// <summary>
/// Criterio único de «stock bajo» (decisión del 15 de noviembre de 2026): producto activo cuyo stock disponible
/// (existencias menos reservado) es igual o menor al mínimo; incluye los agotados. Lo comparten el Dashboard y el
/// filtro de Inventario para que ambos cuenten exactamente los mismos productos.
/// </summary>
public static class LowStockSelection
{
    public static Expression<Func<Producto, bool>> Predicate { get; } =
        x => x.Activo && x.StockActual - x.StockReservado <= x.StockMinimo;
}
