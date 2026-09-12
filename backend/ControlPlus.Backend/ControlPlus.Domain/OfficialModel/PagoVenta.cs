using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class PagoVenta
{
    public Guid Id { get; set; }

    public Guid VentaId { get; set; }

    public Guid MetodoPagoId { get; set; }

    public Guid? MovimientoCajaId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public decimal ValorAplicado { get; set; }

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

    public virtual MetodoPago MetodoPago { get; set; } = null!;

    public virtual MotivoOperacion? MotivoAnulacion { get; set; }

    public virtual MovimientoCaja? MovimientoCaja { get; set; }

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual Usuario Usuario { get; set; } = null!;

    public virtual Usuario? UsuarioAnulacion { get; set; }

    public virtual Venta Venta { get; set; } = null!;
}
