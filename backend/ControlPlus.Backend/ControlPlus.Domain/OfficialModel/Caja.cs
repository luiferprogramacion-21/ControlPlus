using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Caja
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual ICollection<TurnoCaja> TurnoCaja { get; set; } = new List<TurnoCaja>();
}
