using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class VwNecesidadReposicion
{
    public Guid? ProductoId { get; set; }

    public string? CodigoInterno { get; set; }

    public string? Producto { get; set; }

    public Guid? ProveedorId { get; set; }

    public string? Proveedor { get; set; }

    public int? StockDisponible { get; set; }

    public int? StockMinimo { get; set; }

    public int? PendienteEnPedidos { get; set; }

    public int? CantidadSugerida { get; set; }

    public string? EstadoProveedor { get; set; }

    public bool? PuedeGenerarPedido { get; set; }
}
