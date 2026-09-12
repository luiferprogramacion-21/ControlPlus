using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Apartado
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public Guid TurnoCajaId { get; set; }

    public Guid ClienteId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public Guid AutorizacionOperacionId { get; set; }

    public Guid? VentaEntregaId { get; set; }

    public string Prefijo { get; set; } = null!;

    public string Serie { get; set; } = null!;

    public long Consecutivo { get; set; }

    public DateTime FechaHora { get; set; }

    public DateOnly FechaLimite { get; set; }

    public string AmbitoDescuento { get; set; } = null!;

    public string? TipoDescuento { get; set; }

    public decimal? ValorDescuentoSolicitado { get; set; }

    public string? MotivoDescuento { get; set; }

    public Guid? AutorizacionDescuentoId { get; set; }

    public decimal PorcentajeMinimoInicial { get; set; }

    public decimal AbonoInicial { get; set; }

    public decimal SubtotalBruto { get; set; }

    public decimal DescuentoTotal { get; set; }

    public decimal? BaseGravableTotal { get; set; }

    public decimal? ImpuestoIncluidoTotal { get; set; }

    public bool IvaDiscriminadoCompleto { get; set; }

    public decimal Total { get; set; }

    public decimal SaldoPendiente { get; set; }

    public string Estado { get; set; } = null!;

    public DateTime? FechaPago { get; set; }

    public DateTime? FechaEntrega { get; set; }

    public Guid? UsuarioEntregaId { get; set; }

    public DateTime? FechaAnulacion { get; set; }

    public Guid? UsuarioAnulacionId { get; set; }

    public Guid? AutorizacionAnulacionId { get; set; }

    public Guid? MotivoAnulacionId { get; set; }

    public string? ObservacionAnulacion { get; set; }

    public decimal? TotalAbonadoCancelacion { get; set; }

    public decimal? ValorDevueltoCancelacion { get; set; }

    public decimal? ValorRetenidoCancelacion { get; set; }

    public long Version { get; set; }

    public virtual AutorizacionOperacion? AutorizacionAnulacion { get; set; }

    public virtual AutorizacionOperacion? AutorizacionDescuento { get; set; }

    public virtual AutorizacionOperacion AutorizacionOperacion { get; set; } = null!;

    public virtual Cliente Cliente { get; set; } = null!;

    public virtual ICollection<DetalleApartado> DetalleApartado { get; set; } = new List<DetalleApartado>();

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual MotivoOperacion? MotivoAnulacion { get; set; }

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual ICollection<PagoApartado> PagoApartado { get; set; } = new List<PagoApartado>();

    public virtual ICollection<ReembolsoApartado> ReembolsoApartado { get; set; } = new List<ReembolsoApartado>();

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;

    public virtual Usuario? UsuarioAnulacion { get; set; }

    public virtual Usuario? UsuarioEntrega { get; set; }

    public virtual Venta? VentaEntrega { get; set; }
}
