using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class MovimientoCaja
{
    public Guid Id { get; set; }

    public Guid TurnoCajaId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public Guid? MovimientoRevertidoId { get; set; }

    public string Tipo { get; set; } = null!;

    public string CategoriaMovimiento { get; set; } = null!;

    public decimal Valor { get; set; }

    public string? Concepto { get; set; }

    public DateTime FechaHora { get; set; }

    public Guid CorrelacionId { get; set; }

    public virtual MovimientoCaja? InverseMovimientoRevertido { get; set; }

    public virtual MovimientoCaja? MovimientoRevertido { get; set; }

    public virtual ICollection<PagoApartado> PagoApartado { get; set; } = new List<PagoApartado>();

    public virtual ICollection<PagoCambioVenta> PagoCambioVenta { get; set; } = new List<PagoCambioVenta>();

    public virtual ICollection<PagoCredito> PagoCredito { get; set; } = new List<PagoCredito>();

    public virtual ICollection<PagoVenta> PagoVenta { get; set; } = new List<PagoVenta>();

    public virtual ICollection<ReembolsoApartado> ReembolsoApartado { get; set; } = new List<ReembolsoApartado>();

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
