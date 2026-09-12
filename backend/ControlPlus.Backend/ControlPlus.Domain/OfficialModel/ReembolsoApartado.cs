using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class ReembolsoApartado
{
    public Guid Id { get; set; }

    public Guid ApartadoId { get; set; }

    public Guid TurnoCajaId { get; set; }

    public Guid MetodoPagoId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public Guid? MovimientoCajaId { get; set; }

    public decimal Valor { get; set; }

    public string? Referencia { get; set; }

    public DateTime FechaHora { get; set; }

    public Guid CorrelacionId { get; set; }

    public virtual Apartado Apartado { get; set; } = null!;

    public virtual MetodoPago MetodoPago { get; set; } = null!;

    public virtual MovimientoCaja? MovimientoCaja { get; set; }

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
