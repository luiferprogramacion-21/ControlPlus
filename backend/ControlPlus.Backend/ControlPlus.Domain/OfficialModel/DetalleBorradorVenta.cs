using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class DetalleBorradorVenta
{
    public Guid Id { get; set; }

    public Guid BorradorVentaId { get; set; }

    public Guid ProductoId { get; set; }

    public int Cantidad { get; set; }

    public string TipoPrecio { get; set; } = null!;

    public decimal PrecioUnitario { get; set; }

    public string? TipoDescuento { get; set; }

    public decimal? PorcentajeDescuento { get; set; }

    public decimal ValorDescuento { get; set; }

    public int Orden { get; set; }

    public DateTime FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual BorradorVenta BorradorVenta { get; set; } = null!;

    public virtual Producto Producto { get; set; } = null!;
}
