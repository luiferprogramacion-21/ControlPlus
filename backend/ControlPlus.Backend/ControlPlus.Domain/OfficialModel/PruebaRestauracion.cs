using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class PruebaRestauracion
{
    public Guid Id { get; set; }

    public Guid CopiaRespaldoId { get; set; }

    public Guid EjecutadaPorId { get; set; }

    public Guid AutorizacionOperacionId { get; set; }

    public DateTime FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }

    public string Resultado { get; set; } = null!;

    public int? RpoObservadoMinutos { get; set; }

    public int? RtoObservadoMinutos { get; set; }

    public bool IntegridadVerificada { get; set; }

    public bool AplicacionVerificada { get; set; }

    public string? Observaciones { get; set; }

    public string? ErrorSanitizado { get; set; }

    public virtual AutorizacionOperacion AutorizacionOperacion { get; set; } = null!;

    public virtual CopiaRespaldo CopiaRespaldo { get; set; } = null!;

    public virtual Usuario EjecutadaPor { get; set; } = null!;
}
