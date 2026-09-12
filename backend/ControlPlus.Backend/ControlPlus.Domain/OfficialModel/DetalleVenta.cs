using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class DetalleVenta
{
    public Guid Id { get; set; }

    public Guid VentaId { get; set; }

    public Guid ProductoId { get; set; }

    public string CodigoProducto { get; set; } = null!;

    public string NombreProducto { get; set; } = null!;

    public string UnidadMedida { get; set; } = null!;

    public int Cantidad { get; set; }

    public string TipoPrecio { get; set; } = null!;

    public decimal PrecioUnitario { get; set; }

    public decimal? CostoUnitario { get; set; }

    public decimal? PorcentajeIva { get; set; }

    public decimal SubtotalBruto { get; set; }

    public string OrigenDescuento { get; set; } = null!;

    public string? TipoDescuento { get; set; }

    public decimal? PorcentajeDescuento { get; set; }

    public decimal ValorDescuento { get; set; }

    public decimal? BaseGravable { get; set; }

    public decimal? ValorIva { get; set; }

    public decimal SubtotalNeto { get; set; }

    public virtual ICollection<DetalleCambioVenta> DetalleCambioVenta { get; set; } = new List<DetalleCambioVenta>();

    public virtual Producto Producto { get; set; } = null!;

    public virtual Venta Venta { get; set; } = null!;
}
