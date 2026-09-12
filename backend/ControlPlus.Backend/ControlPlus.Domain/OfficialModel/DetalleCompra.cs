using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class DetalleCompra
{
    public Guid Id { get; set; }

    public Guid CompraId { get; set; }

    public Guid ProductoId { get; set; }

    public Guid? DetallePedidoCompraId { get; set; }

    public int Cantidad { get; set; }

    public string ModoIva { get; set; } = null!;

    public decimal CostoUnitarioDocumento { get; set; }

    public decimal? PorcentajeIva { get; set; }

    public decimal SubtotalBruto { get; set; }

    public decimal? BaseGravable { get; set; }

    public decimal? ValorIva { get; set; }

    public decimal TotalLinea { get; set; }

    public decimal? CostoPromedioAnterior { get; set; }

    public decimal CostoPromedioResultante { get; set; }

    public decimal PrecioVentaAnterior { get; set; }

    public decimal PrecioVentaNuevo { get; set; }

    public bool EsSustituto { get; set; }

    public Guid? AutorizacionExcepcionId { get; set; }

    public virtual AutorizacionOperacion? AutorizacionExcepcion { get; set; }

    public virtual Compra Compra { get; set; } = null!;

    public virtual DetallePedidoCompra? DetallePedidoCompra { get; set; }

    public virtual Producto Producto { get; set; } = null!;
}
