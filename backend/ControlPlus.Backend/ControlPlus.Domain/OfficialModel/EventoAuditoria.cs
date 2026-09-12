using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class EventoAuditoria
{
    public Guid Id { get; set; }

    public Guid? UsuarioId { get; set; }

    public Guid? UsuarioAutorizadorId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public Guid InstalacionId { get; set; }

    public Guid? TerminalId { get; set; }

    public DateTime FechaHora { get; set; }

    public string Accion { get; set; } = null!;

    public string? Entidad { get; set; }

    public Guid? EntidadId { get; set; }

    public string Resultado { get; set; } = null!;

    public string? Motivo { get; set; }

    public string? DatosAnteriores { get; set; }

    public string? DatosNuevos { get; set; }

    public Guid CorrelacionId { get; set; }

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual Terminal? Terminal { get; set; }

    public virtual Usuario? Usuario { get; set; }

    public virtual Usuario? UsuarioAutorizador { get; set; }
}
