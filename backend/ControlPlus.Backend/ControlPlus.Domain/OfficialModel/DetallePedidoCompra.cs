using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class DetallePedidoCompra
{
    public Guid Id { get; set; }

    public Guid PedidoCompraId { get; set; }

    public Guid ProductoId { get; set; }

    public string CodigoProducto { get; set; } = null!;

    public string NombreProducto { get; set; } = null!;

    public int StockDisponibleOrigen { get; set; }

    public int StockMinimoOrigen { get; set; }

    public int PendienteOtrosPedidos { get; set; }

    public int CantidadSugerida { get; set; }

    public int CantidadSolicitada { get; set; }

    /// <summary>
    /// Cantidad recibida historicamente. Una anulacion posterior de la compra revierte inventario, pero no reescribe el pedido enviado.
    /// </summary>
    public int CantidadRecibida { get; set; }

    public int CantidadExcedente { get; set; }

    public Guid? AutorizacionExcesoId { get; set; }

    public string OrigenNecesidad { get; set; } = null!;

    public decimal? CostoEstimado { get; set; }

    public string? Observacion { get; set; }

    public long Version { get; set; }

    public virtual AutorizacionOperacion? AutorizacionExceso { get; set; }

    public virtual ICollection<DetalleCompra> DetalleCompra { get; set; } = new List<DetalleCompra>();

    public virtual PedidoCompra PedidoCompra { get; set; } = null!;

    public virtual Producto Producto { get; set; } = null!;
}
