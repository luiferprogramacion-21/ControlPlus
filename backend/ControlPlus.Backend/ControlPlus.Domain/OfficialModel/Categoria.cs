using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Categoria
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = null!;

    public string? Descripcion { get; set; }

    public bool Activo { get; set; }

    public Guid UsuarioCreacionId { get; set; }

    public Guid? UsuarioModificacionId { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual ICollection<Producto> Producto { get; set; } = new List<Producto>();

    public virtual Usuario UsuarioCreacion { get; set; } = null!;

    public virtual Usuario? UsuarioModificacion { get; set; }
}
