using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// Límites configurables por rol. En V1 el descuento máximo es 80, 20 y 5 para Administrador, Supervisor y Cajero.
/// </summary>
public partial class LimiteOperacionRol
{
    public Guid Id { get; set; }

    public Guid RolId { get; set; }

    public string CodigoOperacion { get; set; } = null!;

    public string TipoLimite { get; set; } = null!;

    public decimal ValorMaximo { get; set; }

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual Rol Rol { get; set; } = null!;
}
