using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class MovimientoInventario
{
    public Guid Id { get; set; }

    public Guid ProductoId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public Guid? AutorizacionOperacionId { get; set; }

    public Guid? MotivoOperacionId { get; set; }

    public Guid? VentaId { get; set; }

    public Guid? CompraId { get; set; }

    public Guid? ApartadoId { get; set; }

    public Guid? CambioVentaId { get; set; }

    public Guid? AnulacionVentaId { get; set; }

    public Guid? MovimientoRevertidoId { get; set; }

    public string TipoMovimiento { get; set; } = null!;

    public int CantidadStock { get; set; }

    public int CantidadReservada { get; set; }

    public int StockAnterior { get; set; }

    public int StockPosterior { get; set; }

    public int ReservadoAnterior { get; set; }

    public int ReservadoPosterior { get; set; }

    public decimal? CostoPromedioAnterior { get; set; }

    public decimal? CostoPromedioPosterior { get; set; }

    public DateTime FechaHora { get; set; }

    public string? Motivo { get; set; }

    public Guid CorrelacionId { get; set; }

    public virtual AnulacionVenta? AnulacionVenta { get; set; }

    public virtual Apartado? Apartado { get; set; }

    public virtual AutorizacionOperacion? AutorizacionOperacion { get; set; }

    public virtual CambioVenta? CambioVenta { get; set; }

    public virtual Compra? Compra { get; set; }

    public virtual MovimientoInventario? InverseMovimientoRevertido { get; set; }

    public virtual MotivoOperacion? MotivoOperacion { get; set; }

    public virtual MovimientoInventario? MovimientoRevertido { get; set; }

    public virtual Producto Producto { get; set; } = null!;

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual Usuario Usuario { get; set; } = null!;

    public virtual Venta? Venta { get; set; }
}
