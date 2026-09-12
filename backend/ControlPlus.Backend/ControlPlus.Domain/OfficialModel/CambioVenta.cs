using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class CambioVenta
{
    public Guid Id { get; set; }

    public Guid VentaId { get; set; }

    public Guid? CreditoId { get; set; }

    public Guid InstalacionId { get; set; }

    public Guid TurnoCajaId { get; set; }

    public Guid AutorizacionOperacionId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public string Prefijo { get; set; } = null!;

    public string Serie { get; set; } = null!;

    public long Consecutivo { get; set; }

    public DateTime FechaHora { get; set; }

    public string? Motivo { get; set; }

    public decimal TotalDevuelto { get; set; }

    public decimal TotalEntregado { get; set; }

    public decimal Diferencia { get; set; }

    public string TratamientoDiferencia { get; set; } = null!;

    public decimal ValorAjusteCredito { get; set; }

    public decimal ValorPagoODevolucion { get; set; }

    public string Estado { get; set; } = null!;

    public Guid? UsuarioAnulacionId { get; set; }

    public Guid? AutorizacionAnulacionId { get; set; }

    public Guid? MotivoAnulacionId { get; set; }

    public DateTime? FechaAnulacion { get; set; }

    public string? ObservacionAnulacion { get; set; }

    public Guid CorrelacionId { get; set; }

    public virtual AjusteCreditoCambio? AjusteCreditoCambio { get; set; }

    public virtual AutorizacionOperacion? AutorizacionAnulacion { get; set; }

    public virtual AutorizacionOperacion AutorizacionOperacion { get; set; } = null!;

    public virtual Credito? Credito { get; set; }

    public virtual ICollection<DetalleCambioVenta> DetalleCambioVenta { get; set; } = new List<DetalleCambioVenta>();

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual MotivoOperacion? MotivoAnulacion { get; set; }

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual ICollection<PagoCambioVenta> PagoCambioVenta { get; set; } = new List<PagoCambioVenta>();

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;

    public virtual Usuario? UsuarioAnulacion { get; set; }

    public virtual Venta Venta { get; set; } = null!;
}
