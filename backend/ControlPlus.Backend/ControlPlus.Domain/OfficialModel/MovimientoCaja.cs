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

    public virtual PagoApartado? PagoApartado { get; set; }

    public virtual PagoCambioVenta? PagoCambioVenta { get; set; }

    public virtual PagoCredito? PagoCredito { get; set; }

    public virtual PagoVenta? PagoVenta { get; set; }

    public virtual ReembolsoApartado? ReembolsoApartado { get; set; }

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
