namespace ControlPlus.Domain.OfficialModel;

public sealed class AutorizacionCierreTurno
{
    public Guid TurnoCajaId { get; set; }
    public Guid AutorizacionId { get; set; }
    public Guid CorrelacionId { get; set; }
    public Guid UsuarioEjecutorId { get; set; }
    public DateTime FechaVinculacion { get; set; }
    public Guid InstalacionId { get; set; }
    public Guid EstablecimientoId { get; set; }
    public TurnoCaja TurnoCaja { get; set; } = null!;
    public AutorizacionOperacion Autorizacion { get; set; } = null!;
}
