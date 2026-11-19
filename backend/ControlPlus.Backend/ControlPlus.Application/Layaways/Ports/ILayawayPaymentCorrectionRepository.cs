using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Layaways.Ports;

public interface ILayawayPaymentCorrectionRepository
{
    Task<PagoApartado?> PaymentForUpdateAsync(Guid layawayId, Guid paymentId, CancellationToken ct);
    Task<IReadOnlyCollection<PagoApartado>> MovementPaymentsForUpdateAsync(Guid movementId, CancellationToken ct);
    Task<MovimientoCaja?> MovementForUpdateAsync(Guid movementId, CancellationToken ct);
    Task<bool> HasReversalAsync(Guid movementId, CancellationToken ct);
    Task<IReadOnlyCollection<MetodoPago>> MethodsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyCollection<DetalleApartado>> LinesForUpdateAsync(Guid layawayId, CancellationToken ct);
    Task AppendLinesAsync(IReadOnlyCollection<DetalleApartado> newLines, CancellationToken ct);
    Task AddCorrectionAsync(CorreccionApartado correction, IReadOnlyCollection<ReembolsoCorreccionApartado> refunds, CancellationToken ct);
    Task<IReadOnlyCollection<CorreccionApartado>> HistoryAsync(Guid layawayId, CancellationToken ct);
}
