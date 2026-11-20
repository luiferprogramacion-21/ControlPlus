namespace ControlPlus.Domain.OfficialModel;

/// <summary>Append-only evidence that cancelling a receipt restored its order's pending units.</summary>
public sealed class CorreccionRecepcion
{
    public Guid Id { get; set; }
    public Guid CompraId { get; set; }
    public Guid PedidoCompraId { get; set; }
    public Guid UsuarioId { get; set; }
    public Guid AutorizacionOperacionId { get; set; }
    public long VersionPedidoAnterior { get; set; }
    public long VersionPedidoPosterior { get; set; }
    public string EstadoAnterior { get; set; } = null!;
    public string EstadoPosterior { get; set; } = null!;
    public DateTime FechaHora { get; set; }
    public Guid CorrelacionId { get; set; }
}
