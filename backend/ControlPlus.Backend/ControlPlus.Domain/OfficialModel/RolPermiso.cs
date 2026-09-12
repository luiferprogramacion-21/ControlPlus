using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class RolPermiso
{
    public Guid RolId { get; set; }

    public Guid PermisoId { get; set; }

    public DateTime FechaAsignacion { get; set; }

    public virtual Permiso Permiso { get; set; } = null!;

    public virtual Rol Rol { get; set; } = null!;
}
