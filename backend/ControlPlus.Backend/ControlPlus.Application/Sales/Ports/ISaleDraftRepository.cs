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

    /// <summary>Borradores abiertos del usuario en el turno, del más reciente al más antiguo, con sus líneas y el cliente.</summary>
    Task<IReadOnlyCollection<BorradorVenta>> GetOpenAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken = default);

    /// <summary>Un borrador abierto del usuario en el turno, con sus líneas; null si no existe, es de otro o ya se cerró.</summary>
    Task<BorradorVenta?> GetOpenByIdAsync(Guid draftId, Guid userId, Guid shiftId, bool track,
        CancellationToken cancellationToken = default);

    Task<int> CountOpenAsync(Guid userId, Guid shiftId, CancellationToken cancellationToken = default);

    /// <summary>Descarta un borrador abierto del usuario. False si no había nada que descartar.</summary>
    Task<bool> DiscardAsync(Guid draftId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dentro de la transacción de la venta cierra como CONVERTIDO, enlazado a la venta, el borrador de esa venta: el
    /// indicado o, si no se indica, el más reciente del usuario en el turno solo cuando tiene los mismos productos y
    /// cantidades. Las ventas en espera quedan abiertas. Solo modifica entidades rastreadas; guarda quien llama.
    /// </summary>
    Task ConvertAsync(Guid userId, Guid shiftId, Guid saleId, Guid? draftId,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> saleLines, DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
