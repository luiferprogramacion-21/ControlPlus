using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class UnidadMedida
{
    public Guid Id { get; set; }

    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public string Simbolo { get; set; } = null!;

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public virtual ICollection<Producto> Producto { get; set; } = new List<Producto>();
}
