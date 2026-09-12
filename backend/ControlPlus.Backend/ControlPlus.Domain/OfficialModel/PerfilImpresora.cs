using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class PerfilImpresora
{
    public Guid Id { get; set; }

    public Guid TerminalId { get; set; }

    public string Nombre { get; set; } = null!;

    public string TipoImpresora { get; set; } = null!;

    public string NombreSistema { get; set; } = null!;

    public decimal? AnchoMm { get; set; }

    public decimal? AltoMm { get; set; }

    public int? ResolucionDpi { get; set; }

    public bool EsPredeterminada { get; set; }

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual Terminal Terminal { get; set; } = null!;

    public virtual ICollection<TrabajoImpresion> TrabajoImpresion { get; set; } = new List<TrabajoImpresion>();
}
