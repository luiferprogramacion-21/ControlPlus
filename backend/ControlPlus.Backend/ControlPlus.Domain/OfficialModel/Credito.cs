using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Credito
{
    public Guid Id { get; set; }

    public Guid VentaId { get; set; }

    public Guid AutorizacionOperacionId { get; set; }

    public decimal MontoOriginal { get; set; }

    public decimal MontoAjustado { get; set; }

    public decimal SaldoPendiente { get; set; }

    public DateOnly FechaOtorgamiento { get; set; }

    public DateOnly FechaVencimiento { get; set; }

    public DateOnly? FechaPago { get; set; }

    public string Estado { get; set; } = null!;

    public long Version { get; set; }

    public virtual ICollection<AjusteCreditoCambio> AjusteCreditoCambio { get; set; } = new List<AjusteCreditoCambio>();

    public virtual AutorizacionOperacion AutorizacionOperacion { get; set; } = null!;

    public virtual ICollection<CambioVenta> CambioVenta { get; set; } = new List<CambioVenta>();

    public virtual ICollection<PagoCredito> PagoCredito { get; set; } = new List<PagoCredito>();

    public virtual Venta Venta { get; set; } = null!;
}
