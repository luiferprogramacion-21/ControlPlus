namespace ControlPlus.Domain.OfficialModel;

public partial class Venta
{
    /// <summary>Original confirmed sale when this is an additional charge for an omitted product.</summary>
    public Guid? VentaOrigenId { get; set; }
}
