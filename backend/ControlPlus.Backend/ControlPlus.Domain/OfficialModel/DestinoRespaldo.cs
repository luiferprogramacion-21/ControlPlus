using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// Configura copia local fisicamente separada y copia externa cifrada con retencion diaria, semanal y mensual.
/// </summary>
public partial class DestinoRespaldo
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public string Nombre { get; set; } = null!;

    public string TipoDestino { get; set; } = null!;

    public string UbicacionBase { get; set; } = null!;

    public bool RequiereCifrado { get; set; }

    public string? ReferenciaSecreto { get; set; }

    public int RetencionDiaria { get; set; }

    public int RetencionSemanal { get; set; }

    public int RetencionMensual { get; set; }

    public bool ConservarPreActualizacion { get; set; }

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual ICollection<CopiaRespaldo> CopiaRespaldo { get; set; } = new List<CopiaRespaldo>();

    public virtual Instalacion Instalacion { get; set; } = null!;
}
