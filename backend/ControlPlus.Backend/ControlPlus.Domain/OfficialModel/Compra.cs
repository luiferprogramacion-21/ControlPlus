using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Compra
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public Guid ProveedorId { get; set; }

    public Guid? PedidoCompraId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public string? Prefijo { get; set; }

    public string? Serie { get; set; }

    public long? Consecutivo { get; set; }

    /// <summary>
    /// Factura o referencia del proveedor opcional; si se informa, es unica para ese proveedor.
    /// </summary>
    public string? NumeroDocumentoProveedor { get; set; }

    public DateOnly FechaDocumento { get; set; }

    public DateTime FechaHoraRegistro { get; set; }

    public DateTime? FechaHoraConfirmacion { get; set; }

    public string ModoIva { get; set; } = null!;

    public decimal SubtotalBruto { get; set; }

    public decimal? ImpuestoTotal { get; set; }

    public decimal Total { get; set; }

    public string Estado { get; set; } = null!;

    public Guid? UsuarioAnulacionId { get; set; }

    public Guid? AutorizacionAnulacionId { get; set; }

    public Guid? MotivoAnulacionId { get; set; }

    public DateTime? FechaAnulacion { get; set; }

    public string? ObservacionAnulacion { get; set; }

    public long Version { get; set; }

    public virtual AutorizacionOperacion? AutorizacionAnulacion { get; set; }

    public virtual ICollection<DetalleCompra> DetalleCompra { get; set; } = new List<DetalleCompra>();

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual MotivoOperacion? MotivoAnulacion { get; set; }

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual PedidoCompra? PedidoCompra { get; set; }

    public virtual Proveedor Proveedor { get; set; } = null!;

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual Usuario Usuario { get; set; } = null!;

    public virtual Usuario? UsuarioAnulacion { get; set; }
}
