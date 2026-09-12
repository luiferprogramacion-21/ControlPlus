using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class PagoCambioVenta
{
    public Guid Id { get; set; }

    public Guid CambioVentaId { get; set; }

    public Guid TurnoCajaId { get; set; }

    public Guid MetodoPagoId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public Guid? MovimientoCajaId { get; set; }

    public string TipoMovimiento { get; set; } = null!;

    public decimal Valor { get; set; }

    public decimal? ValorRecibido { get; set; }

    public decimal Cambio { get; set; }

    public string? Referencia { get; set; }

    public DateTime FechaHora { get; set; }

    public string Estado { get; set; } = null!;

    public Guid? UsuarioAnulacionId { get; set; }

    public Guid? AutorizacionAnulacionId { get; set; }

    public Guid? MotivoAnulacionId { get; set; }

    public DateTime? FechaAnulacion { get; set; }

    public string? ObservacionAnulacion { get; set; }

    public virtual AutorizacionOperacion? AutorizacionAnulacion { get; set; }

    public virtual CambioVenta CambioVenta { get; set; } = null!;

    public virtual MetodoPago MetodoPago { get; set; } = null!;

    public virtual MotivoOperacion? MotivoAnulacion { get; set; }

    public virtual MovimientoCaja? MovimientoCaja { get; set; }

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual TurnoCaja TurnoCaja { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;

    public virtual Usuario? UsuarioAnulacion { get; set; }
}
