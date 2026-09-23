using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class MetodoPago
{
    public Guid Id { get; set; }

    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public bool AfectaEfectivo { get; set; }

    public bool RequiereReferencia { get; set; }

    public int OrdenVisual { get; set; }

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public long Version { get; set; }

    public virtual ICollection<PagoApartado> PagoApartado { get; set; } = new List<PagoApartado>();

    public virtual ICollection<DetalleArqueoMedioPago> DetalleArqueoMedioPago { get; set; } = new List<DetalleArqueoMedioPago>();

    public virtual ICollection<PagoCambioVenta> PagoCambioVenta { get; set; } = new List<PagoCambioVenta>();

    public virtual ICollection<PagoCredito> PagoCredito { get; set; } = new List<PagoCredito>();

    public virtual ICollection<PagoVenta> PagoVenta { get; set; } = new List<PagoVenta>();

    public virtual ICollection<ReembolsoApartado> ReembolsoApartado { get; set; } = new List<ReembolsoApartado>();
}
