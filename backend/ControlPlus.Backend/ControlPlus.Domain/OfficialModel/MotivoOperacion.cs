using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class MotivoOperacion
{
    public Guid Id { get; set; }

    public Guid EstablecimientoId { get; set; }

    public string TipoOperacion { get; set; } = null!;

    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public string? Descripcion { get; set; }

    public int OrdenVisual { get; set; }

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual ICollection<AnulacionVenta> AnulacionVenta { get; set; } = new List<AnulacionVenta>();

    public virtual ICollection<Apartado> Apartado { get; set; } = new List<Apartado>();

    public virtual ICollection<CambioVenta> CambioVenta { get; set; } = new List<CambioVenta>();

    public virtual ICollection<Compra> Compra { get; set; } = new List<Compra>();

    public virtual Establecimiento Establecimiento { get; set; } = null!;

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual ICollection<PagoApartado> PagoApartado { get; set; } = new List<PagoApartado>();

    public virtual ICollection<PagoCambioVenta> PagoCambioVenta { get; set; } = new List<PagoCambioVenta>();

    public virtual ICollection<PagoCredito> PagoCredito { get; set; } = new List<PagoCredito>();

    public virtual ICollection<PagoVenta> PagoVenta { get; set; } = new List<PagoVenta>();

    public virtual ICollection<PedidoCompra> PedidoCompraMotivoCancelacion { get; set; } = new List<PedidoCompra>();

    public virtual ICollection<PedidoCompra> PedidoCompraMotivoCierre { get; set; } = new List<PedidoCompra>();

    public virtual ICollection<TurnoCaja> TurnoCaja { get; set; } = new List<TurnoCaja>();
}
