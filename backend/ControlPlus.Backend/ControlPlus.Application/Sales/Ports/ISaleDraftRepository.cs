using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Sales.Ports;

/// <summary>Instalación, terminal y turno abierto en los que se guarda un borrador.</summary>
public sealed record SaleDraftScope(Guid InstallationId, Guid TerminalId, Guid ShiftId);

public interface ISaleDraftRepository
{
    /// <summary>Null si no hay instalación activa o turno de Caja abierto.</summary>
    Task<SaleDraftScope?> GetScopeAsync(CancellationToken cancellationToken = default);

    /// <summary>Borrador ACTIVO o RECUPERADO del usuario en el turno, con sus líneas ordenadas.</summary>
    Task<BorradorVenta?> GetActiveAsync(Guid userId, Guid shiftId, bool track, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Producto>> GetProductsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);

    Task<bool> CustomerExistsAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// En una transacción: descarta los borradores activos del usuario en otros turnos, reemplaza las líneas del borrador
    /// y guarda la cabecera (nueva o ya rastreada).
    /// </summary>
    Task SaveAsync(BorradorVenta draft, bool isNew, IReadOnlyCollection<DetalleBorradorVenta> lines, DateTime nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Descarta los borradores activos del usuario (todos los turnos).</summary>
    Task DiscardActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dentro de la transacción de la venta: marca CONVERTIDO el borrador activo del usuario en el turno, enlazado a la
    /// venta, y DESCARTADO cualquier otro activo del mismo turno. Solo modifica entidades rastreadas; guarda quien llama.
    /// </summary>
    Task ConvertActiveAsync(Guid userId, Guid shiftId, Guid saleId, DateTime nowUtc, CancellationToken cancellationToken = default);
}
