using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Cliente
{
    public Guid Id { get; set; }

    public string? TipoDocumento { get; set; }

    public string? NumeroDocumento { get; set; }

    public string NombreCompleto { get; set; } = null!;

    public string? Telefono { get; set; }

    public string? Direccion { get; set; }

    public string? Correo { get; set; }

    public string? Observaciones { get; set; }

    public bool Activo { get; set; }

    public Guid UsuarioCreacionId { get; set; }

    public Guid? UsuarioModificacionId { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual ICollection<Apartado> Apartado { get; set; } = new List<Apartado>();

    public virtual ICollection<BorradorVenta> BorradorVenta { get; set; } = new List<BorradorVenta>();

    public virtual Usuario UsuarioCreacion { get; set; } = null!;

    public virtual Usuario? UsuarioModificacion { get; set; }

    public virtual ICollection<Venta> Venta { get; set; } = new List<Venta>();
}
