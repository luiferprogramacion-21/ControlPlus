namespace ControlPlus.Domain.OfficialModel;

public sealed class CorreccionApartado
{
    public Guid Id { get; set; }
    public Guid ApartadoId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid AutorizacionId { get; set; }
    public long VersionAnterior { get; set; }
    public long VersionPosterior { get; set; }
    public decimal TotalAnterior { get; set; }
    public decimal TotalPosterior { get; set; }
    public decimal SaldoAnterior { get; set; }
    public decimal SaldoPosterior { get; set; }
    public decimal Reembolso { get; set; }
    public string Motivo { get; set; } = "";
    public string Antes { get; set; } = "[]";
    public string Despues { get; set; } = "[]";
    public DateTime FechaHora { get; set; }
    public Guid CorrelacionId { get; set; }
}

public sealed class ReembolsoCorreccionApartado
{
    public Guid Id { get; set; }
    public Guid CorreccionId { get; set; }
    public Guid TurnoCajaId { get; set; }
    public Guid MetodoPagoId { get; set; }
    public Guid MovimientoCajaId { get; set; }
    public decimal Valor { get; set; }
    public string? Referencia { get; set; }
    public CorreccionApartado Correccion { get; set; } = null!;
    public MovimientoCaja MovimientoCaja { get; set; } = null!;
}
