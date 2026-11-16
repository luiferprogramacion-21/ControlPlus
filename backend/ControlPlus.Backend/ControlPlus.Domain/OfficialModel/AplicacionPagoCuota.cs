namespace ControlPlus.Domain.OfficialModel;

public sealed class AplicacionPagoCuota
{
    public Guid Id { get; set; }
    public Guid PagoCreditoId { get; set; }
    public Guid CuotaCreditoId { get; set; }
    public decimal ValorAplicado { get; set; }

    public PagoCredito PagoCredito { get; set; } = null!;
    public CuotaCredito CuotaCredito { get; set; } = null!;
}
