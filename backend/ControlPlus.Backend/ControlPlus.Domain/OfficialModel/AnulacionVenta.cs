using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class AnulacionVenta
{
    public Guid Id { get; set; }

    public Guid VentaId { get; set; }

    public Guid UsuarioSolicitanteId { get; set; }

    public Guid UsuarioAutorizadorId { get; set; }

    public Guid AutorizacionOperacionId { get; set; }

    public Guid MotivoOperacionId { get; set; }

    public DateTime FechaHora { get; set; }

    public string? Observacion { get; set; }

    public Guid CorrelacionId { get; set; }

    public virtual AutorizacionOperacion AutorizacionOperacion { get; set; } = null!;

    public virtual MotivoOperacion MotivoOperacion { get; set; } = null!;

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual Usuario UsuarioAutorizador { get; set; } = null!;

    public virtual Usuario UsuarioSolicitante { get; set; } = null!;

    public virtual Venta Venta { get; set; } = null!;
}
