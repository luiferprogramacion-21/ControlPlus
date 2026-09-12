using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Instalacion
{
    public Guid Id { get; set; }

    public Guid EstablecimientoId { get; set; }

    public string Codigo { get; set; } = null!;

    public string Serie { get; set; } = null!;

    public DateTime FechaInstalacion { get; set; }

    public string VersionAplicacion { get; set; } = null!;

    public string VersionEsquema { get; set; } = null!;

    public bool Activo { get; set; }

    public long Version { get; set; }

    public virtual ICollection<Apartado> Apartado { get; set; } = new List<Apartado>();

    public virtual ICollection<BorradorVenta> BorradorVenta { get; set; } = new List<BorradorVenta>();

    public virtual ICollection<Caja> Caja { get; set; } = new List<Caja>();

    public virtual ICollection<CambioVenta> CambioVenta { get; set; } = new List<CambioVenta>();

    public virtual ICollection<Compra> Compra { get; set; } = new List<Compra>();

    public virtual ICollection<ConsecutivoDocumento> ConsecutivoDocumento { get; set; } = new List<ConsecutivoDocumento>();

    public virtual ICollection<DestinoRespaldo> DestinoRespaldo { get; set; } = new List<DestinoRespaldo>();

    public virtual ICollection<DocumentoEmitido> DocumentoEmitido { get; set; } = new List<DocumentoEmitido>();

    public virtual ICollection<EjecucionRespaldo> EjecucionRespaldo { get; set; } = new List<EjecucionRespaldo>();

    public virtual Establecimiento Establecimiento { get; set; } = null!;

    public virtual ICollection<EventoAuditoria> EventoAuditoria { get; set; } = new List<EventoAuditoria>();

    public virtual ICollection<OperacionIdempotente> OperacionIdempotente { get; set; } = new List<OperacionIdempotente>();

    public virtual ICollection<PagoApartado> PagoApartado { get; set; } = new List<PagoApartado>();

    public virtual ICollection<PagoCredito> PagoCredito { get; set; } = new List<PagoCredito>();

    public virtual ICollection<PedidoCompra> PedidoCompra { get; set; } = new List<PedidoCompra>();

    public virtual ICollection<Terminal> Terminal { get; set; } = new List<Terminal>();

    public virtual ICollection<Venta> Venta { get; set; } = new List<Venta>();
}
