namespace ControlPlus.Domain.OfficialModel;

/// <summary>
/// Intento de envio del pulso del cajon. Se registra antes de hablar con la impresora;
/// nunca se reintenta de forma automatica porque el resultado fisico puede ser incierto.
/// </summary>
public sealed class AperturaCajon
{
    public Guid Id { get; set; }
    public Guid? PerfilImpresoraId { get; set; }
    public Guid TerminalId { get; set; }
    public Guid TurnoCajaId { get; set; }
    public Guid SolicitadoPorId { get; set; }
    public string TipoOperacion { get; set; } = null!;
    public Guid OperacionId { get; set; }
    public string Estado { get; set; } = "INTENTANDO";
    public string? Motivo { get; set; }
    public DateTime FechaSolicitud { get; set; }
    public DateTime? FechaResultado { get; set; }
    public string? ErrorSanitizado { get; set; }

    public PerfilImpresora? PerfilImpresora { get; set; }
    public Terminal Terminal { get; set; } = null!;
    public TurnoCaja TurnoCaja { get; set; } = null!;
    public Usuario SolicitadoPor { get; set; } = null!;
}
