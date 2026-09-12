using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// Pedido generado por el proveedor actualmente asignado a los productos; cada recepcion confirmada crea una compra.
/// </summary>
public partial class PedidoCompra
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public Guid ProveedorId { get; set; }

    public Guid UsuarioCreacionId { get; set; }

    public string Prefijo { get; set; } = null!;

    public string Serie { get; set; } = null!;

    public long Consecutivo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaEnvio { get; set; }

    public Guid? UsuarioEnvioId { get; set; }

    public DateTime? FechaCierre { get; set; }

    public Guid? UsuarioCierreId { get; set; }

    public Guid? AutorizacionCierreId { get; set; }

    public Guid? MotivoCierreId { get; set; }

    public string? ObservacionCierre { get; set; }

    public DateTime? FechaCancelacion { get; set; }

    public Guid? UsuarioCancelacionId { get; set; }

    public Guid? AutorizacionCancelacionId { get; set; }

    public Guid? MotivoCancelacionId { get; set; }

    public string? ObservacionCancelacion { get; set; }

    public string Estado { get; set; } = null!;

    public long Version { get; set; }

    public virtual AutorizacionOperacion? AutorizacionCancelacion { get; set; }

    public virtual AutorizacionOperacion? AutorizacionCierre { get; set; }

    public virtual ICollection<Compra> Compra { get; set; } = new List<Compra>();

    public virtual ICollection<DetallePedidoCompra> DetallePedidoCompra { get; set; } = new List<DetallePedidoCompra>();

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual MotivoOperacion? MotivoCancelacion { get; set; }

    public virtual MotivoOperacion? MotivoCierre { get; set; }

    public virtual Proveedor Proveedor { get; set; } = null!;

    public virtual Usuario? UsuarioCancelacion { get; set; }

    public virtual Usuario? UsuarioCierre { get; set; }

    public virtual Usuario UsuarioCreacion { get; set; } = null!;

    public virtual Usuario? UsuarioEnvio { get; set; }
}
