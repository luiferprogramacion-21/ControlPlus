using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class PlantillaImpresion
{
    public Guid Id { get; set; }

    public Guid EstablecimientoId { get; set; }

    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public string TipoSalida { get; set; } = null!;

    public string? FormatoCodigo { get; set; }

    public decimal? AnchoMm { get; set; }

    public decimal? AltoMm { get; set; }

    public string Configuracion { get; set; } = null!;

    public bool Activa { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual Establecimiento Establecimiento { get; set; } = null!;

    public virtual ICollection<TrabajoImpresion> TrabajoImpresion { get; set; } = new List<TrabajoImpresion>();
}
