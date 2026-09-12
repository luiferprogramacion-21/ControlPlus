using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// Credencial rapida Code 128. Solo conserva el hash y un fragmento no secreto; el token se imprime directamente y nunca entra en snapshots ni colas persistentes.
/// </summary>
public partial class CredencialUsuario
{
    public Guid Id { get; set; }

    public Guid UsuarioId { get; set; }

    public string TokenHash { get; set; } = null!;

    public string FragmentoVisible { get; set; } = null!;

    public string FormatoCodigo { get; set; } = null!;

    public string Estado { get; set; } = null!;

    public Guid EmitidaPorId { get; set; }

    public Guid? RevocadaPorId { get; set; }

    public DateTime FechaEmision { get; set; }

    public DateTime? FechaVencimiento { get; set; }

    public DateTime? FechaRevocacion { get; set; }

    public string? MotivoRevocacion { get; set; }

    public long Version { get; set; }

    public virtual ICollection<AutorizacionOperacion> AutorizacionOperacion { get; set; } = new List<AutorizacionOperacion>();

    public virtual Usuario EmitidaPor { get; set; } = null!;

    public virtual Usuario? RevocadaPor { get; set; }

    public virtual ICollection<SesionOperador> SesionOperador { get; set; } = new List<SesionOperador>();

    public virtual Usuario Usuario { get; set; } = null!;
}
