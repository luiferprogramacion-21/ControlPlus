using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// La aplicacion crea Administrador, Supervisor y Cajero como plantillas iniciales; sus permisos son editables.
/// </summary>
public partial class Rol
{
    public Guid Id { get; set; }

    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public string? Descripcion { get; set; }

    public bool EsPredefinido { get; set; }

    public bool PermisosEditables { get; set; }

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual ICollection<LimiteOperacionRol> LimiteOperacionRol { get; set; } = new List<LimiteOperacionRol>();

    public virtual ICollection<RolClaim> RolClaim { get; set; } = new List<RolClaim>();

    public virtual ICollection<RolPermiso> RolPermiso { get; set; } = new List<RolPermiso>();

    public virtual ICollection<UsuarioRol> UsuarioRol { get; set; } = new List<UsuarioRol>();
}
