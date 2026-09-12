using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class AutorizacionOperacion
{
    public Guid Id { get; set; }

    public Guid UsuarioSolicitanteId { get; set; }

    public Guid? UsuarioAutorizadorId { get; set; }

    public Guid? CredencialAutorizadorId { get; set; }

    public Guid PermisoId { get; set; }

    public string TipoOperacion { get; set; } = null!;

    public Guid CorrelacionId { get; set; }

    public string DescripcionOperacion { get; set; } = null!;

    public string? Motivo { get; set; }

    public string? MetodoAutenticacion { get; set; }

    public string? Resultado { get; set; }

    public string Estado { get; set; } = null!;

    public DateTime FechaSolicitud { get; set; }

    public DateTime FechaExpiracion { get; set; }

    public DateTime? FechaUtilizacion { get; set; }

    public virtual AnulacionVenta? AnulacionVenta { get; set; }

    public virtual ICollection<Apartado> ApartadoAutorizacionAnulacion { get; set; } = new List<Apartado>();

    public virtual ICollection<Apartado> ApartadoAutorizacionDescuento { get; set; } = new List<Apartado>();

    public virtual Apartado? ApartadoAutorizacionOperacion { get; set; }

    public virtual ICollection<CambioVenta> CambioVentaAutorizacionAnulacion { get; set; } = new List<CambioVenta>();

    public virtual CambioVenta? CambioVentaAutorizacionOperacion { get; set; }

    public virtual ICollection<Compra> Compra { get; set; } = new List<Compra>();

    public virtual CredencialUsuario? CredencialAutorizador { get; set; }

    public virtual Credito? Credito { get; set; }

    public virtual ICollection<DetalleCompra> DetalleCompra { get; set; } = new List<DetalleCompra>();

    public virtual ICollection<DetallePedidoCompra> DetallePedidoCompra { get; set; } = new List<DetallePedidoCompra>();

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual ICollection<PagoApartado> PagoApartado { get; set; } = new List<PagoApartado>();

    public virtual ICollection<PagoCambioVenta> PagoCambioVenta { get; set; } = new List<PagoCambioVenta>();

    public virtual ICollection<PagoCredito> PagoCredito { get; set; } = new List<PagoCredito>();

    public virtual ICollection<PagoVenta> PagoVenta { get; set; } = new List<PagoVenta>();

    public virtual ICollection<PedidoCompra> PedidoCompraAutorizacionCancelacion { get; set; } = new List<PedidoCompra>();

    public virtual ICollection<PedidoCompra> PedidoCompraAutorizacionCierre { get; set; } = new List<PedidoCompra>();

    public virtual Permiso Permiso { get; set; } = null!;

    public virtual ICollection<PruebaRestauracion> PruebaRestauracion { get; set; } = new List<PruebaRestauracion>();

    public virtual Usuario? UsuarioAutorizador { get; set; }

    public virtual Usuario UsuarioSolicitante { get; set; } = null!;

    public virtual ICollection<Venta> Venta { get; set; } = new List<Venta>();
}
