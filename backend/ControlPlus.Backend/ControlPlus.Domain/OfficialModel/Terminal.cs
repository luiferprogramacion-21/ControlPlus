using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Terminal
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public string Codigo { get; set; } = null!;

    public string Nombre { get; set; } = null!;

    public bool Activo { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual BorradorVenta? BorradorVenta { get; set; }

    public virtual ICollection<DocumentoEmitido> DocumentoEmitido { get; set; } = new List<DocumentoEmitido>();

    public virtual ICollection<EventoAuditoria> EventoAuditoria { get; set; } = new List<EventoAuditoria>();

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual ICollection<OperacionIdempotente> OperacionIdempotente { get; set; } = new List<OperacionIdempotente>();

    public virtual ICollection<PerfilImpresora> PerfilImpresora { get; set; } = new List<PerfilImpresora>();

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual ICollection<TrabajoImpresion> TrabajoImpresion { get; set; } = new List<TrabajoImpresion>();

    public virtual ICollection<TurnoCaja> TurnoCaja { get; set; } = new List<TurnoCaja>();
}
