namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// Immutable reconciliation detail for one payment method at cash-shift closing.
/// </summary>
public partial class DetalleArqueoMedioPago
{
    public Guid Id { get; set; }

    public Guid TurnoCajaId { get; set; }

    public Guid MetodoPagoId { get; set; }

    public decimal ValorEsperado { get; set; }

    public decimal ValorContado { get; set; }

    public decimal Diferencia { get; set; }

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual MetodoPago MetodoPago { get; set; } = null!;
}
