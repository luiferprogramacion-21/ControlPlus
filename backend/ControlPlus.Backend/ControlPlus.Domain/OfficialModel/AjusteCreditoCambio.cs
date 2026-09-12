using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class AjusteCreditoCambio
{
    public Guid Id { get; set; }

    public Guid CreditoId { get; set; }

    public Guid CambioVentaId { get; set; }

    public Guid UsuarioId { get; set; }

    public string TipoAjuste { get; set; } = null!;

    public decimal Valor { get; set; }

    public decimal MontoAjustadoAnterior { get; set; }

    public decimal MontoAjustadoPosterior { get; set; }

    public decimal SaldoAnterior { get; set; }

    public decimal SaldoPosterior { get; set; }

    public DateTime FechaHora { get; set; }

    public Guid CorrelacionId { get; set; }

    public virtual CambioVenta CambioVenta { get; set; } = null!;

    public virtual Credito Credito { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
