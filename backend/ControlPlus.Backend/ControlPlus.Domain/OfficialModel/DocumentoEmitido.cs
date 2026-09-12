using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// Snapshot inmutable de datos y plantilla para que una reimpresion no cambie el documento historico.
/// </summary>
public partial class DocumentoEmitido
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public Guid TerminalId { get; set; }

    public Guid EmitidoPorId { get; set; }

    public string TipoDocumento { get; set; } = null!;

    public string EntidadTipo { get; set; } = null!;

    public Guid EntidadId { get; set; }

    public string? NumeroVisible { get; set; }

    public int VersionDocumento { get; set; }

    public string DatosSnapshot { get; set; } = null!;

    public string PlantillaSnapshot { get; set; } = null!;

    public string HashSnapshot { get; set; } = null!;

    public DateTime FechaEmision { get; set; }

    public virtual Usuario EmitidoPor { get; set; } = null!;

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual Terminal Terminal { get; set; } = null!;

    public virtual ICollection<TrabajoImpresion> TrabajoImpresion { get; set; } = new List<TrabajoImpresion>();
}
