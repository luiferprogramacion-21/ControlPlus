using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class DetalleApartado
{
    public Guid Id { get; set; }

    public Guid ApartadoId { get; set; }

    public Guid ProductoId { get; set; }

    public string CodigoProducto { get; set; } = null!;

    public string NombreProducto { get; set; } = null!;

    public string UnidadMedida { get; set; } = null!;

    public int Cantidad { get; set; }

    public decimal PrecioReserva { get; set; }

    public decimal? CostoUnitario { get; set; }

    public decimal? PorcentajeIva { get; set; }

    public decimal SubtotalBruto { get; set; }

    public string OrigenDescuento { get; set; } = null!;

    public string? TipoDescuento { get; set; }

    public decimal? PorcentajeDescuento { get; set; }

    public decimal ValorDescuento { get; set; }

    public decimal? BaseGravable { get; set; }

    public decimal? ValorIva { get; set; }

    public decimal SubtotalNeto { get; set; }

    public virtual Apartado Apartado { get; set; } = null!;

    public virtual Producto Producto { get; set; } = null!;
}
