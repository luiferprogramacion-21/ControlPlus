using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Establecimiento
{
    public Guid Id { get; set; }

    public string NombreComercial { get; set; } = null!;

    public string Identificacion { get; set; } = null!;

    public string? Direccion { get; set; }

    public string? Telefono { get; set; }

    public string ZonaHoraria { get; set; } = null!;

    /// <summary>
    /// Opcional. NULL significa que no existe un IVA sugerido al crear productos.
    /// </summary>
    public decimal? IvaPredeterminado { get; set; }

    /// <summary>
    /// Opcional. Si es NULL, el usuario debe escoger la fecha limite de cada apartado.
    /// </summary>
    public int? DiasLimiteApartado { get; set; }

    /// <summary>
    /// Porcentaje minimo configurable; el valor inicial aprobado es 20 por ciento.
    /// </summary>
    public decimal PorcentajeMinimoApartado { get; set; }

    public int DiasLimiteCambio { get; set; }

    /// <summary>
    /// Modo aplicado al siguiente turno. No puede cambiarse mientras exista operacion activa.
    /// </summary>
    public string ModoTurnoPredeterminado { get; set; } = null!;

    public int RpoMinutos { get; set; }

    public int RtoMinutos { get; set; }

    /// <summary>
    /// Frecuencia maxima inicial de 90 dias, ademas de pruebas posteriores a cambios mayores.
    /// </summary>
    public int DiasPruebaRestauracion { get; set; }

    public string? EncabezadoComprobante { get; set; }

    public string? PieComprobante { get; set; }

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual ICollection<Instalacion> Instalacion { get; set; } = new List<Instalacion>();

    public virtual ICollection<MotivoOperacion> MotivoOperacion { get; set; } = new List<MotivoOperacion>();

    public virtual ICollection<PlantillaImpresion> PlantillaImpresion { get; set; } = new List<PlantillaImpresion>();
}
