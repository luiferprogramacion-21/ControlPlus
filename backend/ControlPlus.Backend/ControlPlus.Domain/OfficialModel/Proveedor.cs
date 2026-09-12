using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Proveedor
{
    public Guid Id { get; set; }

    public string? TipoDocumento { get; set; }

    public string? NumeroDocumento { get; set; }

    public string NombreRazonSocial { get; set; } = null!;

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

    public virtual ICollection<Compra> Compra { get; set; } = new List<Compra>();

    public virtual ICollection<PedidoCompra> PedidoCompra { get; set; } = new List<PedidoCompra>();

    public virtual ICollection<Producto> Producto { get; set; } = new List<Producto>();

    public virtual Usuario UsuarioCreacion { get; set; } = null!;

    public virtual Usuario? UsuarioModificacion { get; set; }
}
