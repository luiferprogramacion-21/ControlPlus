using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class UsuarioLogin
{
    public string ProveedorLogin { get; set; } = null!;

    public string ClaveProveedor { get; set; } = null!;

    public Guid UsuarioId { get; set; }

    public string? NombreProveedor { get; set; }

    public virtual Usuario Usuario { get; set; } = null!;
}
