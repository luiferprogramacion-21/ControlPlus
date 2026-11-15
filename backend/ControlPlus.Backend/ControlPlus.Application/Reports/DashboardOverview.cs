namespace ControlPlus.Application.Reports;

/// <summary>Reglas fijas del tablero aprobadas el 15 de noviembre de 2026.</summary>
public static class DashboardRules
{
    /// <summary>Un apartado activo es «próximo» si su fecha límite cae entre hoy y este número de días.</summary>
    public const int UpcomingLayawayDays = 3;

    /// <summary>Cantidad de ventas que muestra la actividad reciente.</summary>
    public const int RecentActivityLimit = 10;
}

/// <summary>Ventas de hoy contra el mismo lapso del día anterior; el porcentaje es nulo si ayer no hubo ventas.</summary>
public sealed record DashboardComparison(DateOnly PreviousDate, decimal PreviousTotal, decimal? PercentChange);

/// <summary>Efectivo esperado del turno abierto, igual al que muestra el arqueo.</summary>
public sealed record DashboardCash(DateTimeOffset OpenedAt, decimal ExpectedCash);

/// <summary>Contadores de las alertas operativas.</summary>
public sealed record DashboardAlerts(int LowStockProducts, int OverdueCreditCustomers,
    int UpcomingLayaways, int UpcomingLayawayDays);

/// <summary>Ventas confirmadas de una hora local del establecimiento.</summary>
public sealed record DashboardHour(int Hour, int Transactions, decimal Total);

public sealed record DashboardActivity(Guid SaleId, string Number, DateTimeOffset OccurredAt,
    string? CustomerName, decimal Total, string SaleType, string State);

public sealed record DashboardSaleTime(DateTime OccurredAtUtc, decimal Total);

public sealed record DashboardSaleRow(Guid Id, string Prefix, string Series, long Consecutive,
    DateTime OccurredAtUtc, string? CustomerName, decimal Total, string SaleType, string State);

/// <summary>Consultas de solo lectura del tablero que no existen en Reportes ni en Caja.</summary>
public interface IDashboardOverviewRepository
{
    /// <summary>Efectivo esperado del turno abierto con el mismo cálculo del arqueo; nulo si no hay turno abierto.</summary>
    Task<DashboardCash?> GetCurrentCashAsync(CancellationToken cancellationToken);

    /// <summary>Total de ventas confirmadas de una fecha operativa registradas hasta un instante UTC.</summary>
    Task<decimal> GetSalesTotalUntilAsync(Guid installationId, DateOnly operatingDate, DateTime untilUtc,
        CancellationToken cancellationToken);

    /// <summary>Instante y total de cada venta confirmada de la fecha operativa, con la misma selección que «Ventas de hoy».</summary>
    Task<IReadOnlyList<DashboardSaleTime>> GetSaleTimesAsync(Guid installationId, DateOnly operatingDate,
        CancellationToken cancellationToken);

    /// <summary>Últimas ventas de contado o crédito de la fecha operativa, confirmadas o anuladas, de la más reciente a la más antigua.</summary>
    Task<IReadOnlyList<DashboardSaleRow>> GetRecentSalesAsync(Guid installationId, DateOnly operatingDate, int limit,
        CancellationToken cancellationToken);

    /// <summary>Productos activos agotados o por debajo del mínimo según el criterio de la vista oficial de reposición.</summary>
    Task<int> CountLowStockProductsAsync(CancellationToken cancellationToken);

    /// <summary>Clientes con al menos un crédito con saldo cuyo vencimiento es anterior a la fecha operativa.</summary>
    Task<int> CountOverdueCreditCustomersAsync(Guid installationId, DateOnly operatingDate,
        CancellationToken cancellationToken);

    /// <summary>Apartados activos cuya fecha límite está entre las dos fechas, ambas incluidas.</summary>
    Task<int> CountUpcomingLayawaysAsync(Guid installationId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken);
}
