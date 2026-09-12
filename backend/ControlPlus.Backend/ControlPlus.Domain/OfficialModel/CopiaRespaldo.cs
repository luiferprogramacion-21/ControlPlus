using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class CopiaRespaldo
{
    public Guid Id { get; set; }

    public Guid EjecucionRespaldoId { get; set; }

    public Guid DestinoRespaldoId { get; set; }

    public string Estado { get; set; } = null!;

    public int Intentos { get; set; }

    public string? UbicacionFinal { get; set; }

    public bool Cifrado { get; set; }

    public bool HashVerificado { get; set; }

    public DateTime? FechaUltimoIntento { get; set; }

    public DateTime? FechaCompletada { get; set; }

    public string? ErrorSanitizado { get; set; }

    public virtual DestinoRespaldo DestinoRespaldo { get; set; } = null!;

    public virtual EjecucionRespaldo EjecucionRespaldo { get; set; } = null!;

    public virtual ICollection<PruebaRestauracion> PruebaRestauracion { get; set; } = new List<PruebaRestauracion>();
}
