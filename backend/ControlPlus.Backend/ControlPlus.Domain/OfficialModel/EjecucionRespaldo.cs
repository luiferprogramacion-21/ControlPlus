using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class EjecucionRespaldo
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public DateTime FechaInicio { get; set; }

    public DateTime? FechaFin { get; set; }

    public string Estrategia { get; set; } = null!;

    public string Disparador { get; set; } = null!;

    public string Resultado { get; set; } = null!;

    public long? TamanoBytes { get; set; }

    public string? HashIntegridad { get; set; }

    public string? RutaArtefactoLocal { get; set; }

    public string? InicioWal { get; set; }

    public string? FinWal { get; set; }

    public string? ErrorSanitizado { get; set; }

    public virtual ICollection<CopiaRespaldo> CopiaRespaldo { get; set; } = new List<CopiaRespaldo>();

    public virtual Instalacion Instalacion { get; set; } = null!;
}
