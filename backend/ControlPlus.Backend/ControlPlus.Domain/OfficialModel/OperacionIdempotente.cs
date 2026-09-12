using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class OperacionIdempotente
{
    public Guid Id { get; set; }

    public Guid InstalacionId { get; set; }

    public Guid TerminalId { get; set; }

    public Guid UsuarioId { get; set; }

    public Guid? SesionOperadorId { get; set; }

    public string ClaveIdempotencia { get; set; } = null!;

    public string TipoOperacion { get; set; } = null!;

    public string HashSolicitud { get; set; } = null!;

    public string? Entidad { get; set; }

    public Guid? EntidadId { get; set; }

    public string? Resultado { get; set; }

    public Guid CorrelacionId { get; set; }

    public DateTime FechaRegistro { get; set; }

    public virtual Instalacion Instalacion { get; set; } = null!;

    public virtual SesionOperador? SesionOperador { get; set; }

    public virtual Terminal Terminal { get; set; } = null!;

    public virtual Usuario Usuario { get; set; } = null!;
}
