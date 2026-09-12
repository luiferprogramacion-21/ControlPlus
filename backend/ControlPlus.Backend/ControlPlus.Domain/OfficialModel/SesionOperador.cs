using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class SesionOperador
{
    public Guid Id { get; set; }

    public Guid TurnoCajaId { get; set; }

    public Guid TerminalId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid CredencialUsuarioId { get; set; }

    public DateTime FechaInicio { get; set; }

    public DateTime FechaUltimoUso { get; set; }

    public DateTime? FechaFin { get; set; }

    public string Estado { get; set; } = null!;

    public string? MotivoCierre { get; set; }

    public Guid CorrelacionId { get; set; }

    public virtual ICollection<Apartado> Apartado { get; set; } = new List<Apartado>();

    public virtual ICollection<BorradorVenta> BorradorVenta { get; set; } = new List<BorradorVenta>();

    public virtual ICollection<CambioVenta> CambioVenta { get; set; } = new List<CambioVenta>();

    public virtual ICollection<Compra> Compra { get; set; } = new List<Compra>();

    public virtual CredencialUsuario CredencialUsuario { get; set; } = null!;

    public virtual ICollection<EventoAuditoria> EventoAuditoria { get; set; } = new List<EventoAuditoria>();

    public virtual ICollection<MovimientoCaja> MovimientoCaja { get; set; } = new List<MovimientoCaja>();

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual ICollection<OperacionIdempotente> OperacionIdempotente { get; set; } = new List<OperacionIdempotente>();

    public virtual ICollection<PagoApartado> PagoApartado { get; set; } = new List<PagoApartado>();

    public virtual ICollection<PagoCambioVenta> PagoCambioVenta { get; set; } = new List<PagoCambioVenta>();

    public virtual ICollection<PagoCredito> PagoCredito { get; set; } = new List<PagoCredito>();

    public virtual ICollection<PagoVenta> PagoVenta { get; set; } = new List<PagoVenta>();

    public virtual ICollection<ReembolsoApartado> ReembolsoApartado { get; set; } = new List<ReembolsoApartado>();

    public virtual Terminal Terminal { get; set; } = null!;

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;

    public virtual Venta? Venta { get; set; }
}
