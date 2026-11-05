namespace ControlPlus.Domain.OfficialModel;

/// <summary>Permanent lookup key: current references, barcodes and historical aliases share one namespace.</summary>
public sealed class ProductoCodigo
{
    public string Codigo { get; set; } = null!;
    public Guid ProductoId { get; set; }
}
