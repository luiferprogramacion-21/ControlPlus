using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Venta
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public Guid TurnoCajaId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public Guid? ClienteId { get; set; }

    public string Prefijo { get; set; } = null!;

    public string Serie { get; set; } = null!;

    public long Consecutivo { get; set; }

    public DateTime FechaHora { get; set; }

    public DateOnly FechaOperativa { get; set; }

    public DateOnly FechaLimiteCambio { get; set; }

    public string TipoVenta { get; set; } = null!;

    public string AmbitoDescuento { get; set; } = null!;

    public string? TipoDescuento { get; set; }

    public decimal? ValorDescuentoSolicitado { get; set; }

    public string? MotivoDescuento { get; set; }

    public Guid? AutorizacionDescuentoId { get; set; }

    public decimal SubtotalBruto { get; set; }

    public decimal DescuentoTotal { get; set; }

    public decimal? BaseGravableTotal { get; set; }

    public decimal? ImpuestoIncluidoTotal { get; set; }

    public bool IvaDiscriminadoCompleto { get; set; }

    /// <summary>
    /// Precio final en COP enteros. El IVA ya esta incluido y nunca se suma nuevamente.
    /// </summary>
    public decimal Total { get; set; }

    public string Estado { get; set; } = null!;

    public long Version { get; set; }

    public virtual AnulacionVenta? AnulacionVenta { get; set; }

    public virtual Apartado? Apartado { get; set; }

    public virtual AutorizacionOperacion? AutorizacionDescuento { get; set; }

    public virtual BorradorVenta? BorradorVenta { get; set; }

    public virtual ICollection<CambioVenta> CambioVenta { get; set; } = new List<CambioVenta>();

    public virtual Cliente? Cliente { get; set; }

    public virtual Credito? Credito { get; set; }

    public virtual ICollection<DetalleVenta> DetalleVenta { get; set; } = new List<DetalleVenta>();

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual ICollection<PagoVenta> PagoVenta { get; set; } = new List<PagoVenta>();

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
