using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class RolClaim
{
    public Guid Id { get; set; }

    public Guid RolId { get; set; }

    public string TipoClaim { get; set; } = null!;

    public string ValorClaim { get; set; } = null!;

    public virtual Rol Rol { get; set; } = null!;
}
