using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// Carrito recuperable despues de una interrupcion. No constituye una venta ni afecta caja o inventario.
/// </summary>
public partial class BorradorVenta
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public Guid TerminalId { get; set; }

    public Guid TurnoCajaId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public Guid? ClienteId { get; set; }

    public Guid? VentaConfirmadaId { get; set; }

    public string TipoVenta { get; set; } = null!;

    public string AmbitoDescuento { get; set; } = null!;

    public string? TipoDescuento { get; set; }

    public decimal? ValorDescuentoSolicitado { get; set; }

    public string? MotivoDescuento { get; set; }

    public string Estado { get; set; } = null!;

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public DateTime? FechaCierre { get; set; }

    public Guid CorrelacionId { get; set; }

    public long Version { get; set; }

    public virtual Cliente? Cliente { get; set; }

    public virtual ICollection<DetalleBorradorVenta> DetalleBorradorVenta { get; set; } = new List<DetalleBorradorVenta>();

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual Terminal Terminal { get; set; } = null!;

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;

    public virtual Venta? VentaConfirmada { get; set; }
}
