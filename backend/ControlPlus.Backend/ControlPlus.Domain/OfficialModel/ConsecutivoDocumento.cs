using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class ConsecutivoDocumento
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public string TipoDocumento { get; set; } = null!;

    public string Prefijo { get; set; } = null!;

    public string Serie { get; set; } = null!;

    public long UltimoNumero { get; set; }

    public long Version { get; set; }

    public virtual Instalacion Instalacion { get; set; } = null!;
}
