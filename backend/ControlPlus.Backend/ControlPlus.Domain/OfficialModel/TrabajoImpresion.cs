using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// Cola reintentable separada de la transaccion comercial; un fallo de impresion no revierte la venta.
/// </summary>
public partial class TrabajoImpresion
{
    public Guid Id { get; set; }

    public Guid DocumentoEmitidoId { get; set; }

    public Guid PerfilImpresoraId { get; set; }

    public Guid PlantillaImpresionId { get; set; }

    public Guid TerminalId { get; set; }

    public Guid SolicitadoPorId { get; set; }

    public string ClaveIdempotencia { get; set; } = null!;

    public string TipoCopia { get; set; } = null!;

    public int NumeroReimpresion { get; set; }

    public string Estado { get; set; } = null!;

    public int Intentos { get; set; }

    public byte[] ContenidoRenderizado { get; set; } = null!;

    public string HashContenido { get; set; } = null!;

    public DateTime FechaSolicitud { get; set; }

    public DateTime? FechaUltimoIntento { get; set; }

    public DateTime? FechaImpresion { get; set; }

    public string? ErrorSanitizado { get; set; }

    public virtual DocumentoEmitido DocumentoEmitido { get; set; } = null!;

    public virtual PerfilImpresora PerfilImpresora { get; set; } = null!;

    public virtual PlantillaImpresion PlantillaImpresion { get; set; } = null!;

    public virtual Usuario SolicitadoPor { get; set; } = null!;

    public virtual Terminal Terminal { get; set; } = null!;
}
