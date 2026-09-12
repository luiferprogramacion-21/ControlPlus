using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class UsuarioRol
{
    public Guid UsuarioId { get; set; }

    public Guid RolId { get; set; }

    public Guid? AsignadoPorId { get; set; }

    public DateTime FechaAsignacion { get; set; }

    public virtual Usuario? AsignadoPor { get; set; }

    public virtual Rol Rol { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
