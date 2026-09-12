using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class UsuarioClaim
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }

    public string TipoClaim { get; set; } = null!;

    public string ValorClaim { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
