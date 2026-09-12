using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Producto
{
    public Guid Id { get; set; }

    public Guid CategoriaId { get; set; }

    public Guid UnidadMedidaId { get; set; }

    /// <summary>
    /// Unico proveedor asignado al producto en V1. NULL se muestra como Sin proveedor.
    /// </summary>
    public Guid? ProveedorId { get; set; }

    public string CodigoInterno { get; set; } = null!;

    public string? CodigoBarras { get; set; }

    public string? FormatoCodigoBarras { get; set; }

    public bool CodigoBarrasGenerado { get; set; }

    public string Nombre { get; set; } = null!;

    public string? Descripcion { get; set; }

    /// <summary>
    /// Precio final de venta en COP enteros con IVA incluido cuando este se conoce.
    /// </summary>
    public decimal PrecioMinorista { get; set; }

    /// <summary>
    /// Precio final mayorista opcional en COP enteros; nunca se le suma IVA en caja.
    /// </summary>
    public decimal? PrecioMayorista { get; set; }

    /// <summary>
    /// NULL significa costo aun desconocido; cero representa un costo conocido de cero.
    /// </summary>
    public decimal? CostoPromedio { get; set; }

    /// <summary>
    /// NULL significa IVA no especificado; 0 significa IVA conocido de cero por ciento.
    /// </summary>
    public decimal? PorcentajeIva { get; set; }

    public int StockActual { get; set; }

    public int StockReservado { get; set; }

    public int StockMinimo { get; set; }

    public bool Activo { get; set; }

    public Guid UsuarioCreacionId { get; set; }

    public Guid? UsuarioModificacionId { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual Categoria Categoria { get; set; } = null!;

    public virtual ICollection<DetalleApartado> DetalleApartado { get; set; } = new List<DetalleApartado>();

    public virtual ICollection<DetalleBorradorVenta> DetalleBorradorVenta { get; set; } = new List<DetalleBorradorVenta>();

    public virtual ICollection<DetalleCambioVenta> DetalleCambioVenta { get; set; } = new List<DetalleCambioVenta>();

    public virtual ICollection<DetalleCompra> DetalleCompra { get; set; } = new List<DetalleCompra>();

    public virtual ICollection<DetallePedidoCompra> DetallePedidoCompra { get; set; } = new List<DetallePedidoCompra>();

    public virtual ICollection<DetalleVenta> DetalleVenta { get; set; } = new List<DetalleVenta>();

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual Proveedor? Proveedor { get; set; }

    public virtual UnidadMedida UnidadMedida { get; set; } = null!;

    public virtual Usuario UsuarioCreacion { get; set; } = null!;

    public virtual Usuario? UsuarioModificacion { get; set; }
}
