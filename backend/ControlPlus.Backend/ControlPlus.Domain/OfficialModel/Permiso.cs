using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Permiso
{
    public Guid Id { get; set; }

    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public string? Descripcion { get; set; }

    public string Modulo { get; set; } = null!;

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public virtual ICollection<AutorizacionOperacion> AutorizacionOperacion { get; set; } = new List<AutorizacionOperacion>();

    public virtual ICollection<RolPermiso> RolPermiso { get; set; } = new List<RolPermiso>();

    public virtual ICollection<UsuarioPermiso> UsuarioPermiso { get; set; } = new List<UsuarioPermiso>();
}
