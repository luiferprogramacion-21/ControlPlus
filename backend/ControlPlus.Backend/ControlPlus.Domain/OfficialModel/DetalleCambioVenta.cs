using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class DetalleCambioVenta
{
    public Guid Id { get; set; }

    public Guid CambioVentaId { get; set; }

    public Guid? DetalleVentaOrigenId { get; set; }

    public Guid ProductoId { get; set; }

    public string TipoDetalle { get; set; } = null!;

    public string CodigoProducto { get; set; } = null!;

    public string NombreProducto { get; set; } = null!;

    public string UnidadMedida { get; set; } = null!;

    public int Cantidad { get; set; }

    public decimal PrecioUnitario { get; set; }

    public decimal? PorcentajeIva { get; set; }

    public decimal ValorDescuento { get; set; }

    public decimal? BaseGravable { get; set; }

    public decimal? ValorIva { get; set; }

    public decimal SubtotalNeto { get; set; }

    public virtual CambioVenta CambioVenta { get; set; } = null!;

    public virtual DetalleVenta? DetalleVentaOrigen { get; set; }

    public virtual Producto Producto { get; set; } = null!;
}
