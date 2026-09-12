using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class UsuarioToken
{
    public Guid UsuarioId { get; set; }

    public string ProveedorLogin { get; set; } = null!;

    public string NombreToken { get; set; } = null!;

    public string? ValorToken { get; set; }

    public virtual Usuario Usuario { get; set; } = null!;
}
