using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class PerfilImpresora
{
    public Guid Id { get; set; }

    public Guid TerminalId { get; set; }

    public string Nombre { get; set; } = null!;

    public string TipoImpresora { get; set; } = null!;

    public string NombreSistema { get; set; } = null!;

    public decimal? AnchoMm { get; set; }

    public decimal? AltoMm { get; set; }

    public int? ResolucionDpi { get; set; }

    public bool EsPredeterminada { get; set; }

    public bool Activo { get; set; }

    /// <summary>Solo un perfil de recibos puede activar el pulso ESC/POS del cajon.</summary>
    public bool CajonHabilitado { get; set; }

    /// <summary>Pin de salida del conector del cajon (2 o 5). Nulo hasta configurarlo.</summary>
    public short? CajonPin { get; set; }

    /// <summary>Duracion del pulso encendido en milisegundos, en pasos de 2 ms.</summary>
    public short CajonPulsoOnMs { get; set; } = 100;

    /// <summary>Duracion del pulso apagado en milisegundos, en pasos de 2 ms.</summary>
    public short CajonPulsoOffMs { get; set; } = 100;

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual Terminal Terminal { get; set; } = null!;

    public virtual ICollection<TrabajoImpresion> TrabajoImpresion { get; set; } = new List<TrabajoImpresion>();
}
