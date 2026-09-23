using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class TurnoCaja
{
    public Guid Id { get; set; }

    public Guid CajaId { get; set; }

    public Guid TerminalId { get; set; }

    public string ModoOperacion { get; set; } = null!;

    public Guid UsuarioAperturaId { get; set; }

    public Guid? UsuarioResponsableId { get; set; }

    public Guid? UsuarioCierreId { get; set; }

    public Guid? MotivoDiferenciaId { get; set; }


    public DateOnly FechaOperativa { get; set; }

    public DateTime FechaHoraApertura { get; set; }

    public DateTime? FechaHoraCierre { get; set; }

    public decimal MontoInicial { get; set; }

    public decimal? EfectivoEsperado { get; set; }

    public decimal? EfectivoContado { get; set; }

    public decimal? Diferencia { get; set; }

    public decimal? DiferenciaTotal { get; set; }

    public string Estado { get; set; } = null!;

    public string? Observaciones { get; set; }

    public string? ObservacionDiferencia { get; set; }

    public long Version { get; set; }

    public virtual ICollection<Apartado> Apartado { get; set; } = new List<Apartado>();

    public virtual ICollection<BorradorVenta> BorradorVenta { get; set; } = new List<BorradorVenta>();

    public virtual Caja Caja { get; set; } = null!;

    public virtual AutorizacionCierreTurno? AutorizacionCierre { get; set; }

    public virtual ICollection<DetalleArqueoMedioPago> DetalleArqueoMedioPago { get; set; } = new List<DetalleArqueoMedioPago>();

    public virtual ICollection<CambioVenta> CambioVenta { get; set; } = new List<CambioVenta>();

    public virtual MotivoOperacion? MotivoDiferencia { get; set; }

    public virtual ICollection<MovimientoCaja> MovimientoCaja { get; set; } = new List<MovimientoCaja>();

    public virtual ICollection<PagoApartado> PagoApartado { get; set; } = new List<PagoApartado>();

    public virtual ICollection<PagoCambioVenta> PagoCambioVenta { get; set; } = new List<PagoCambioVenta>();

    public virtual ICollection<PagoCredito> PagoCredito { get; set; } = new List<PagoCredito>();

    public virtual ICollection<ReembolsoApartado> ReembolsoApartado { get; set; } = new List<ReembolsoApartado>();

    public virtual ICollection<SesionOperador> SesionOperador { get; set; } = new List<SesionOperador>();

    public virtual Terminal Terminal { get; set; } = null!;

    public virtual Usuario UsuarioApertura { get; set; } = null!;

    public virtual Usuario? UsuarioCierre { get; set; }

    public virtual Usuario? UsuarioResponsable { get; set; }

    public virtual ICollection<Venta> Venta { get; set; } = new List<Venta>();
}
