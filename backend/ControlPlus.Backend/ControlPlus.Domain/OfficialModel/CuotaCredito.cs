namespace ControlPlus.Domain.OfficialModel;

public sealed class CuotaCredito
{
    public Guid Id { get; set; }
    public Guid CreditoId { get; set; }
    public int Numero { get; set; }
    public decimal Monto { get; set; }
    public decimal SaldoPendiente { get; set; }
    public DateOnly FechaVencimiento { get; set; }
    public DateOnly? FechaPago { get; set; }

    public Credito Credito { get; set; } = null!;
    public ICollection<AplicacionPagoCuota> AplicacionPagoCuota { get; set; } = new List<AplicacionPagoCuota>();
}
