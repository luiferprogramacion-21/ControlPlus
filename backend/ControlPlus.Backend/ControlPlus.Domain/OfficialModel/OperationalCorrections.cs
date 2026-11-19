namespace ControlPlus.Domain.OfficialModel;

/// <summary>An append-only link; the stock movements themselves keep their original meaning.</summary>
public sealed class CorreccionMovimientoInventario
{
    public Guid Id { get; set; }
    public Guid MovimientoOriginalId { get; set; }
    public Guid MovimientoCorrectorId { get; set; }
    public DateTime FechaHora { get; set; }
}

/// <summary>A reviewed count never rewrites a closed shift or moves money to another shift.</summary>
public sealed class RevisionCierreCaja
{
    public Guid Id { get; set; }
    public Guid TurnoCajaId { get; set; }
    public Guid UsuarioId { get; set; }
    public decimal EfectivoEsperado { get; set; }
    public decimal ContadoOriginal { get; set; }
    public decimal ContadoRevisado { get; set; }
    public string Motivo { get; set; } = null!;
    public DateTime FechaHora { get; set; }
    public Guid CorrelacionId { get; set; }
}
